import { ChangeDetectionStrategy, Component, input } from '@angular/core';
import { LucideDynamicIcon } from '@lucide/angular';
import { MailAttachment } from '../../../_services/api/mail.service';

@Component({
  selector: 'app-mail-attachments',
  standalone: true,
  imports: [LucideDynamicIcon],
  template: `
    @if (attachments().length > 0) {
      <div class="attachments-panel">
        <div class="attachments-header">
          <svg lucideIcon="paperclip" size="16"></svg>
          <span>{{ attachments().length }} attachment{{ attachments().length === 1 ? '' : 's' }}</span>
        </div>
        <div class="attachments-list">
          @for (att of attachments(); track att.filename) {
            <button class="attachment" (click)="downloadAttachment(att)" [title]="'Download ' + att.filename">
              <svg lucideIcon="file" size="20"></svg>
              <div class="attachment-info">
                <div class="attachment-name">{{ att.filename }}</div>
                <div class="attachment-meta">{{ att.contentType }} &middot; {{ formatBytes(att.sizeBytes) }}</div>
              </div>
              <svg lucideIcon="download" size="16"></svg>
            </button>
          }
        </div>
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
