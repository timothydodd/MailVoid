import { DatePipe } from '@angular/common';
import { ChangeDetectionStrategy, Component, computed, inject, input, output, signal } from '@angular/core';
import { takeUntilDestroyed, toObservable } from '@angular/core/rxjs-interop';
import { DomSanitizer, SafeHtml } from '@angular/platform-browser';
import { LucideDynamicIcon } from '@lucide/angular';
import { catchError, of, switchMap, tap } from 'rxjs';
import { Mail, MailAttachment, MailService } from '../../../_services/api/mail.service';
import { TRUSTED_SENDERS_KEY } from '../../../_services/auth-service';
import { initialsFor } from '../../../_services/mail-format';
import { MailAttachmentsComponent } from '../mail-attachments/mail-attachments.component';

type Tab = 'preview' | 'headers' | 'raw';

/**
 * Renders one message (header, Message/Headers/Raw tabs, attachments). Used by the
 * full-page mail route and by the slide-over reader on the mail list.
 */
@Component({
  selector: 'app-mail-view',
  standalone: true,
  imports: [DatePipe, LucideDynamicIcon, MailAttachmentsComponent],
  templateUrl: './mail-view.component.html',
  styleUrl: './mail-view.component.scss',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class MailViewComponent {
  mailId = input.required<string | number>();
  /** Emits once the message has loaded (and been marked read on the server). */
  loaded = output<Mail>();

  private mailService = inject(MailService);
  private sanitizer = inject(DomSanitizer);

  mail = signal<Mail | null>(null);
  loading = signal(true);
  failed = signal(false);
  activeTab = signal<Tab>('preview');
  loadRemote = signal(false);
  copied = signal(false);
  trustedSenders = signal<string[]>(this.loadTrustedSenders());
  private copiedTimer?: ReturnType<typeof setTimeout>;

  // Sender address (unwrapped from a "Name <addr>" From header), used to remember
  // per-sender "always load remote content" preferences.
  senderEmail = computed(() => this.extractEmail(this.mail()?.from));
  initials = computed(() => initialsFor(this.mail()?.fromName || this.mail()?.from));
  isSenderTrusted = computed(() => {
    const email = this.senderEmail();
    return !!email && this.trustedSenders().includes(email);
  });
  // The From header is trivially spoofable, so a trusted sender only auto-loads remote
  // content when the message is authenticated (SPF or DKIM pass). Otherwise the user
  // can still load it manually.
  isSenderAuthenticated = computed(() => {
    const m = this.mail();
    return this.isPass(m?.spfResult) || this.isPass(m?.dkimResult);
  });

  attachments = computed<MailAttachment[]>(() => {
    const raw = this.mail()?.attachments;
    if (!raw) return [];
    try {
      return JSON.parse(raw) as MailAttachment[];
    } catch {
      return [];
    }
  });

  headers = computed<{ name: string; value: string }[]>(() => {
    const raw = this.mail()?.headers;
    if (!raw) return [];
    try {
      const obj = JSON.parse(raw) as Record<string, string>;
      return Object.entries(obj).map(([name, value]) => ({ name, value }));
    } catch {
      return [];
    }
  });

  hasRawSource = computed(() => !!this.mail()?.rawSource);

  // Render untrusted email HTML into a fully sandboxed iframe (opaque origin, no
  // scripts, no same-origin). A CSP meta blocks remote resource loads by default;
  // "Load images" re-renders with remote content allowed.
  srcDoc = computed<SafeHtml | null>(() => {
    const m = this.mail();
    if (!m?.isHtml) return null;
    const csp = this.loadRemote()
      ? "default-src 'none'; img-src data: https: http:; media-src data: https: http:; style-src 'unsafe-inline' data: https: http:; font-src data: https: http:;"
      : "default-src 'none'; img-src data:; style-src 'unsafe-inline'; font-src data:;";
    const html =
      `<!doctype html><html><head><meta charset="utf-8">` +
      `<meta http-equiv="Content-Security-Policy" content="${csp}">` +
      `<base target="_blank"></head><body>${m.text ?? ''}</body></html>`;
    return this.sanitizer.bypassSecurityTrustHtml(html);
  });

  constructor() {
    toObservable(this.mailId)
      .pipe(
        tap(() => {
          this.loading.set(true);
          this.failed.set(false);
        }),
        switchMap((id) => this.mailService.getEmail(String(id), true).pipe(catchError(() => of(null)))),
        takeUntilDestroyed()
      )
      .subscribe((m) => {
        this.mail.set(m);
        this.loading.set(false);
        this.failed.set(!m);
        this.activeTab.set('preview');
        // Auto-load remote content only for a trusted sender whose mail authenticated.
        this.loadRemote.set(this.isSenderTrusted() && this.isSenderAuthenticated());
        if (m) this.loaded.emit(m);
      });
  }

  setTab(tab: Tab) {
    this.activeTab.set(tab);
  }

  toggleRemote() {
    this.loadRemote.update((v) => !v);
  }

  // Toggle the current sender's "always load remote content" preference. Enabling it
  // also loads remote content for the message on screen right away.
  toggleAlwaysAllowSender() {
    const email = this.senderEmail();
    if (!email) return;
    const list = this.trustedSenders();
    const next = list.includes(email) ? list.filter((e) => e !== email) : [...list, email];
    this.trustedSenders.set(next);
    localStorage.setItem(TRUSTED_SENDERS_KEY, JSON.stringify(next));
    if (next.includes(email)) this.loadRemote.set(true);
  }

  copyEmail(email: string | null | undefined) {
    if (!email) return;
    navigator.clipboard.writeText(email).then(() => {
      // Inline feedback: swap the copy icon to a check briefly instead of a toast.
      this.copied.set(true);
      clearTimeout(this.copiedTimer);
      this.copiedTimer = setTimeout(() => this.copied.set(false), 1500);
    });
  }

  badgeClass(result: string | null | undefined): string {
    if (!result) return 'badge-secondary';
    const r = result.toLowerCase();
    if (r === 'pass') return 'badge-success';
    if (r === 'fail' || r === 'softfail') return 'badge-danger';
    return 'badge-secondary';
  }

  private isPass(result: string | null | undefined): boolean {
    return result?.trim().toLowerCase() === 'pass';
  }

  private extractEmail(from: string | null | undefined): string | null {
    if (!from) return null;
    const match = from.match(/<([^>]+)>/);
    const email = (match ? match[1] : from).trim().toLowerCase();
    return email || null;
  }

  private loadTrustedSenders(): string[] {
    try {
      const raw = localStorage.getItem(TRUSTED_SENDERS_KEY);
      return raw ? (JSON.parse(raw) as string[]) : [];
    } catch {
      return [];
    }
  }
}
