import { CommonModule } from '@angular/common';
import { Component, computed, inject, signal, ChangeDetectionStrategy } from '@angular/core';
import { takeUntilDestroyed, toObservable } from '@angular/core/rxjs-interop';
import { ActivatedRoute, Router } from '@angular/router';
import { LucideDynamicIcon } from '@lucide/angular';
import { ConfirmDialogService, ToastService } from '@rd-ui';
import { auditTime, catchError, filter, forkJoin, map, Observable, of, Subject, switchMap } from 'rxjs';
import { WebhookBucket, WebhookListItem, WebhookService } from '../../_services/api/webhook.service';
import { SignalRService } from '../../services/signalr.service';
import { HookDetailComponent } from './hook-detail/hook-detail.component';

type SortColumn = 'httpMethod' | 'path' | 'contentType' | 'createdOn';
type SortDirection = 'asc' | 'desc';

// Large page used to pull a bucket's full history when aggregating the "All" view.
const AGGREGATE_PAGE_SIZE = 500;

// Minimum gap between live (SignalR-driven) refreshes of the list and sidebar.
const LIVE_REFRESH_INTERVAL_MS = 1000;

interface FetchResult {
  ok: boolean;
  items: WebhookListItem[];
  totalCount: number;
  // Set when the fetch also loaded the bucket list (the aggregated "All" view).
  buckets?: WebhookBucket[];
}

@Component({
  selector: 'app-hooks',
  standalone: true,
  imports: [CommonModule, LucideDynamicIcon, HookDetailComponent],
  host: {
    '(document:keydown.arrowdown)': 'onArrow($event, 1)',
    '(document:keydown.arrowup)': 'onArrow($event, -1)',
    '(document:keydown.escape)': 'selectWebhook(null)',
  },
  template: `
    <div class="hooks-container" [class.has-detail]="selectedId() !== null">
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
            @if (buckets()?.length) {
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

      <!-- List -->
      <div class="hooks-content">
        <div class="content-header">
          <div class="content-title">
            <h2>{{ selectedBucket() ?? 'All Webhooks' }}</h2>
            @if (selectedBucket()) {
              <div class="endpoint-row">
                <code class="endpoint-url">{{ endpointUrl() }}</code>
                <button class="copy-btn" (click)="copyEndpointUrl()" [title]="copyTooltip()">
                  <svg lucideIcon="copy" size="14"></svg>
                </button>
              </div>
            }
          </div>
          <div class="content-actions">
            @if (totalCount() > 0) {
              <span class="count-label">{{ totalCount() }} captured</span>
            }
            <button class="btn btn-outline btn-sm btn-purge" (click)="purge()" [disabled]="totalCount() === 0">
              <svg lucideIcon="trash-2" size="14"></svg>
              Purge history
            </button>
          </div>
        </div>

        <div class="table-wrap">
          <table>
            <thead>
              <tr>
                @for (col of columns; track col.key) {
                  <th class="sortable" [class.optional]="col.optional" (click)="toggleSort(col.key)">
                    {{ col.label }}
                    @if (sortColumn() === col.key) {
                      <svg [lucideIcon]="sortDirection() === 'asc' ? 'chevron-up' : 'chevron-down'" size="14"></svg>
                    }
                  </th>
                }
              </tr>
            </thead>
            <tbody>
              @if (loading()) {
                <tr class="read">
                  <td colspan="4" class="table-status">Loading webhooks...</td>
                </tr>
              } @else if (error()) {
                <tr class="read">
                  <td colspan="4" class="table-status table-status-error">
                    Failed to load webhooks. Please try again.
                  </td>
                </tr>
              } @else if (paginatedWebhooks()?.length) {
                @for (webhook of paginatedWebhooks(); track webhook.id) {
                  <tr
                    class="read"
                    role="button"
                    tabindex="0"
                    [class.selected]="webhook.id === selectedId()"
                    (click)="selectWebhook(webhook.id)"
                    (keydown.enter)="selectWebhook(webhook.id)"
                    (keydown.space)="$event.preventDefault(); selectWebhook(webhook.id)"
                  >
                    <td>
                      <span class="method-badge method-{{ webhook.httpMethod.toLowerCase() }}">{{
                        webhook.httpMethod
                      }}</span>
                    </td>
                    <td class="path-cell">
                      {{ webhook.path }}{{ webhook.queryString || '' }}
                      @if (!selectedBucket()) {
                        <span class="row-bucket">{{ webhook.bucketName }}</span>
                      }
                    </td>
                    <td class="optional">{{ webhook.contentType || '-' }}</td>
                    <td class="date-cell">{{ webhook.createdOn | date: 'short' }}</td>
                  </tr>
                }
              } @else {
                <tr class="read">
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

      <!-- Detail panel -->
      @if (selectedId(); as id) {
        <app-hook-detail
          class="hooks-detail"
          [webhookId]="id"
          [hasPrevious]="hasPrevious()"
          [hasNext]="hasNext()"
          (previous)="step(-1)"
          (next)="step(1)"
          (close)="selectWebhook(null)"
          (deleted)="onDeleted($event)"
        />
      }
    </div>
  `,
  changeDetection: ChangeDetectionStrategy.OnPush,
  styleUrl: './hooks.component.scss',
})
export class HooksComponent {
  private router = inject(Router);
  private route = inject(ActivatedRoute);
  private webhookService = inject(WebhookService);
  private signalRService = inject(SignalRService);
  private confirmDialog = inject(ConfirmDialogService);
  private toast = inject(ToastService);

  readonly columns: { key: SortColumn; label: string; optional?: boolean }[] = [
    { key: 'httpMethod', label: 'Method' },
    { key: 'path', label: 'Path' },
    { key: 'contentType', label: 'Content-Type', optional: true },
    { key: 'createdOn', label: 'Received' },
  ];

  buckets = signal<WebhookBucket[] | null>(null);
  // Full dataset for the current view (a bucket's page, or all buckets aggregated).
  webhooks = signal<WebhookListItem[] | null>(null);
  // Both are driven by the URL (?bucket=…&id=…) so selection survives reloads and the back button.
  selectedBucket = signal<string | null>(null);
  selectedId = signal<number | null>(null);
  copyTooltip = signal<string>('Copy URL');
  loading = signal(false);
  error = signal(false);

  // Server total for the selected bucket; for the aggregated "All" view it is the
  // total number of aggregated items.
  totalCount = signal(0);

  endpointUrl = computed(() => `${window.location.origin}/api/hook/${this.selectedBucket()}`);

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

  // Position of the open webhook in the visible page; -1 when it isn't on this page.
  private selectedIndex = computed(() => {
    const id = this.selectedId();
    return id === null ? -1 : (this.paginatedWebhooks() ?? []).findIndex((w) => w.id === id);
  });
  hasPrevious = computed(() => this.selectedIndex() > 0 || (this.selectedIndex() === 0 && this.currentPage() > 1));
  hasNext = computed(() => {
    const index = this.selectedIndex();
    const page = this.paginatedWebhooks() ?? [];
    return index >= 0 && (index < page.length - 1 || this.currentPage() < this.totalPages());
  });

  // When stepping past a page edge, the next fetch selects this end of the new page.
  private selectAfterLoad: 'first' | 'last' | null = null;

  // Every list fetch goes through this stream; switchMap cancels any in-flight
  // request so only the latest (bucket, page) can render.
  // background = true skips the "Loading..." placeholder (used for live refreshes).
  private fetchRequests = new Subject<{ background: boolean }>();
  private bucketRefreshRequests = new Subject<void>();

  constructor() {
    this.fetchRequests
      .pipe(
        switchMap(({ background }) => {
          if (!background) {
            this.loading.set(true);
          }
          this.error.set(false);
          // Bucket and page are captured now, so a superseded request can never apply.
          return this.loadWebhooks(this.selectedBucket(), this.currentPage());
        }),
        takeUntilDestroyed()
      )
      .subscribe((result) => {
        if (result.buckets) {
          this.buckets.set(result.buckets);
        }
        this.webhooks.set(result.items);
        this.totalCount.set(result.totalCount);
        this.loading.set(false);
        this.error.set(!result.ok);

        const edge = this.selectAfterLoad;
        this.selectAfterLoad = null;
        const page = this.paginatedWebhooks() ?? [];
        if (edge && page.length) {
          this.selectWebhook(page[edge === 'first' ? 0 : page.length - 1].id);
        }
      });

    this.bucketRefreshRequests
      .pipe(
        switchMap(() => this.webhookService.getBuckets().pipe(catchError(() => of(null)))),
        takeUntilDestroyed()
      )
      .subscribe((buckets) => {
        if (buckets) this.buckets.set(buckets);
      });

    // The URL is the source of truth for bucket + selection.
    this.route.queryParamMap.pipe(takeUntilDestroyed()).subscribe((params) => {
      const id = Number(params.get('id'));
      this.selectedBucket.set(params.get('bucket') || null);
      this.selectedId.set(Number.isInteger(id) && id > 0 ? id : null);
    });

    // Load webhooks when the selected bucket changes. The initial "All" view fetch
    // also loads the bucket list, so no separate bucket request is needed on init.
    toObservable(this.selectedBucket)
      .pipe(takeUntilDestroyed())
      .subscribe(() => {
        this.currentPage.set(1);
        this.fetchWebhooks();
      });

    // Live updates, throttled so a burst of webhooks causes at most one refresh per
    // second. Refresh the list in the All view or when the webhook hit the open bucket;
    // in the All view that fetch reloads the bucket list too.
    this.signalRService.newWebhook$
      .pipe(
        filter((webhook) => {
          const currentBucket = this.selectedBucket();
          return !currentBucket || webhook.bucketName === currentBucket;
        }),
        auditTime(LIVE_REFRESH_INTERVAL_MS),
        takeUntilDestroyed()
      )
      .subscribe(() => this.fetchWebhooks(true));

    // With a single bucket open, the sidebar (counts/activity/new buckets) needs its own refresh.
    this.signalRService.newWebhook$
      .pipe(
        auditTime(LIVE_REFRESH_INTERVAL_MS),
        filter(() => this.selectedBucket() !== null),
        takeUntilDestroyed()
      )
      .subscribe(() => this.refreshBuckets());
  }

  private fetchWebhooks(background = false) {
    this.fetchRequests.next({ background });
  }

  private loadWebhooks(bucket: string | null, page: number): Observable<FetchResult> {
    const failed: FetchResult = { ok: false, items: [], totalCount: 0 };

    if (bucket) {
      return this.webhookService.getWebhooks(bucket, page, this.pageSize).pipe(
        map((result): FetchResult => ({ ok: true, items: result?.items ?? [], totalCount: result?.totalCount ?? 0 })),
        catchError(() => of(failed))
      );
    }

    // "All" view - aggregate webhooks across every bucket.
    return this.webhookService.getBuckets().pipe(
      switchMap((buckets) => {
        const list = buckets ?? [];
        if (list.length === 0) {
          return of<FetchResult>({ ok: true, items: [], totalCount: 0, buckets: list });
        }
        return forkJoin(
          list.map((b) =>
            this.webhookService.getWebhooks(b.name, 1, AGGREGATE_PAGE_SIZE).pipe(
              map((r) => r?.items ?? []),
              catchError(() => of([] as WebhookListItem[]))
            )
          )
        ).pipe(
          map((lists): FetchResult => {
            const all = lists.flat();
            return { ok: true, items: all, totalCount: all.length, buckets: list };
          })
        );
      }),
      catchError(() => of(failed))
    );
  }

  refreshBuckets() {
    this.bucketRefreshRequests.next();
  }

  selectBucket(name: string | null) {
    if (name === this.selectedBucket()) return;
    // Switching bucket also drops the open webhook.
    this.router.navigate([], { relativeTo: this.route, queryParams: { bucket: name, id: null } });
  }

  selectWebhook(id: number | null) {
    if (id === this.selectedId()) return;
    this.router.navigate([], {
      relativeTo: this.route,
      queryParams: { id },
      queryParamsHandling: 'merge',
      replaceUrl: this.selectedId() !== null,
    });
  }

  /** Moves the selection by one row, crossing page boundaries when needed. */
  step(direction: 1 | -1) {
    const page = this.paginatedWebhooks() ?? [];
    const index = this.selectedIndex();
    if (index < 0) return;
    const target = index + direction;
    if (target >= 0 && target < page.length) {
      this.selectWebhook(page[target].id);
    } else if (direction === 1 && this.currentPage() < this.totalPages()) {
      this.selectAfterLoad = 'first';
      this.nextPage();
    } else if (direction === -1 && this.currentPage() > 1) {
      this.selectAfterLoad = 'last';
      this.previousPage();
    }
  }

  onArrow(event: Event, direction: 1 | -1) {
    if (this.selectedId() === null) return;
    event.preventDefault();
    this.step(direction);
  }

  onDeleted(id: number) {
    // Jump to the neighbour so the panel stays useful, then drop the row.
    const page = this.paginatedWebhooks() ?? [];
    const index = page.findIndex((w) => w.id === id);
    const neighbour = page[index + 1] ?? page[index - 1] ?? null;
    this.selectWebhook(neighbour?.id ?? null);
    this.webhooks.update((list) => list?.filter((w) => w.id !== id) ?? null);
    this.totalCount.update((n) => Math.max(0, n - 1));
    this.toast.success('Webhook deleted');
    // Background refetch keeps server paging in step without flashing the list.
    this.fetchWebhooks(true);
  }

  purge() {
    const bucket = this.selectedBucket();
    const count = this.totalCount();
    this.confirmDialog
      .confirm({
        title: 'Purge webhook history',
        message: bucket
          ? `Delete all ${count} captured webhooks in "${bucket}"? The bucket and its URL are kept.`
          : `Delete all ${count} captured webhooks in every bucket? The buckets and their URLs are kept.`,
        confirmText: 'Purge',
      })
      .pipe(
        filter(Boolean),
        switchMap(() => (bucket ? this.webhookService.purgeBucket(bucket) : this.webhookService.purgeAll()))
      )
      .subscribe({
        next: (result) => {
          this.toast.success(`Deleted ${result.deleted} webhook${result.deleted === 1 ? '' : 's'}`);
          this.selectWebhook(null);
          this.currentPage.set(1);
          this.fetchWebhooks();
          this.refreshBuckets();
        },
        error: () => this.toast.error('Failed to purge webhook history'),
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
      this.afterPageChange();
    }
  }

  previousPage() {
    if (this.currentPage() > 1) {
      this.currentPage.update((page) => page - 1);
      this.afterPageChange();
    }
  }

  private afterPageChange() {
    // Server-paged bucket needs a refetch; the aggregated view re-slices reactively,
    // so it applies any pending edge selection itself.
    if (this.selectedBucket()) {
      this.fetchWebhooks();
      return;
    }
    const edge = this.selectAfterLoad;
    this.selectAfterLoad = null;
    const page = this.paginatedWebhooks() ?? [];
    if (edge && page.length) {
      this.selectWebhook(page[edge === 'first' ? 0 : page.length - 1].id);
    }
  }

  copyEndpointUrl() {
    navigator.clipboard.writeText(this.endpointUrl()).then(() => {
      this.copyTooltip.set('Copied!');
      setTimeout(() => this.copyTooltip.set('Copy URL'), 2000);
    });
  }
}
