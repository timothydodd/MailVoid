import { DatePipe } from '@angular/common';
import { ChangeDetectionStrategy, Component, computed, inject, input, output, signal } from '@angular/core';
import { takeUntilDestroyed, toObservable } from '@angular/core/rxjs-interop';
import { LucideDynamicIcon } from '@lucide/angular';
import { catchError, of, switchMap } from 'rxjs';
import { WebhookDetail, WebhookService } from '../../../_services/api/webhook.service';

type DetailTab = 'body' | 'headers';

/**
 * Side panel showing one captured webhook. It loads the full webhook itself so the
 * list only has to hand over an id, and reports navigation/close/delete to the parent.
 */
@Component({
  selector: 'app-hook-detail',
  standalone: true,
  imports: [DatePipe, LucideDynamicIcon],
  template: `
    <div class="slide-panel-toolbar">
      <button type="button" class="icon-btn" (click)="close.emit()" aria-label="Close" title="Close (Esc)">
        <svg lucideIcon="x" size="18"></svg>
      </button>
      <button
        type="button"
        class="icon-btn"
        (click)="previous.emit()"
        [disabled]="!hasPrevious()"
        aria-label="Previous webhook"
        title="Previous (↑)"
      >
        <svg lucideIcon="chevron-up" size="18"></svg>
      </button>
      <button
        type="button"
        class="icon-btn"
        (click)="next.emit()"
        [disabled]="!hasNext()"
        aria-label="Next webhook"
        title="Next (↓)"
      >
        <svg lucideIcon="chevron-down" size="18"></svg>
      </button>
      @if (position()) {
        <span class="slide-panel-position">{{ position() }}</span>
      }
      <span class="spacer"></span>
      <button
        type="button"
        class="icon-btn icon-btn-danger"
        (click)="deleteWebhook()"
        [disabled]="!webhook()"
        aria-label="Delete webhook"
        title="Delete this webhook"
      >
        <svg lucideIcon="trash-2" size="16"></svg>
      </button>
    </div>

    @if (webhook(); as hook) {
      <header class="hd-header">
        <div class="hd-request">
          <span class="method-badge method-{{ hook.httpMethod.toLowerCase() }}">{{ hook.httpMethod }}</span>
          <span class="hd-path">{{ hook.path }}{{ hook.queryString || '' }}</span>
          <button
            type="button"
            class="icon-btn icon-btn-sm"
            (click)="copy(hook.path + (hook.queryString || ''), 'path')"
            [attr.aria-label]="copied() === 'path' ? 'Copied' : 'Copy path'"
            [title]="copied() === 'path' ? 'Copied!' : 'Copy path'"
          >
            <svg [lucideIcon]="copied() === 'path' ? 'circle-check' : 'copy'" size="14"></svg>
          </button>
        </div>
        <div class="hd-facts">
          <span>{{ hook.bucketName }}</span>
          <time [attr.datetime]="hook.createdOn">{{ hook.createdOn | date: 'medium' }}</time>
          @if (hook.sourceIp) {
            <span>from {{ hook.sourceIp }}</span>
          }
        </div>

        <div class="tab-bar" role="tablist">
          <button
            type="button"
            role="tab"
            class="tab-bar-item"
            [class.active]="tab() === 'body'"
            [attr.aria-selected]="tab() === 'body'"
            (click)="tab.set('body')"
          >
            Body
            @if (hook.contentType) {
              <span class="count-pill count-pill-muted">{{ shortContentType() }}</span>
            }
          </button>
          <button
            type="button"
            role="tab"
            class="tab-bar-item"
            [class.active]="tab() === 'headers'"
            [attr.aria-selected]="tab() === 'headers'"
            (click)="tab.set('headers')"
          >
            Headers
            @if (headerEntries(); as entries) {
              <span class="count-pill count-pill-muted">{{ entries.length }}</span>
            }
          </button>
          <span class="spacer"></span>
          <button
            type="button"
            class="btn-quiet btn-quiet-sm"
            (click)="copy(tab() === 'body' ? prettyBody() : hook.headers, 'content')"
          >
            <svg [lucideIcon]="copied() === 'content' ? 'circle-check' : 'copy'" size="13"></svg>
            {{ copied() === 'content' ? 'Copied' : 'Copy' }}
          </button>
        </div>
      </header>

      <div class="hd-body">
        @if (tab() === 'body') {
          @if (hook.body) {
            <pre class="code-block">{{ prettyBody() }}</pre>
          } @else {
            <p class="list-status">No body</p>
          }
        } @else {
          @if (headerEntries(); as entries) {
            <div class="kv-list">
              @for (entry of entries; track entry.key) {
                <div class="kv-row">
                  <span class="kv-key">{{ entry.key }}</span>
                  <span>{{ entry.value }}</span>
                </div>
              }
            </div>
          } @else {
            <pre class="code-block">{{ hook.headers }}</pre>
          }
        }
      </div>
    } @else if (error()) {
      <p class="list-status list-status-error">Failed to load this webhook.</p>
    } @else {
      <div class="loading-container hd-loading">
        <div class="loading-spinner"></div>
      </div>
    }
  `,
  changeDetection: ChangeDetectionStrategy.OnPush,
  styleUrl: './hook-detail.component.scss',
})
export class HookDetailComponent {
  webhookId = input.required<number>();
  position = input('');
  hasPrevious = input(false);
  hasNext = input(false);

  previous = output<void>();
  next = output<void>();
  close = output<void>();
  deleted = output<number>();

  private webhookService = inject(WebhookService);

  webhook = signal<WebhookDetail | null>(null);
  error = signal(false);
  tab = signal<DetailTab>('body');
  // Which copy button last succeeded, for its brief "Copied" state.
  copied = signal<'path' | 'content' | null>(null);
  private copiedTimer?: ReturnType<typeof setTimeout>;

  headerEntries = computed(() => {
    const hook = this.webhook();
    if (!hook) return null;
    try {
      const parsed = JSON.parse(hook.headers) as Record<string, string>;
      return Object.keys(parsed)
        .sort((a, b) => a.localeCompare(b))
        .map((key) => ({ key, value: parsed[key] }));
    } catch {
      return null;
    }
  });

  private parsedBody = computed(() => {
    const hook = this.webhook();
    if (!hook) return { text: '', json: false };
    try {
      return { text: JSON.stringify(JSON.parse(hook.body), null, 2), json: true };
    } catch {
      return { text: hook.body, json: false };
    }
  });
  prettyBody = computed(() => this.parsedBody().text);
  isJsonBody = computed(() => this.parsedBody().json);

  shortContentType = computed(() => (this.webhook()?.contentType ?? '').split(';')[0].trim());

  constructor() {
    // switchMap drops the response of a superseded id, so fast prev/next can't show a stale webhook.
    toObservable(this.webhookId)
      .pipe(
        switchMap((id) => {
          this.webhook.set(null);
          this.error.set(false);
          return this.webhookService.getWebhook(id).pipe(catchError(() => of(null)));
        }),
        takeUntilDestroyed()
      )
      .subscribe((hook) => {
        this.webhook.set(hook);
        this.error.set(!hook);
      });
  }

  copy(text: string, what: 'path' | 'content') {
    navigator.clipboard.writeText(text).then(() => {
      this.copied.set(what);
      clearTimeout(this.copiedTimer);
      this.copiedTimer = setTimeout(() => this.copied.set(null), 1500);
    });
  }

  deleteWebhook() {
    const hook = this.webhook();
    if (!hook) return;
    this.webhookService.deleteWebhook(hook.id).subscribe({
      next: () => this.deleted.emit(hook.id),
      error: () => this.error.set(true),
    });
  }
}
