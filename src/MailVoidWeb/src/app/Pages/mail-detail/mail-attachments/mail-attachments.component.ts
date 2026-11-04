import { ChangeDetectionStrategy, Component, input } from '@angular/core';
import { LucideDynamicIcon } from '@lucide/angular';
import { MailAttachment } from '../../../_services/api/mail.service';

@Component({
  selector: 'app-mail-attachments',
  standalone: true,
  imports: [LucideDynamicIcon],
  template: `
    @if (attachments().length > 0) {
      <div class="attachments-list" role="list" aria-label="Attachments">
        @for (att of attachments(); track $index) {
          <button
            type="button"
            role="listitem"
            class="attachment"
            (click)="downloadAttachment(att)"
            [title]="'Download ' + att.filename"
          >
            <span class="attachment-icon"><svg lucideIcon="paperclip" size="16"></svg></span>
            <span class="attachment-info">
              <span class="attachment-name">{{ att.filename }}</span>
              <span class="attachment-meta">{{ formatBytes(att.sizeBytes) }} · {{ att.contentType }}</span>
            </span>
            <svg lucideIcon="download" size="15" class="attachment-download"></svg>
          </button>
        }
      </div>
    }
  `,
  styleUrl: './mail-attachments.component.scss',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class MailAttachmentsComponent {
  attachments = input.required<MailAttachment[]>();

  formatBytes(n: number): string {
    if (n < 1024) return `${n} B`;
    if (n < 1024 * 1024) return `${(n / 1024).toFixed(1)} KB`;
    return `${(n / (1024 * 1024)).toFixed(2)} MB`;
  }

  downloadAttachment(att: MailAttachment) {
    const binary = atob(att.content);
    const bytes = new Uint8Array(binary.length);
    for (let i = 0; i < binary.length; i++) bytes[i] = binary.charCodeAt(i);
    const blob = new Blob([bytes], { type: att.contentType || 'application/octet-stream' });
    const url = URL.createObjectURL(blob);
    const a = document.createElement('a');
    a.href = url;
    a.download = att.filename || 'attachment';
    document.body.appendChild(a);
    a.click();
    document.body.removeChild(a);
    URL.revokeObjectURL(url);
  }
}
