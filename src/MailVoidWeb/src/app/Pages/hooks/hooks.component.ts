import { CommonModule } from '@angular/common';
import { Component, computed, inject, signal } from '@angular/core';
import { takeUntilDestroyed, toObservable } from '@angular/core/rxjs-interop';
import { Router } from '@angular/router';
import { LucideAngularModule } from 'lucide-angular';
import { catchError, of, switchMap } from 'rxjs';
import { WebhookBucket, WebhookListItem, WebhookService } from '../../_services/api/webhook.service';
import { SignalRService } from '../../services/signalr.service';

type SortColumn = 'httpMethod' | 'path' | 'contentType' | 'createdOn';
type SortDirection = 'asc' | 'desc';

@Component({
  selector: 'app-hooks',
  standalone: true,
  imports: [CommonModule, LucideAngularModule],
  template: `
    <div class="hooks-container">
      <!-- Left Sidebar -->
      <div class="hooks-sidebar">
        <div class="sidebar-card">
          <div class="sidebar-header">
            <h3>Buckets</h3>
            <button
              class="btn btn-icon"
              [class.active]="selectedBucket() === null"
              (click)="selectBucket(null)"
              title="Show all webhooks"
            >
              <lucide-icon name="inbox" size="20"></lucide-icon>
            </button>
          </div>
          <div class="sidebar-body">
            @if (buckets()) {
              <ul class="bucket-list">
                @for (bucket of buckets(); track bucket.id) {
                  <li
                    class="bucket-item"
                    [class.selected]="selectedBucket() === bucket.name"
                    (click)="selectBucket(bucket.name)"
                  >
                    <lucide-icon name="folder" size="16"></lucide-icon>
                    <span class="bucket-name">{{ bucket.name }}</span>
                    @if (bucket.lastActivity) {
                      <span class="bucket-activity">{{ bucket.lastActivity | date: 'short' }}</span>
                    }
                  </li>
                }
              </ul>
            } @else {
              <p class="no-buckets">No buckets yet. Send a webhook to create one.</p>
            }
          </div>
        </div>
      </div>

      <!-- Main Content Area -->
      <div class="hooks-content">
        @if (selectedBucket()) {
          <div class="content-header">
            <h2>{{ selectedBucket() }}</h2>
            <div class="endpoint-row">
              <code class="endpoint-url">{{ endpointUrl() }}</code>
              <button class="btn btn-icon btn-copy" (click)="copyEndpointUrl()" [title]="copyTooltip()">
                <lucide-icon [name]="copyIcon()" size="14"></lucide-icon>
              </button>
            </div>
          </div>
        } @else {
          <div class="content-header">
            <h2>All Webhooks</h2>
          </div>
        }

        <div class="table-wrap">
          <table>
            <thead>
              <tr>
                <th class="sortable" (click)="toggleSort('httpMethod')">
                  Method
                  @if (sortColumn() === 'httpMethod') {
                    <lucide-icon
                      [name]="sortDirection() === 'asc' ? 'chevron-up' : 'chevron-down'"
                      size="14"
                    ></lucide-icon>
                  }
                </th>
                <th class="sortable" (click)="toggleSort('path')">
                  Path
                  @if (sortColumn() === 'path') {
                    <lucide-icon
                      [name]="sortDirection() === 'asc' ? 'chevron-up' : 'chevron-down'"
                      size="14"
                    ></lucide-icon>
                  }
                </th>
                <th class="sortable" (click)="toggleSort('contentType')">
                  Content-Type
                  @if (sortColumn() === 'contentType') {
                    <lucide-icon
                      [name]="sortDirection() === 'asc' ? 'chevron-up' : 'chevron-down'"
                      size="14"
                    ></lucide-icon>
                  }
                </th>
                <th class="sortable" (click)="toggleSort('createdOn')">
                  Received
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
              @if (paginatedWebhooks()) {
                @for (webhook of paginatedWebhooks(); track webhook.id) {
                  <tr (click)="clickWebhook(webhook)">
                    <td>
                      <span class="method-badge method-{{ webhook.httpMethod.toLowerCase() }}">
                        {{ webhook.httpMethod }}
                      </span>
                    </td>
                    <td class="path-cell">{{ webhook.path }}{{ webhook.queryString || '' }}</td>
                    <td>{{ webhook.contentType || '-' }}</td>
                    <td>{{ webhook.createdOn | date: 'short' }}</td>
                  </tr>
                }
              }
              @if (!webhooks() || webhooks()!.length === 0) {
                <tr>
                  <td colspan="4" class="no-data">No webhooks captured yet</td>
                </tr>
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
  `,
  styleUrl: './hooks.component.scss',
})
export class HooksComponent {
  router = inject(Router);
  webhookService = inject(WebhookService);
  signalRService = inject(SignalRService);

  buckets = signal<WebhookBucket[] | null>(null);
  webhooks = signal<WebhookListItem[] | null>(null);
  selectedBucket = signal<string | null>(null);
  copyIcon = signal<string>('copy');
  copyTooltip = signal<string>('Copy URL');

  endpointUrl = computed(() => {
    const origin = window.location.origin;
    return `${origin}/api/hook/${this.selectedBucket()}`;
  });

  // Sorting
  sortColumn = signal<SortColumn>('createdOn');
  sortDirection = signal<SortDirection>('desc');

  // Pagination
  currentPage = signal(1);
  itemsPerPage = 20;

  sortedWebhooks = computed(() => {
    const webhooks = this.webhooks();
    if (!webhooks) return null;

    const sorted = [...webhooks].sort((a, b) => {
      const column = this.sortColumn();
      const direction = this.sortDirection();

      let aVal: string | number = a[column] ?? '';
      let bVal: string | number = b[column] ?? '';

      if (column === 'createdOn') {
        aVal = new Date(aVal).getTime();
        bVal = new Date(bVal).getTime();
      }

      if (typeof aVal === 'string') {
        aVal = aVal.toLowerCase();
        bVal = (bVal as string).toLowerCase();
      }

      if (aVal < bVal) return direction === 'asc' ? -1 : 1;
      if (aVal > bVal) return direction === 'asc' ? 1 : -1;
      return 0;
    });

    return sorted;
  });

  totalPages = computed(() => {
    const webhooks = this.sortedWebhooks();
    if (!webhooks) return 0;
    return Math.ceil(webhooks.length / this.itemsPerPage);
  });

  paginatedWebhooks = computed(() => {
    const webhooks = this.sortedWebhooks();
    if (!webhooks) return null;

    const start = (this.currentPage() - 1) * this.itemsPerPage;
    const end = start + this.itemsPerPage;

    return webhooks.slice(start, end);
  });

  constructor() {
    // Load buckets
    this.refreshBuckets();

    // Load webhooks when selected bucket changes
    toObservable(this.selectedBucket)
      .pipe(
        switchMap((bucketName) => {
          if (!bucketName) {
            // Show all webhooks - get from all buckets
            return this.webhookService.getBuckets().pipe(
              switchMap((buckets) => {
                if (!buckets || buckets.length === 0) return of({ items: [], totalCount: 0, page: 1, pageSize: 50 });
                // Get webhooks from the first bucket for now (could aggregate later)
                return this.webhookService.getWebhooks(buckets[0].name);
              }),
              catchError(() => of({ items: [], totalCount: 0, page: 1, pageSize: 50 }))
            );
          }
          return this.webhookService.getWebhooks(bucketName).pipe(
            catchError(() => of({ items: [], totalCount: 0, page: 1, pageSize: 50 }))
          );
        }),
        takeUntilDestroyed()
      )
      .subscribe((result) => {
        this.webhooks.set(result?.items ?? []);
        this.currentPage.set(1);
      });

    // Subscribe to new webhook notifications
    this.signalRService.newWebhook$.pipe(takeUntilDestroyed()).subscribe((webhook) => {
      const currentBucket = this.selectedBucket();
      if (!currentBucket || webhook.bucketName === currentBucket) {
        // Refresh webhooks for the current view
        if (currentBucket) {
          this.webhookService
            .getWebhooks(currentBucket)
            .pipe(catchError(() => of({ items: [], totalCount: 0, page: 1, pageSize: 50 })))
            .subscribe((result) => {
              this.webhooks.set(result?.items ?? []);
            });
        }
      }
      // Always refresh buckets to update counts/activity
      this.refreshBuckets();
    });
  }

  refreshBuckets() {
    this.webhookService.getBuckets().subscribe((buckets) => {
      this.buckets.set(buckets);
    });
  }

  selectBucket(name: string | null) {
    this.selectedBucket.set(name);
  }

  clickWebhook(webhook: WebhookListItem) {
    this.router.navigate(['hooks', webhook.bucketName, webhook.id]);
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
    }
  }

  previousPage() {
    if (this.currentPage() > 1) {
      this.currentPage.update((page) => page - 1);
    }
  }

  copyEndpointUrl() {
    navigator.clipboard.writeText(this.endpointUrl()).then(() => {
      this.copyTooltip.set('Copied!');
      setTimeout(() => this.copyTooltip.set('Copy URL'), 2000);
    });
  }
}
