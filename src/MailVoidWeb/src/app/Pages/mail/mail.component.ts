import { CommonModule } from '@angular/common';
import { Component, computed, inject, signal, ChangeDetectionStrategy } from '@angular/core';
import { takeUntilDestroyed, toObservable } from '@angular/core/rxjs-interop';
import { Router } from '@angular/router';
import { LucideDynamicIcon } from '@lucide/angular';
import { combineLatest, debounceTime, distinctUntilChanged } from 'rxjs';
import { MailSettingsModalComponent } from '../../_components/mail-settings-modal/mail-settings-modal.component';
import { ModalContainerService } from '@rd-ui';
import {
  FilterOptions,
  MailBox,
  MailBoxGroups,
  MailGroup,
  MailService,
  MailWithReadStatus,
} from '../../_services/api/mail.service';
import { LastSeenService } from '../../_services/last-seen.service';
import { MailSearchService } from '../../_services/mail-search.service';
import { MobileMenuService } from '../../_services/mobile-menu.service';
import { SignalRService } from '../../services/signalr.service';
import { BoxListComponent } from './box-list/box-list.component';

type SortColumn = 'from' | 'to' | 'subject' | 'createdOn';
type SortDirection = 'asc' | 'desc';

@Component({
  selector: 'app-mail',
  standalone: true,
  imports: [CommonModule, BoxListComponent, LucideDynamicIcon],
  template: `
    <div class="mail-container">
      <!-- Left Sidebar (Hidden on mobile, shown as overlay when menu is open) -->
      <div class="mail-sidebar" [class.mobile-menu-open]="isMobileMenuOpen()">
        <div class="sidebar-card">
          <div class="sidebar-header">
            <button class="btn btn-icon mobile-close-btn" (click)="closeMobileMenu()" title="Close">
              <svg lucideIcon="x" size="24"></svg>
            </button>
            <button
              class="btn btn-icon show-all-btn"
              [class.active]="selectedBox() === null"
              (click)="clickBox(null)"
              title="Show all emails"
            >
              <svg lucideIcon="inbox" size="20"></svg>
            </button>
            <button class="btn btn-icon" (click)="mailSettingsClick()" title="Mail Settings">
              <svg lucideIcon="cog" size="20"></svg>
            </button>
          </div>
          <div class="sidebar-body">
            <app-box-list
              [mailboxes]="mailboxes()"
              [(selectedBox)]="selectedBox"
              (deleteEvent)="deleteBox($event)"
              (markAllAsReadEvent)="markAllAsRead($event)"
              (boxClick)="onMobileBoxSelect($event)"
            ></app-box-list>
          </div>
        </div>
      </div>

      <!-- Overlay backdrop for mobile -->
      @if (isMobileMenuOpen()) {
        <div class="mobile-overlay" (click)="closeMobileMenu()"></div>
      }

      <!-- Main Content Area -->
      <div class="mail-content">
        <div class="table-wrap">
          <table>
            <thead>
              <tr>
                <th class="sortable" (click)="toggleSort('from')">
                  From
                  @if (sortColumn() === 'from') {
                    <svg
                      [lucideIcon]="sortDirection() === 'asc' ? 'chevron-up' : 'chevron-down'"
                      size="14"
                    ></svg>
                  }
                </th>
                <th class="sortable" (click)="toggleSort('to')">
                  To
                  @if (sortColumn() === 'to') {
                    <svg
                      [lucideIcon]="sortDirection() === 'asc' ? 'chevron-up' : 'chevron-down'"
                      size="14"
                    ></svg>
                  }
                </th>
                <th class="sortable" (click)="toggleSort('subject')">
                  Subject
                  @if (sortColumn() === 'subject') {
                    <svg
                      [lucideIcon]="sortDirection() === 'asc' ? 'chevron-up' : 'chevron-down'"
                      size="14"
                    ></svg>
                  }
                </th>
                <th class="sortable" (click)="toggleSort('createdOn')">
                  Created On
                  @if (sortColumn() === 'createdOn') {
                    <svg
                      [lucideIcon]="sortDirection() === 'asc' ? 'chevron-up' : 'chevron-down'"
                      size="14"
                    ></svg>
                  }
                </th>
              </tr>
            </thead>
            <tbody>
              @if (loading()) {
                <tr>
                  <td colspan="4" class="table-status">Loading emails...</td>
                </tr>
              } @else if (error()) {
                <tr>
                  <td colspan="4" class="table-status table-status-error">
                    Failed to load emails. Please try again.
                  </td>
                </tr>
              } @else if (sortedEmails() && sortedEmails()!.length > 0) {
                @for (email of sortedEmails(); track email.id) {
                  <tr
                    role="button"
                    tabindex="0"
                    (click)="clickMail(email)"
                    (keydown.enter)="clickMail(email)"
                    (keydown.space)="$event.preventDefault(); clickMail(email)"
                    [class.read]="email.isRead"
                  >
                    <td class="from-email">{{ email.from }}</td>
                    <td style="max-width: 100px;overflow:hidden;">{{ email.to }}</td>
                    <td style="max-width: 300px;word-break: break-all;">
                      {{ email.subject }}
                    </td>
                    <td>{{ email.createdOn | date: 'short' }}</td>
                  </tr>
                }
              } @else {
                <tr>
                  <td colspan="4" class="table-status">No emails found</td>
                </tr>
              }
            </tbody>
          </table>
        </div>
        @if (totalPages() > 1) {
          <div class="pagination-controls">
            <div>
              <button class="btn" (click)="previousPage()" [disabled]="currentPage() === 1">
                <svg lucideIcon="chevron-left" size="14"></svg>
                Previous
              </button>
              <span class="page-info">Page {{ currentPage() }} of {{ totalPages() }}</span>
              <button class="btn" (click)="nextPage()" [disabled]="currentPage() === totalPages()">
                Next
                <svg lucideIcon="chevron-right" size="14"></svg>
              </button>
            </div>
          </div>
        }
      </div>
    </div>
  `,
  changeDetection: ChangeDetectionStrategy.OnPush,
  styleUrl: './mail.component.scss',
})
export class MailComponent {
  router = inject(Router);
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

  // Sorting (applied to the current server page)
  sortColumn = signal<SortColumn>('createdOn');
  sortDirection = signal<SortDirection>('desc');

  // Server-side pagination
  currentPage = signal(1);
  pageSize = 20;
  totalCount = signal(0);

  totalPages = computed(() => Math.ceil(this.totalCount() / this.pageSize));

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

  constructor() {
    // Refetch (page 1) whenever the selected box or search term changes.
    combineLatest([
      toObservable(this.selectedBox),
      toObservable(this.mailSearchService.searchText).pipe(debounceTime(250), distinctUntilChanged()),
    ])
      .pipe(takeUntilDestroyed())
      .subscribe(() => {
        this.currentPage.set(1);
        this.fetchEmails();
      });

    this.refreshMail();
    this.mobileMenuService.menuToggled.pipe(takeUntilDestroyed()).subscribe(() => {
      this.toggleMobileMenu();
    });

    // Subscribe to new mail notifications
    this.signalRService.newMail$.pipe(takeUntilDestroyed()).subscribe((mail) => {
      const currentBox = this.selectedBox();
      if (!currentBox || mail.to === currentBox || (mail.mailGroupPath && currentBox.includes(mail.mailGroupPath))) {
        this.fetchEmails();
      }
      // Always refresh mailboxes to update counts
      this.refreshMail();
    });
  }

  private fetchEmails() {
    this.loading.set(true);
    this.error.set(false);
    this.mailService
      .getEmails(this.buildFilter(this.selectedBox(), this.mailSearchService.searchText(), this.currentPage()))
      .subscribe({
        next: (result) => {
          this.emails.set(result?.items ?? null);
          this.totalCount.set(result?.totalCount ?? 0);
          this.loading.set(false);
        },
        error: () => {
          this.emails.set(null);
          this.totalCount.set(0);
          this.loading.set(false);
          this.error.set(true);
        },
      });
  }

  private buildFilter(box: string | null, search: string, page: number): FilterOptions {
    const trimmed = search.trim();
    return { to: box, search: trimmed || null, page, pageSize: this.pageSize };
  }

  refreshMail() {
    combineLatest([this.mailService.getMailboxes(), this.mailService.getMailGroups()]).subscribe(
      ([mailboxes, mailGroups]) => {
        this.mailboxes.set(mailboxes);
        this.mailGroups.set(mailGroups);
      }
    );
  }
  clickBox(box: string | null) {
    this.selectedBox.set(box);
    this.updateLastSeen(box);
  }
  clickMail(mail: MailWithReadStatus) {
    this.router.navigate(['mail', mail.id]);
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
