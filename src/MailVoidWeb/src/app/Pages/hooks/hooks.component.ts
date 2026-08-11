import { CommonModule } from '@angular/common';
import { Component, computed, inject, signal, ChangeDetectionStrategy } from '@angular/core';
import { takeUntilDestroyed, toObservable } from '@angular/core/rxjs-interop';
import { Router } from '@angular/router';
import { LucideDynamicIcon } from '@lucide/angular';
import { catchError, forkJoin, map, of } from 'rxjs';
import { WebhookBucket, WebhookListItem, WebhookService } from '../../_services/api/webhook.service';
import { SignalRService } from '../../services/signalr.service';

type SortColumn = 'httpMethod' | 'path' | 'contentType' | 'createdOn';
type SortDirection = 'asc' | 'desc';

// Large page used to pull a bucket's full history when aggregating the "All" view.
const AGGREGATE_PAGE_SIZE = 500;

@Component({
  selector: 'app-hooks',
  standalone: true,
  imports: [CommonModule, LucideDynamicIcon],
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
              <svg lucideIcon="inbox" size="20"></svg>
            </button>
          </div>
          <div class="sidebar-body">
            @if (buckets()) {
              <ul class="bucket-list">
                @for (bucket of buckets(); track bucket.id) {
                  <li
                    class="bucket-item"
                    role="button"
                    tabindex="0"
                    [class.selected]="selectedBucket() === bucket.name"
                    (click)="selectBucket(bucket.name)"
                    (keydown.enter)="selectBucket(bucket.name)"
                    (keydown.space)="$event.preventDefault(); selectBucket(bucket.name)"
                  >
                    <svg lucideIcon="folder" size="16"></svg>
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
                <svg [lucideIcon]="copyIcon()" size="14"></svg>
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
                    <svg
                      [lucideIcon]="sortDirection() === 'asc' ? 'chevron-up' : 'chevron-down'"
                      size="14"
                    ></svg>
                  }
                </th>
                <th class="sortable" (click)="toggleSort('path')">
                  Path
                  @if (sortColumn() === 'path') {
                    <svg
                      [lucideIcon]="sortDirection() === 'asc' ? 'chevron-up' : 'chevron-down'"
                      size="14"
                    ></svg>
                  }
                </th>
                <th class="sortable" (click)="toggleSort('contentType')">
                  Content-Type
                  @if (sortColumn() === 'contentType') {
                    <svg
                      [lucideIcon]="sortDirection() === 'asc' ? 'chevron-up' : 'chevron-down'"
                      size="14"
                    ></svg>
                  }
                </th>
                <th class="sortable" (click)="toggleSort('createdOn')">
                  Received
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
                  <td colspan="4" class="table-status">Loading webhooks...</td>
                </tr>
              } @else if (error()) {
                <tr>
                  <td colspan="4" class="table-status table-status-error">
                    Failed to load webhooks. Please try again.
                  </td>
                </tr>
              } @else if (paginatedWebhooks() && paginatedWebhooks()!.length > 0) {
                @for (webhook of paginatedWebhooks(); track webhook.id) {
                  <tr
                    role="button"
                    tabindex="0"
                    (click)="clickWebhook(webhook)"
                    (keydown.enter)="clickWebhook(webhook)"
                    (keydown.space)="$event.preventDefault(); clickWebhook(webhook)"
                  >
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
              } @else {
                <tr>
                  <td colspan="4" class="table-status">No webhooks captured yet</td>
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
  styleUrl: './hooks.component.scss',
})
export class HooksComponent {
  router = inject(Router);
  webhookService = inject(WebhookService);
  signalRService = inject(SignalRService);

  buckets = signal<WebhookBucket[] | null>(null);
  // Full dataset for the current view (a bucket's page, or all buckets aggregated).
  webhooks = signal<WebhookListItem[] | null>(null);
  selectedBucket = signal<string | null>(null);
  copyIcon = signal<string>('copy');
  copyTooltip = signal<string>('Copy URL');
  loading = signal(false);
  error = signal(false);

  // Server total for the selected bucket; for the aggregated "All" view it is the
  // total number of aggregated items.
  totalCount = signal(0);

  endpointUrl = computed(() => {
    const origin = window.location.origin;
    return `${origin}/api/hook/${this.selectedBucket()}`;
  });

  // Sorting
  sortColumn = signal<SortColumn>('createdOn');
  sortDirection = signal<SortDirection>('desc');

  // Pagination
  currentPage = signal(1);
  pageSize = 20;

  totalPages = computed(() => Math.ceil(this.totalCount() / this.pageSize));

  sortedWebhooks = computed(() => {
    const webhooks = this.webhooks();
    if (!webhooks) return null;

    return [...webhooks].sort((a, b) => {
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
  });

  // The aggregated "All" view holds every item in memory, so it is paged locally.
  // A single bucket is paged on the server, so its page is shown as-is.
  paginatedWebhooks = computed(() => {
    const sorted = this.sortedWebhooks();
    if (!sorted) return null;
    if (this.selectedBucket()) return sorted;
    const start = (this.currentPage() - 1) * this.pageSize;
    return sorted.slice(start, start + this.pageSize);
  });

  constructor() {
    this.refreshBuckets();

    // Load webhooks when the selected bucket changes.
    toObservable(this.selectedBucket)
      .pipe(takeUntilDestroyed())
      .subscribe(() => {
        this.currentPage.set(1);
        this.fetchWebhooks();
      });

    // Subscribe to new webhook notifications
    this.signalRService.newWebhook$.pipe(takeUntilDestroyed()).subscribe((webhook) => {
      const currentBucket = this.selectedBucket();
      // Refresh whenever we're in the All view or the webhook belongs to the open bucket.
      if (!currentBucket || webhook.bucketName === currentBucket) {
        this.fetchWebhooks();
      }
      // Always refresh buckets to update counts/activity
      this.refreshBuckets();
    });
  }

  private fetchWebhooks() {
    const bucket = this.selectedBucket();
    this.loading.set(true);
    this.error.set(false);

    if (bucket) {
      this.webhookService.getWebhooks(bucket, this.currentPage(), this.pageSize).subscribe({
        next: (result) => {
          this.webhooks.set(result?.items ?? []);
          this.totalCount.set(result?.totalCount ?? 0);
          this.loading.set(false);
        },
        error: () => this.onFetchError(),
      });
      return;
    }

    // "All" view - aggregate webhooks across every bucket.
    this.webhookService
      .getBuckets()
      .pipe(
        map((buckets) => buckets ?? []),
        catchError(() => of([] as WebhookBucket[]))
      )
      .subscribe({
        next: (buckets) => {
          if (buckets.length === 0) {
            this.webhooks.set([]);
            this.totalCount.set(0);
            this.loading.set(false);
            return;
          }
          forkJoin(
            buckets.map((b) =>
              this.webhookService.getWebhooks(b.name, 1, AGGREGATE_PAGE_SIZE).pipe(
                map((r) => r?.items ?? []),
                catchError(() => of([] as WebhookListItem[]))
              )
            )
          ).subscribe({
            next: (lists) => {
              const all = lists.flat();
              this.webhooks.set(all);
              this.totalCount.set(all.length);
              this.loading.set(false);
            },
            error: () => this.onFetchError(),
          });
        },
        error: () => this.onFetchError(),
      });
  }

  private onFetchError() {
    this.webhooks.set([]);
    this.totalCount.set(0);
    this.loading.set(false);
    this.error.set(true);
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
      // Server-paged bucket needs a refetch; aggregated view re-slices reactively.
      if (this.selectedBucket()) this.fetchWebhooks();
    }
  }

  previousPage() {
    if (this.currentPage() > 1) {
      this.currentPage.update((page) => page - 1);
      if (this.selectedBucket()) this.fetchWebhooks();
    }
  }

  copyEndpointUrl() {
    navigator.clipboard.writeText(this.endpointUrl()).then(() => {
      this.copyTooltip.set('Copied!');
      setTimeout(() => this.copyTooltip.set('Copy URL'), 2000);
    });
  }
}
