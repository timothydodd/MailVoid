import { CommonModule } from '@angular/common';
import { Component, computed, inject, signal } from '@angular/core';
import { takeUntilDestroyed, toObservable } from '@angular/core/rxjs-interop';
import { Router } from '@angular/router';
import { LucideAngularModule } from 'lucide-angular';
import { catchError, of, switchMap } from 'rxjs';
import { MailSettingsModalComponent } from '../../_components/mail-settings-modal/mail-settings-modal.component';
import { FilterOptions, Mail, MailBoxGroups, MailService } from '../../_services/api/mail.service';
import { MobileMenuService } from '../../_services/mobile-menu.service';
import { BoxListComponent } from './box-list/box-list.component';

type SortColumn = 'from' | 'to' | 'subject' | 'createdOn';
type SortDirection = 'asc' | 'desc';

@Component({
  selector: 'app-mail',
  standalone: true,
  imports: [CommonModule, BoxListComponent, LucideAngularModule, MailSettingsModalComponent],
  template: `
    <div class="mail-container">
      <!-- Left Sidebar (Hidden on mobile, shown as overlay when menu is open) -->
      <div class="mail-sidebar" [class.mobile-menu-open]="isMobileMenuOpen()">
        <div class="sidebar-card">
          <div class="sidebar-header">
            <button class="btn btn-icon mobile-close-btn" (click)="closeMobileMenu()" title="Close">
              <lucide-icon name="x" size="24"></lucide-icon>
            </button>
            <button class="btn btn-icon" (click)="mailSettings.show()" title="Mail Settings">
              <lucide-icon name="cog"></lucide-icon>
            </button>
          </div>
          <div class="sidebar-body">
            <app-box-list
              [mailboxes]="mailboxes()"
              [(selectedBox)]="selectedBox"
              (deleteEvent)="deleteBox($event)"
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
                    <lucide-icon
                      [name]="sortDirection() === 'asc' ? 'chevron-up' : 'chevron-down'"
                      size="14"
                    ></lucide-icon>
                  }
                </th>
                <th class="sortable" (click)="toggleSort('to')">
                  To
                  @if (sortColumn() === 'to') {
                    <lucide-icon
                      [name]="sortDirection() === 'asc' ? 'chevron-up' : 'chevron-down'"
                      size="14"
                    ></lucide-icon>
                  }
                </th>
                <th class="sortable" (click)="toggleSort('subject')">
                  Subject
                  @if (sortColumn() === 'subject') {
                    <lucide-icon
                      [name]="sortDirection() === 'asc' ? 'chevron-up' : 'chevron-down'"
                      size="14"
                    ></lucide-icon>
                  }
                </th>
                <th class="sortable" (click)="toggleSort('createdOn')">
                  Created On
                  @if (sortColumn() === 'createdOn') {
                    <lucide-icon
                      [name]="sortDirection() === 'asc' ? 'chevron-up' : 'chevron-down'"
                      size="14"
                    ></lucide-icon>
                  }
                </th>
              </tr>
            </thead>
            <tbody>
              @if (paginatedEmails()) {
                @for (email of paginatedEmails(); track email.id) {
                  <tr (click)="clickMail(email)">
                    <td class="from-email">{{ email.from }}</td>
                    <td style="max-width: 100px;overflow:hidden;">{{ email.to }}</td>
                    <td style="max-width: 300px;word-break: break-all;">
                      {{ email.subject }}
                    </td>
                    <td>{{ email.createdOn | date: 'short' }}</td>
                  </tr>
                }
              }
            </tbody>
          </table>
        </div>
        @if (totalPages() > 1) {
          <div class="pagination-controls">
            <div>
              <button class="btn" (click)="previousPage()" [disabled]="currentPage() === 1">
                <lucide-icon name="chevron-left" size="14"></lucide-icon>
                Previous
              </button>
              <span class="page-info">Page {{ currentPage() }} of {{ totalPages() }}</span>
              <button class="btn" (click)="nextPage()" [disabled]="currentPage() === totalPages()">
                Next
                <lucide-icon name="chevron-right" size="14"></lucide-icon>
              </button>
            </div>
          </div>
        }
      </div>
    </div>
    <app-mail-settings-modal #mailSettings></app-mail-settings-modal>
  `,
  styleUrl: './mail.component.scss',
})
export class MailComponent {
  router = inject(Router);
  mailService = inject(MailService);
  mobileMenuService = inject(MobileMenuService);
  mailboxes = signal<MailBoxGroups[] | null>(null);
  emails = signal<Mail[] | null>(null);
  selectedBox = signal<string | null>(null);
  isMobileMenuOpen = signal(false);

  // Sorting
  sortColumn = signal<SortColumn>('createdOn');
  sortDirection = signal<SortDirection>('desc');

  // Pagination
  currentPage = signal(1);
  itemsPerPage = 20;

  sortedEmails = computed(() => {
    const emails = this.emails();
    if (!emails) return null;

    const sorted = [...emails].sort((a, b) => {
      const column = this.sortColumn();
      const direction = this.sortDirection();

      let aVal: any = a[column];
      let bVal: any = b[column];

      // Handle date sorting
      if (column === 'createdOn') {
        aVal = new Date(aVal).getTime();
        bVal = new Date(bVal).getTime();
      }

      // Handle string sorting (case insensitive)
      if (typeof aVal === 'string') {
        aVal = aVal.toLowerCase();
        bVal = bVal.toLowerCase();
      }

      if (aVal < bVal) return direction === 'asc' ? -1 : 1;
      if (aVal > bVal) return direction === 'asc' ? 1 : -1;
      return 0;
    });

    return sorted;
  });

  totalPages = computed(() => {
    const emails = this.sortedEmails();
    if (!emails) return 0;
    return Math.ceil(emails.length / this.itemsPerPage);
  });

  paginatedEmails = computed(() => {
    const emails = this.sortedEmails();
    if (!emails) return null;

    const start = (this.currentPage() - 1) * this.itemsPerPage;
    const end = start + this.itemsPerPage;

    return emails.slice(start, end);
  });

  constructor() {
    toObservable(this.selectedBox)
      .pipe(
        switchMap((selectedBox) =>
          this.mailService
            .getEmails(selectedBox ? ({ to: selectedBox } as FilterOptions) : undefined)
            .pipe(catchError(() => of({ items: null, totalCount: 0 })))
        ),
        takeUntilDestroyed()
      )
      .subscribe((selectedBox) => {
        this.emails.set(selectedBox?.items);
        this.currentPage.set(1); // Reset to first page when emails change
      });
    this.refreshMail();
    this.mobileMenuService.menuToggled.pipe(takeUntilDestroyed()).subscribe(() => {
      this.toggleMobileMenu();
    });
  }

  refreshMail() {
    this.mailService.getMailboxes().subscribe((mailboxes) => {
      this.mailboxes.set(mailboxes);
    });
  }
  clickBox(box: string) {
    this.selectedBox.set(box);
  }
  clickMail(mail: Mail) {
    this.router.navigate(['mail', mail.id]);
  }
  deleteBox(email: string) {
    this.mailService.deleteBoxes({ to: email } as FilterOptions).subscribe(() => {
      this.refreshMail();
    });
  }

  toggleSort(column: SortColumn) {
    if (this.sortColumn() === column) {
      // Toggle direction if same column
      this.sortDirection.set(this.sortDirection() === 'asc' ? 'desc' : 'asc');
    } else {
      // New column, default to ascending (except for dates which default to descending)
      this.sortColumn.set(column);
      this.sortDirection.set(column === 'createdOn' ? 'desc' : 'asc');
    }
  }

  nextPage() {
    if (this.currentPage() < this.totalPages()) {
      this.currentPage.update((page) => page + 1);
    }
  }

  previousPage() {
    if (this.currentPage() > 1) {
      this.currentPage.update((page) => page - 1);
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
    this.closeMobileMenu();
  }
}
