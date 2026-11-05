import { DatePipe } from '@angular/common';
import { ChangeDetectionStrategy, Component, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { LucideDynamicIcon } from '@lucide/angular';
import { ConfirmDialogService, ToastService } from '@rd-ui';
import { environment } from '../../../../environments/environment';
import { ApiKey, ApiKeyService, CreatedApiKey } from '../../../_services/api/api-key.service';

@Component({
  selector: 'app-api-keys',
  standalone: true,
  imports: [FormsModule, DatePipe, LucideDynamicIcon],
  template: `
    <div class="settings-section-head">
      <div>
        <h4>API keys</h4>
        <p>
          Let scripts and test suites read your mail and webhook captures through the
          <code class="inline-code">/api/v1</code> endpoints. A key sees exactly what you see, and can only read.
        </p>
      </div>
    </div>

    <form class="key-form" (submit)="create($event)">
      <div class="form-group">
        <label for="key-name">New key name</label>
        <input
          id="key-name"
          class="form-control"
          type="text"
          maxlength="100"
          placeholder="e.g. CI pipeline"
          [(ngModel)]="name"
          name="name"
        />
      </div>
      <button type="submit" class="btn btn-primary" [disabled]="isCreating()">
        <svg lucideIcon="plus" size="14"></svg>
        {{ isCreating() ? 'Generating…' : 'Generate key' }}
      </button>
    </form>

    @if (newKey(); as created) {
      <div class="new-key">
        <div>
          <b>{{ created.name }}</b> created. Copy it now; it won't be shown again.
        </div>
        <div class="key-value">
          <code class="inline-code">{{ created.key }}</code>
          <button
            type="button"
            class="icon-btn icon-btn-sm"
            (click)="copy(created.key)"
            [attr.aria-label]="copied() ? 'Copied' : 'Copy key'"
            [title]="copied() ? 'Copied!' : 'Copy key'"
          >
            <svg [lucideIcon]="copied() ? 'circle-check' : 'copy'" size="14"></svg>
          </button>
        </div>
      </div>
    }

    <div class="data-table keys-table">
      <div class="data-table-head">
        <span>Name</span>
        <span>Key</span>
        <span>Created</span>
        <span>Last used</span>
        <span></span>
      </div>
      @if (isLoading()) {
        <div class="data-table-empty">Loading keys…</div>
      } @else {
        @for (key of keys(); track key.id) {
          <div class="data-table-row">
            <span class="cell-ellipsis key-name">{{ key.name }}</span>
            <span
              ><code class="inline-code">{{ key.prefix }}&hellip;</code></span
            >
            <span class="text-muted">{{ key.createdAt | date: 'mediumDate' }}</span>
            <span class="text-muted">
              @if (key.lastUsedAt) {
                {{ key.lastUsedAt | date: 'medium' }}
              } @else {
                Never
              }
            </span>
            <div class="data-table-actions">
              <button
                type="button"
                class="icon-btn icon-btn-sm icon-btn-danger"
                (click)="remove(key)"
                [disabled]="isDeleting() === key.id"
                [attr.aria-label]="'Delete key ' + key.name"
                title="Delete key"
              >
                <svg lucideIcon="trash-2" size="14"></svg>
              </button>
            </div>
          </div>
        } @empty {
          <div class="data-table-empty">No API keys yet.</div>
        }
      }
    </div>

    @if (errorMessage()) {
      <div class="message message-error">{{ errorMessage() }}</div>
    }

    <details class="disclosure">
      <summary>How to use a key</summary>
      <p class="settings-subtitle usage-hint">
        Send it in an <code class="inline-code">X-Api-Key</code> header (or
        <code class="inline-code">Authorization: Bearer</code>). All endpoints are GET.
      </p>
      <pre class="code-block">{{ usageExample }}</pre>
    </details>
  `,
  styleUrl: './api-keys.component.scss',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class ApiKeysComponent {
  private apiKeys = inject(ApiKeyService);
  private toastr = inject(ToastService);
  private confirmDialog = inject(ConfirmDialogService);

  keys = signal<ApiKey[]>([]);
  newKey = signal<CreatedApiKey | null>(null);
  isLoading = signal(false);
  isCreating = signal(false);
  isDeleting = signal<number | null>(null);
  copied = signal(false);
  errorMessage = signal('');
  name = '';

  private copiedTimer?: ReturnType<typeof setTimeout>;

  readonly usageExample = [
    `# newest mail, optionally filtered: ?to=<address>&search=<text>&page=1&pageSize=50`,
    `curl -H "X-Api-Key: $KEY" ${environment.apiUrl}/api/v1/mail`,
    `curl -H "X-Api-Key: $KEY" ${environment.apiUrl}/api/v1/mail/{id}`,
    `curl -H "X-Api-Key: $KEY" ${environment.apiUrl}/api/v1/mail/boxes`,
    ``,
    `# webhook captures`,
    `curl -H "X-Api-Key: $KEY" ${environment.apiUrl}/api/v1/webhooks/buckets`,
    `curl -H "X-Api-Key: $KEY" ${environment.apiUrl}/api/v1/webhooks/buckets/{bucket}/webhooks`,
    `curl -H "X-Api-Key: $KEY" ${environment.apiUrl}/api/v1/webhooks/{id}`,
  ].join('\n');

  constructor() {
    this.load();
  }

  load() {
    this.isLoading.set(true);
    this.errorMessage.set('');
    this.apiKeys.getKeys().subscribe({
      next: (keys) => {
        this.keys.set(keys);
        this.isLoading.set(false);
      },
      error: () => {
        this.errorMessage.set('Failed to load API keys');
        this.isLoading.set(false);
      },
    });
  }

  create(event: Event) {
    event.preventDefault();
    if (this.isCreating()) return;
    this.isCreating.set(true);
    this.errorMessage.set('');
    this.apiKeys.createKey(this.name.trim()).subscribe({
      next: (created) => {
        this.newKey.set(created);
        this.copied.set(false);
        this.name = '';
        this.isCreating.set(false);
        this.load();
      },
      error: (error) => {
        // 400 carries a plain-text reason (name too long, key limit reached).
        this.errorMessage.set(
          error.status === 400 && typeof error.error === 'string' && error.error
            ? error.error
            : 'Failed to create API key'
        );
        this.isCreating.set(false);
      },
    });
  }

  remove(key: ApiKey) {
    this.confirmDialog.confirmDelete(`the key "${key.name}"`).subscribe((confirmed) => {
      if (!confirmed) return;
      this.isDeleting.set(key.id);
      this.errorMessage.set('');
      this.apiKeys.deleteKey(key.id).subscribe({
        next: () => {
          if (this.newKey()?.id === key.id) this.newKey.set(null);
          this.keys.update((keys) => keys.filter((k) => k.id !== key.id));
          this.isDeleting.set(null);
          this.toastr.success('API key deleted');
        },
        error: () => {
          this.errorMessage.set('Failed to delete API key');
          this.isDeleting.set(null);
        },
      });
    });
  }

  copy(value: string) {
    navigator.clipboard.writeText(value).then(() => {
      this.copied.set(true);
      clearTimeout(this.copiedTimer);
      this.copiedTimer = setTimeout(() => this.copied.set(false), 1500);
    });
  }
}
