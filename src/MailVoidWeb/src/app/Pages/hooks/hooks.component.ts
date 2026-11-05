import { DatePipe } from '@angular/common';
import { Component, computed, inject, signal, ChangeDetectionStrategy } from '@angular/core';
import { takeUntilDestroyed, toObservable } from '@angular/core/rxjs-interop';
import { ActivatedRoute, Router } from '@angular/router';
import { LucideDynamicIcon } from '@lucide/angular';
import { ConfirmDialogService, ToastService } from '@rd-ui';
import { auditTime, catchError, filter, forkJoin, map, Observable, of, Subject, switchMap } from 'rxjs';
import { WebhookBucket, WebhookListItem, WebhookService } from '../../_services/api/webhook.service';
import { SignalRService } from '../../services/signalr.service';
import { shortMailDate } from '../../_services/mail-format';
import { MobileMenuService } from '../../_services/mobile-menu.service';
import { HookDetailComponent } from './hook-detail/hook-detail.component';
import { HookListComponent, HookSortColumn } from './hook-list/hook-list.component';

type SortColumn = HookSortColumn;
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
  imports: [DatePipe, LucideDynamicIcon, HookDetailComponent, HookListComponent],
  host: {
    '(document:keydown.arrowdown)': 'onArrow($event, 1)',
    '(document:keydown.arrowup)': 'onArrow($event, -1)',
    '(document:keydown.escape)': 'selectWebhook(null)',
  },
  template: `
    <div class="shell-layout">
      <aside class="shell-sidebar side-nav" [class.open]="menuOpen()" aria-label="Buckets">
        <div class="shell-sidebar-mobile-head">
          <span>Buckets</span>
          <button type="button" class="icon-btn" (click)="menuOpen.set(false)" aria-label="Close buckets">
            <svg lucideIcon="x" size="18"></svg>
          </button>
        </div>
        <div class="side-nav-body">
          <button
            type="button"
            class="side-item"
            [class.selected]="selectedBucket() === null"
            (click)="selectBucket(null)"
          >
            <svg lucideIcon="inbox" size="16"></svg>
            <span class="side-item-label">All webhooks</span>
          </button>
          <div class="side-section">
            <div class="side-section-label">
              <span class="side-section-title">Buckets</span>
              @if (buckets()?.length) {
                <span class="side-section-meta">{{ buckets()?.length }}</span>
              }
            </div>
            @for (bucket of buckets() ?? []; track bucket.id) {
              <button
                type="button"
                class="side-item"
                [class.selected]="selectedBucket() === bucket.name"
                (click)="selectBucket(bucket.name)"
              >
                <svg lucideIcon="folder" size="16"></svg>
                <span class="side-item-label">{{ bucket.name }}</span>
                @if (bucket.lastActivity) {
                  <span class="side-item-meta" [attr.title]="bucket.lastActivity | date: 'medium'">{{
                    shortDate(bucket.lastActivity)
                  }}</span>
                }
              </button>
            } @empty {
              <p class="side-empty">No buckets yet. Send a webhook to create one.</p>
            }
          </div>
        </div>
      </aside>

      @if (menuOpen()) {
        <div class="shell-sidebar-backdrop" (click)="menuOpen.set(false)"></div>
      }

      <main class="shell-main">
        <div class="page-toolbar">
          <div class="page-toolbar-title">
            <h1 [title]="selectedBucket() ?? 'All webhooks'">{{ selectedBucket() ?? 'All webhooks' }}</h1>
            <p>{{ subtitle() }}</p>
          </div>
          <button type="button" class="btn-quiet btn-quiet-danger" (click)="purge()" [disabled]="totalCount() === 0">
            <svg lucideIcon="trash-2" size="14"></svg>
            Purge history
          </button>
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

        @if (selectedBucket()) {
          <div class="notice-bar">
            <svg lucideIcon="webhook" size="14"></svg>
            <span class="notice-bar-text">
              Send requests to <code class="inline-code endpoint">{{ endpointUrl() }}</code>
            </span>
            <button type="button" class="link-btn" (click)="copyEndpointUrl()">{{ copyLabel() }}</button>
          </div>
        }

        <div class="list-card">
          <app-hook-list
            [webhooks]="paginatedWebhooks()"
            [loading]="loading()"
            [error]="error()"
            [showBucket]="!selectedBucket()"
            [sortColumn]="sortColumn()"
            [sortDirection]="sortDirection()"
            [openId]="selectedId()"
            (sort)="toggleSort($event)"
            (open)="selectWebhook($event)"
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

    <!-- Detail: slide-over panel above the list; click outside or Esc closes it. -->
    @if (selectedId(); as id) {
      <div class="slide-panel-overlay" (click)="selectWebhook(null)"></div>
      <app-hook-detail
        class="slide-panel slide-panel-wide"
        role="dialog"
        aria-modal="true"
        aria-label="Webhook"
        [webhookId]="id"
        [position]="openPosition()"
        [hasPrevious]="hasPrevious()"
        [hasNext]="hasNext()"
        (previous)="step(-1)"
        (next)="step(1)"
        (close)="selectWebhook(null)"
        (deleted)="onDeleted($event)"
      />
    }
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
  private mobileMenu = inject(MobileMenuService);

  shortDate = shortMailDate;
  // Phones: the bucket sidebar is a drawer toggled from the nav bar.
  menuOpen = signal(false);

  buckets = signal<WebhookBucket[] | null>(null);
  // Full dataset for the current view (a bucket's page, or all buckets aggregated).
  webhooks = signal<WebhookListItem[] | null>(null);
  // Both are driven by the URL (?bucket=…&id=…) so selection survives reloads and the back button.
  selectedBucket = signal<string | null>(null);
  selectedId = signal<number | null>(null);
  copyLabel = signal('Copy URL');
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

  subtitle = computed(() => {
    const n = this.totalCount();
    if (this.loading() && !this.webhooks()) return 'Loading…';
    return n === 0 ? 'Nothing captured yet' : `${n} captured`;
  });

  rangeLabel = computed(() => {
    const total = this.totalCount();
    const start = (this.currentPage() - 1) * this.pageSize + 1;
    const end = Math.min(start + this.pageSize - 1, total);
    return `${start}–${end} of ${total}`;
  });

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
  openPosition = computed(() => {
    const index = this.selectedIndex();
    if (index < 0) return '';
    return `${(this.currentPage() - 1) * this.pageSize + index + 1} of ${this.totalCount()}`;
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
      this.menuOpen.set(false);
      this.selectedId.set(Number.isInteger(id) && id > 0 ? id : null);
    });

    this.mobileMenu.menuToggled.pipe(takeUntilDestroyed()).subscribe(() => this.menuOpen.update((open) => !open));

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

  reload() {
    this.fetchWebhooks();
    this.refreshBuckets();
  }

  refreshBuckets() {
    this.bucketRefreshRequests.next();
  }

  selectBucket(name: string | null) {
    this.menuOpen.set(false);
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
      this.copyLabel.set('Copied!');
      setTimeout(() => this.copyLabel.set('Copy URL'), 2000);
    });
  }
}
