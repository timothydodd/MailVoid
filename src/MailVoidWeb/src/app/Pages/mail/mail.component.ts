import { Component, computed, inject, signal, ChangeDetectionStrategy } from '@angular/core';
import { takeUntilDestroyed, toObservable } from '@angular/core/rxjs-interop';
import { LucideDynamicIcon } from '@lucide/angular';
import {
  auditTime,
  catchError,
  combineLatest,
  debounceTime,
  distinctUntilChanged,
  filter,
  forkJoin,
  map,
  of,
  Subject,
  switchMap,
} from 'rxjs';
import { MailSettingsModalComponent } from '../../_components/mail-settings-modal/mail-settings-modal.component';
import { ModalContainerService } from '@rd-ui';
import {
  FilterOptions,
  MailBox,
  MailBoxGroups,
  MailGroup,
  MailService,
  MailSortColumn,
  MailSortDirection,
  MailWithReadStatus,
} from '../../_services/api/mail.service';
import { LastSeenService } from '../../_services/last-seen.service';
import { MailSearchService } from '../../_services/mail-search.service';
import { MobileMenuService } from '../../_services/mobile-menu.service';
import { MailNotification, SignalRService } from '../../services/signalr.service';
import { BoxListComponent } from './box-list/box-list.component';
import { MailListComponent } from './mail-list/mail-list.component';
import { MailPanelComponent } from './mail-panel/mail-panel.component';

type SortColumn = MailSortColumn;
type SortDirection = MailSortDirection;

// Minimum gap between live (SignalR-driven) refreshes of the list and sidebar.
const LIVE_REFRESH_INTERVAL_MS = 1000;

@Component({
  selector: 'app-mail',
  standalone: true,
  imports: [BoxListComponent, LucideDynamicIcon, MailListComponent, MailPanelComponent],
  template: `
    <div class="mail-layout">
      <aside class="mail-sidebar side-nav" [class.open]="isMobileMenuOpen()" aria-label="Mailboxes">
        <div class="mail-sidebar-mobile-head">
          <span>Mailboxes</span>
          <button type="button" class="icon-btn" (click)="closeMobileMenu()" aria-label="Close mailboxes">
            <svg lucideIcon="x" size="18"></svg>
          </button>
        </div>
        <div class="side-nav-body">
          <button
            type="button"
            class="side-item"
            [class.selected]="selectedBox() === null"
            (click)="onMobileBoxSelect(null)"
          >
            <svg lucideIcon="inbox" size="16"></svg>
            <span class="side-item-label">All mail</span>
            @if (totalUnread() > 0) {
              <span class="count-pill">{{ totalUnread() }}</span>
            }
          </button>
          <app-box-list
            [mailboxes]="mailboxes()"
            [(selectedBox)]="selectedBox"
            (deleteEvent)="deleteBox($event)"
            (markAllAsReadEvent)="markAllAsRead($event)"
            (boxClick)="onMobileBoxSelect($event)"
          ></app-box-list>
        </div>
        <div class="side-nav-footer">
          <button type="button" class="side-item side-item-muted" (click)="mailSettingsClick()">
            <svg lucideIcon="cog" size="16"></svg>
            <span class="side-item-label">Mail settings</span>
          </button>
        </div>
      </aside>

      @if (isMobileMenuOpen()) {
        <div class="mail-sidebar-backdrop" (click)="closeMobileMenu()"></div>
      }

      <main class="mail-main">
        <div class="page-toolbar">
          <div class="page-toolbar-title">
            <h1 [title]="selectedBox() ?? 'All mail'">{{ selectedBox() ?? 'All mail' }}</h1>
            <p>{{ subtitle() }}</p>
          </div>
          @if (selectedBox(); as box) {
            <button
              type="button"
              class="icon-btn icon-btn-bordered"
              (click)="copyBox(box)"
              [attr.aria-label]="boxCopied() ? 'Copied' : 'Copy address'"
              [title]="boxCopied() ? 'Copied!' : 'Copy address'"
            >
              <svg [lucideIcon]="boxCopied() ? 'circle-check' : 'copy'" size="15"></svg>
            </button>
          }
          @if (currentUnread() > 0) {
            <button type="button" class="btn-quiet" (click)="markCurrentAsRead()">
              <svg lucideIcon="check" size="14"></svg>
              Mark all read
            </button>
          }
          <button
            type="button"
            class="icon-btn icon-btn-bordered"
            (click)="reload()"
            aria-label="Refresh"
            title="Refresh"
          >
            <svg lucideIcon="refresh-cw" size="15"></svg>
          </button>
        </div>

        <div class="list-card">
          <app-mail-list
            [emails]="sortedEmails()"
            [loading]="loading()"
            [error]="error()"
            [showTo]="selectedBox() === null"
            [selectedBox]="selectedBox()"
            [sortColumn]="sortColumn()"
            [sortDirection]="sortDirection()"
            [openId]="openMailId()"
            (sort)="toggleSort($event)"
            (open)="openMail($event)"
          />
          @if (totalCount() > 0) {
            <div class="list-footer">
              <span>{{ rangeLabel() }}</span>
              @if (totalPages() > 1) {
                <div class="list-footer-actions">
                  <button
                    type="button"
                    class="icon-btn icon-btn-bordered"
                    (click)="previousPage()"
                    [disabled]="currentPage() === 1"
                    aria-label="Previous page"
                  >
                    <svg lucideIcon="chevron-left" size="15"></svg>
                  </button>
                  <button
                    type="button"
                    class="icon-btn icon-btn-bordered"
                    (click)="nextPage()"
                    [disabled]="currentPage() === totalPages()"
                    aria-label="Next page"
                  >
                    <svg lucideIcon="chevron-right" size="15"></svg>
                  </button>
                </div>
              }
            </div>
          }
        </div>
      </main>
    </div>

    @if (openMailId(); as id) {
      <app-mail-panel
        [mailId]="id"
        [position]="openPosition()"
        [hasPrev]="openIndex() > 0"
        [hasNext]="openIndex() >= 0 && openIndex() < (sortedEmails()?.length ?? 0) - 1"
        (closed)="closeMail()"
        (prev)="stepMail(-1)"
        (next)="stepMail(1)"
        (loaded)="onMailLoaded()"
      />
    }
  `,
  changeDetection: ChangeDetectionStrategy.OnPush,
  styleUrl: './mail.component.scss',
})
export class MailComponent {
  modalContainerService = inject(ModalContainerService);
  mailService = inject(MailService);
  lastSeenService = inject(LastSeenService);
  mobileMenuService = inject(MobileMenuService);
  signalRService = inject(SignalRService);
  mailSearchService = inject(MailSearchService);

  mailboxes = signal<MailBoxGroups[] | null>(null);
  mailGroups = signal<MailGroup[] | null>(null);
  emails = signal<MailWithReadStatus[] | null>(null);
  selectedBox = signal<string | null>(null);
  isMobileMenuOpen = signal(false);
  loading = signal(false);
  error = signal(false);

  // Sorting (sent to the server; also applied to the current page as a fallback)
  sortColumn = signal<SortColumn>('createdOn');
  sortDirection = signal<SortDirection>('desc');

  // Server-side pagination
  currentPage = signal(1);
  pageSize = 20;
  totalCount = signal(0);

  totalPages = computed(() => Math.ceil(this.totalCount() / this.pageSize));

  // Message open in the slide-over reader (null = closed).
  openMailId = signal<number | null>(null);
  boxCopied = signal(false);
  private boxCopiedTimer?: ReturnType<typeof setTimeout>;

  // Unread count per mailbox address (a box can be listed under more than one group).
  private unreadByBox = computed(() => {
    const counts = new Map<string, number>();
    for (const group of this.mailboxes() ?? []) {
      for (const box of group.mailBoxes) counts.set(box.name, box.unreadCount);
    }
    return counts;
  });
  totalUnread = computed(() => [...this.unreadByBox().values()].reduce((a, b) => a + b, 0));
  currentUnread = computed(() => {
    const box = this.selectedBox();
    return box ? (this.unreadByBox().get(box) ?? 0) : this.totalUnread();
  });

  subtitle = computed(() => {
    const total = this.totalCount();
    const unread = this.currentUnread();
    const parts = [`${total} ${total === 1 ? 'message' : 'messages'}`];
    if (unread > 0) parts.push(`${unread} unread`);
    if (this.mailSearchService.searchText().trim()) parts.push('filtered by search');
    return parts.join(' · ');
  });

  rangeLabel = computed(() => {
    const start = (this.currentPage() - 1) * this.pageSize + 1;
    const end = Math.min(this.currentPage() * this.pageSize, this.totalCount());
    return `${start}–${end} of ${this.totalCount()}`;
  });

  openIndex = computed(() => {
    const id = this.openMailId();
    return id === null ? -1 : (this.sortedEmails()?.findIndex((e) => e.id === id) ?? -1);
  });
  openPosition = computed(() => {
    const i = this.openIndex();
    return i < 0 ? '' : `${i + 1} of ${this.sortedEmails()?.length ?? 0}`;
  });

  sortedEmails = computed(() => {
    const emails = this.emails();
    if (!emails) return null;

    return [...emails].sort((a, b) => {
      const column = this.sortColumn();
      const direction = this.sortDirection();

      let aVal: any = a[column];
      let bVal: any = b[column];

      if (column === 'createdOn') {
        aVal = new Date(aVal).getTime();
        bVal = new Date(bVal).getTime();
      }

      if (typeof aVal === 'string') {
        aVal = aVal.toLowerCase();
        bVal = bVal.toLowerCase();
      }

      if (aVal < bVal) return direction === 'asc' ? -1 : 1;
      if (aVal > bVal) return direction === 'asc' ? 1 : -1;
      return 0;
    });
  });

  // Every list fetch goes through this stream; switchMap cancels any in-flight
  // request so only the latest (box, search, page, sort) can render.
  // background = true skips the "Loading..." placeholder (used for live refreshes).
  private fetchRequests = new Subject<{ background: boolean }>();
  // Same latest-wins treatment for the sidebar (mailboxes + groups).
  private refreshRequests = new Subject<void>();

  constructor() {
    this.fetchRequests
      .pipe(
        switchMap(({ background }) => {
          if (!background) {
            this.loading.set(true);
          }
          this.error.set(false);
          // The filter is captured now, so a superseded request can never apply.
          return this.mailService.getEmails(this.buildFilter()).pipe(
            map((result) => ({ ok: true as const, result })),
            catchError(() => of({ ok: false as const, result: null }))
          );
        }),
        takeUntilDestroyed()
      )
      .subscribe(({ ok, result }) => {
        this.emails.set(ok ? (result?.items ?? null) : null);
        this.totalCount.set(ok ? (result?.totalCount ?? 0) : 0);
        this.loading.set(false);
        this.error.set(!ok);
      });

    this.refreshRequests
      .pipe(
        switchMap(() =>
          forkJoin([this.mailService.getMailboxes(), this.mailService.getMailGroups()]).pipe(
            catchError((err) => {
              console.error('Failed to refresh mailboxes:', err);
              return of(null);
            })
          )
        ),
        takeUntilDestroyed()
      )
      .subscribe((result) => {
        if (!result) return;
        const [mailboxes, mailGroups] = result;
        this.mailboxes.set(mailboxes);
        this.mailGroups.set(mailGroups);
      });

    // Refetch (page 1) whenever the selected box or search term changes.
    combineLatest([
      toObservable(this.selectedBox),
      toObservable(this.mailSearchService.searchText).pipe(debounceTime(250), distinctUntilChanged()),
    ])
      .pipe(takeUntilDestroyed())
      .subscribe(() => {
        this.currentPage.set(1);
        this.openMailId.set(null);
        this.fetchEmails();
      });

    this.refreshMail();
    this.mobileMenuService.menuToggled.pipe(takeUntilDestroyed()).subscribe(() => {
      this.toggleMobileMenu();
    });

    // Live updates. A burst of incoming mail is throttled to at most one list
    // refresh and one sidebar refresh per second instead of one per message.
    this.signalRService.newMail$
      .pipe(
        filter((mail) => this.isInCurrentView(mail)),
        auditTime(LIVE_REFRESH_INTERVAL_MS),
        takeUntilDestroyed()
      )
      .subscribe(() => this.fetchEmails(true));

    // Always refresh mailboxes to update counts.
    this.signalRService.newMail$
      .pipe(auditTime(LIVE_REFRESH_INTERVAL_MS), takeUntilDestroyed())
      .subscribe(() => this.refreshMail());
  }

  private isInCurrentView(mail: MailNotification): boolean {
    const currentBox = this.selectedBox();
    return !currentBox || mail.to === currentBox || (!!mail.mailGroupPath && currentBox.includes(mail.mailGroupPath));
  }

  private fetchEmails(background = false) {
    this.fetchRequests.next({ background });
  }

  private buildFilter(): FilterOptions {
    const trimmed = this.mailSearchService.searchText().trim();
    return {
      to: this.selectedBox(),
      search: trimmed || null,
      page: this.currentPage(),
      pageSize: this.pageSize,
      // Server-side sort across all pages (Go API). The legacy API ignores these and
      // returns newest-first; sortedEmails() still orders the current page client-side.
      sortBy: this.sortColumn(),
      sortDirection: this.sortDirection(),
    };
  }

  refreshMail() {
    this.refreshRequests.next();
  }
  reload() {
    this.refreshMail();
    this.fetchEmails();
  }

  openMail(mail: MailWithReadStatus) {
    this.openMailId.set(mail.id);
    // Opening marks it read on the server; reflect that in the row right away.
    this.emails.update((list) => list?.map((e) => (e.id === mail.id ? { ...e, isRead: true } : e)) ?? null);
  }

  closeMail() {
    this.openMailId.set(null);
  }

  stepMail(delta: number) {
    const list = this.sortedEmails();
    const next = list?.[this.openIndex() + delta];
    if (next) this.openMail(next);
  }

  // The reader marked the message read server-side; refresh sidebar unread counts.
  onMailLoaded() {
    this.refreshMail();
  }

  copyBox(address: string) {
    navigator.clipboard.writeText(address).then(() => {
      this.boxCopied.set(true);
      clearTimeout(this.boxCopiedTimer);
      this.boxCopiedTimer = setTimeout(() => this.boxCopied.set(false), 1500);
    });
  }

  markCurrentAsRead() {
    const box = this.selectedBox();
    if (box) {
      this.markAllAsRead(box);
      return;
    }
    this.mailService.markAllAsRead().subscribe({
      next: () => this.reload(),
      error: (error) => console.error('Error marking emails as read:', error),
    });
  }
  deleteBox(email: string) {
    this.mailService.deleteBoxes({ to: email, search: null, page: 1, pageSize: this.pageSize }).subscribe(() => {
      this.refreshMail();
    });
  }

  markAllAsRead(mailboxName: string) {
    // Find the mailbox path for this mailbox name
    const mailboxGroups = this.mailboxes();
    let mailboxPath: string | undefined;

    if (mailboxGroups) {
      for (const group of mailboxGroups) {
        const mailbox = group.mailBoxes.find((mb: MailBox) => mb.name === mailboxName);
        if (mailbox) {
          mailboxPath = mailbox.path || undefined;
          break;
        }
      }
    }

    this.mailService.markAllAsRead(mailboxPath).subscribe({
      next: () => {
        // Refresh mail data to update unread counts
        this.refreshMail();
        this.fetchEmails();
      },
      error: (error) => {
        console.error('Error marking emails as read:', error);
      },
    });
  }

  toggleSort(column: SortColumn) {
    if (this.sortColumn() === column) {
      this.sortDirection.set(this.sortDirection() === 'asc' ? 'desc' : 'asc');
    } else {
      this.sortColumn.set(column);
      this.sortDirection.set(column === 'createdOn' ? 'desc' : 'asc');
    }
    // Re-sort across the whole mailbox on the server, starting again from page 1.
    this.currentPage.set(1);
    this.fetchEmails();
  }

  nextPage() {
    if (this.currentPage() < this.totalPages()) {
      this.currentPage.update((page) => page + 1);
      this.fetchEmails();
    }
  }

  previousPage() {
    if (this.currentPage() > 1) {
      this.currentPage.update((page) => page - 1);
      this.fetchEmails();
    }
  }

  toggleMobileMenu() {
    this.isMobileMenuOpen.update((isOpen) => !isOpen);
  }

  closeMobileMenu() {
    this.isMobileMenuOpen.set(false);
  }

  onMobileBoxSelect(box: string | null) {
    this.selectedBox.set(box);
    this.updateLastSeen(box);
    this.closeMobileMenu();
  }
  mailSettingsClick() {
    // Refresh the sidebar when the settings modal closes so group create/delete
    // and retention changes are reflected without a hard reload.
    MailSettingsModalComponent.show(this.modalContainerService).onClose.subscribe(() => {
      this.refreshMail();
    });
  }
  private updateLastSeen(box: string | null) {
    if (!box) return; // Don't track "Show All"

    const mailGroups = this.mailGroups();
    if (!mailGroups) return;

    const matchingGroup = mailGroups.find((group) => {
      const mailboxes = this.mailboxes();
      if (!mailboxes) return false;

      for (const mbGroup of mailboxes) {
        const foundBox = mbGroup.mailBoxes.find((mb) => mb.name === box);
        if (foundBox && foundBox.path === group.path) {
          return true;
        }
      }
      return false;
    });

    if (matchingGroup && matchingGroup.path) {
      this.lastSeenService.setLastSeen(matchingGroup.path);
    }
  }
}
