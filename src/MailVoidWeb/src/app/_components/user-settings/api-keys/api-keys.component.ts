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
    <p class="settings-subtitle">
      Keys let scripts and test suites read your mail and webhook captures through the
      <code>/api/v1</code> endpoints. A key sees exactly what you see here, and can only read.
    </p>

    <form class="form-row key-form" (submit)="create($event)">
      <div class="form-group">
        <label for="key-name">Key name</label>
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
        {{ isCreating() ? 'Generating...' : 'Generate key' }}
      </button>
    </form>

    @if (newKey(); as created) {
      <div class="alert alert-success new-key">
        <div>
          <b>{{ created.name }}</b> created. Copy it now &mdash; it won't be shown again.
        </div>
        <div class="key-value">
          <code>{{ created.key }}</code>
          <button
            type="button"
            class="copy-btn"
            (click)="copy(created.key)"
            [title]="copied() ? 'Copied!' : 'Copy key'"
          >
            <svg [lucideIcon]="copied() ? 'circle-check' : 'copy'" size="16"></svg>
          </button>
        </div>
      </div>
    }

    @if (isLoading()) {
      <div class="loading">Loading keys...</div>
    } @else if (keys().length === 0) {
      <div class="empty-message">No API keys yet.</div>
    } @else {
      <div class="table-grid header">
        <div class="header-cell">Name</div>
        <div class="header-cell">Key</div>
        <div class="header-cell">Created</div>
        <div class="header-cell">Last used</div>
        <div class="header-cell"></div>
      </div>
      @for (key of keys(); track key.id) {
        <div class="table-grid row">
          <div class="cell" data-label="Name">{{ key.name }}</div>
          <div class="cell" data-label="Key">
            <code>{{ key.prefix }}&hellip;</code>
          </div>
          <div class="cell" data-label="Created">{{ key.createdAt | date: 'mediumDate' }}</div>
          <div class="cell" data-label="Last used">
            @if (key.lastUsedAt) {
              {{ key.lastUsedAt | date: 'medium' }}
            } @else {
              <span class="text-muted">Never</span>
            }
          </div>
          <div class="cell actions">
            <button class="btn btn-sm btn-danger" (click)="remove(key)" [disabled]="isDeleting() === key.id">
              {{ isDeleting() === key.id ? 'Deleting...' : 'Delete' }}
            </button>
          </div>
        </div>
      }
    }

    @if (errorMessage()) {
      <div class="message message-error">{{ errorMessage() }}</div>
    }

    <details class="usage">
      <summary>How to use a key</summary>
      <p class="settings-subtitle">
        Send it in an <code>X-Api-Key</code> header (or <code>Authorization: Bearer</code>). All endpoints are GET.
      </p>
      <pre><code>{{ usageExample }}</code></pre>
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
