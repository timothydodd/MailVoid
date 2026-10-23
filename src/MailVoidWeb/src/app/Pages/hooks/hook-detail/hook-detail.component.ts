import { CommonModule } from '@angular/common';
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
  imports: [CommonModule, LucideDynamicIcon],
  template: `
    <div class="panel-toolbar">
      <div class="nav-group">
        <button class="btn-icon-sm" (click)="previous.emit()" [disabled]="!hasPrevious()" title="Previous (↑)">
          <svg lucideIcon="chevron-up" size="16"></svg>
        </button>
        <button class="btn-icon-sm" (click)="next.emit()" [disabled]="!hasNext()" title="Next (↓)">
          <svg lucideIcon="chevron-down" size="16"></svg>
        </button>
      </div>
      <div class="nav-group">
        <button
          class="btn-icon-sm btn-icon-danger"
          (click)="deleteWebhook()"
          [disabled]="!webhook()"
          title="Delete this webhook"
        >
          <svg lucideIcon="trash-2" size="16"></svg>
        </button>
        <button class="btn-icon-sm" (click)="close.emit()" title="Close (Esc)">
          <svg lucideIcon="x" size="16"></svg>
        </button>
      </div>
    </div>

    @if (webhook(); as hook) {
      <div class="panel-meta">
        <div class="meta-line">
          <span class="method-badge method-{{ hook.httpMethod.toLowerCase() }}">{{ hook.httpMethod }}</span>
          <span class="meta-path">{{ hook.path }}{{ hook.queryString || '' }}</span>
          <button class="copy-btn" (click)="copy(hook.path + (hook.queryString || ''))" title="Copy path">
            <svg lucideIcon="copy" size="14"></svg>
          </button>
        </div>
        <div class="meta-line meta-facts">
          <span><span class="meta-label">Bucket</span> {{ hook.bucketName }}</span>
          <span><span class="meta-label">Received</span> {{ hook.createdOn | date: 'medium' }}</span>
          @if (hook.sourceIp) {
            <span><span class="meta-label">From</span> {{ hook.sourceIp }}</span>
          }
        </div>
      </div>

      <div class="nav-tabs panel-tabs">
        <button class="nav-tab" [class.active]="tab() === 'body'" (click)="tab.set('body')">
          Body
          @if (hook.contentType) {
            <span class="tab-hint">{{ shortContentType() }}</span>
          }
        </button>
        <button class="nav-tab" [class.active]="tab() === 'headers'" (click)="tab.set('headers')">
          Headers
          @if (headerEntries(); as entries) {
            <span class="tab-hint">{{ entries.length }}</span>
          }
        </button>
        <span class="tabs-spacer"></span>
        <button class="btn btn-outline btn-sm" (click)="copy(tab() === 'body' ? hook.body : hook.headers)">
          <svg lucideIcon="copy" size="14"></svg>
          {{ copyLabel() }}
        </button>
      </div>

      <div class="panel-body">
        @if (tab() === 'body') {
          @if (hook.body) {
            <pre class="raw" [class.json]="isJsonBody()">{{ prettyBody() }}</pre>
          } @else {
            <p class="empty">No body</p>
          }
        } @else {
          @if (headerEntries(); as entries) {
            <table class="headers-table">
              <tbody>
                @for (entry of entries; track entry.key) {
                  <tr>
                    <td class="header-name">{{ entry.key }}</td>
                    <td class="header-value">{{ entry.value }}</td>
                  </tr>
                }
              </tbody>
            </table>
          } @else {
            <pre class="raw">{{ hook.headers }}</pre>
          }
        }
      </div>
    } @else if (error()) {
      <p class="empty">Failed to load this webhook.</p>
    } @else {
      <div class="loading-container">
        <div class="loading-spinner"></div>
      </div>
    }
  `,
  changeDetection: ChangeDetectionStrategy.OnPush,
  styleUrl: './hook-detail.component.scss',
})
export class HookDetailComponent {
  webhookId = input.required<number>();
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
  copyLabel = signal('Copy');

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

  copy(text: string) {
    navigator.clipboard.writeText(text).then(() => {
      this.copyLabel.set('Copied!');
      setTimeout(() => this.copyLabel.set('Copy'), 1500);
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
