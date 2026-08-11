import { CommonModule } from '@angular/common';
import { Component, computed, ElementRef, inject, signal, viewChild, ChangeDetectionStrategy } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { ActivatedRoute, Router } from '@angular/router';
import { DomSanitizer, SafeHtml } from '@angular/platform-browser';
import { LucideDynamicIcon } from '@lucide/angular';
import { catchError, filter, of, switchMap } from 'rxjs';
import { Mail, MailAttachment, MailService } from '../../_services/api/mail.service';
import { ClickOutsideDirective } from '../../_services/click-outside.directive';
import { MailAttachmentsComponent } from './mail-attachments/mail-attachments.component';

const TRUSTED_SENDERS_KEY = 'mailvoid_trusted_senders';

type Tab = 'preview' | 'headers' | 'raw';

@Component({
  selector: 'app-mail-detail',
  standalone: true,
  imports: [CommonModule, LucideDynamicIcon, ClickOutsideDirective, MailAttachmentsComponent],
  templateUrl: './mail-detail.component.html',
  changeDetection: ChangeDetectionStrategy.OnPush,
  styleUrl: './mail-detail.component.scss',
})
export class MailDetailComponent {
  mail = signal<Mail | null>(null);
  activeTab = signal<Tab>('preview');
  loadRemote = signal(false);
  detailsExpanded = signal(false);
  remoteMenuOpen = signal(false);
  menuPosition = signal({ top: 0, left: 0 });
  trustedSenders = signal<string[]>(this.loadTrustedSenders());
  mailService = inject(MailService);
  private route = inject(ActivatedRoute);
  private router = inject(Router);
  private sanitizer = inject(DomSanitizer);
  copied = signal(false);
  private copiedTimer?: ReturnType<typeof setTimeout>;
  private remoteTrigger = viewChild<ElementRef>('remoteTrigger');
  private remoteMenu = viewChild<ElementRef>('remoteMenu');

  // Sender address (unwrapped from a "Name <addr>" From header), used to remember
  // per-sender "always load remote content" preferences.
  senderEmail = computed(() => this.extractEmail(this.mail()?.from));
  isSenderTrusted = computed(() => {
    const email = this.senderEmail();
    return !!email && this.trustedSenders().includes(email);
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
  // toggling "Load remote images" re-renders with remote content allowed.
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
    this.route.paramMap
      .pipe(
        filter((x) => !!x.get('id')),
        switchMap((x) =>
          this.mailService.getEmail(<string>x.get('id'), true).pipe(catchError(() => of(null)))
        ),
        takeUntilDestroyed()
      )
      .subscribe((d) => {
        this.mail.set(d);
        this.activeTab.set('preview');
        this.remoteMenuOpen.set(false);
        this.detailsExpanded.set(false);
        // Auto-load remote content if this sender has been trusted.
        this.loadRemote.set(this.isSenderTrusted());
      });
  }

  setTab(tab: Tab) {
    this.activeTab.set(tab);
  }

  toggleRemote() {
    this.loadRemote.update((v) => !v);
  }

  toggleDetails() {
    this.detailsExpanded.update((v) => !v);
  }

  toggleRemoteMenu() {
    if (this.remoteMenuOpen()) {
      this.closeRemoteMenu();
      return;
    }
    this.remoteMenuOpen.set(true);
    setTimeout(() => this.positionMenu());
  }

  closeRemoteMenu() {
    this.remoteMenuOpen.set(false);
  }

  // Toggle the current sender's "always load remote content" preference. Enabling it
  // also loads remote content for the message on screen right away.
  toggleAlwaysAllowSender() {
    const email = this.senderEmail();
    if (!email) return;
    const list = this.trustedSenders();
    if (list.includes(email)) {
      const next = list.filter((e) => e !== email);
      this.trustedSenders.set(next);
      this.saveTrustedSenders(next);
    } else {
      const next = [...list, email];
      this.trustedSenders.set(next);
      this.saveTrustedSenders(next);
      this.loadRemote.set(true);
    }
    this.closeRemoteMenu();
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

  private saveTrustedSenders(list: string[]) {
    localStorage.setItem(TRUSTED_SENDERS_KEY, JSON.stringify(list));
  }

  private positionMenu() {
    const trigger = this.remoteTrigger()?.nativeElement;
    const menu = this.remoteMenu()?.nativeElement;
    if (!trigger || !menu) return;
    const t = trigger.getBoundingClientRect();
    const m = menu.getBoundingClientRect();
    let top = t.bottom + 4;
    let left = t.right - m.width;
    if (left < 8) left = 8;
    if (left + m.width > window.innerWidth - 8) left = window.innerWidth - m.width - 8;
    if (top + m.height > window.innerHeight - 8) top = t.top - m.height - 4;
    this.menuPosition.set({ top, left });
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

  goBack() {
    this.router.navigate(['/mail']);
  }

  badgeClass(result: string | null | undefined): string {
    if (!result) return 'badge-secondary';
    const r = result.toLowerCase();
    if (r === 'pass') return 'badge-success';
    if (r === 'fail' || r === 'softfail') return 'badge-danger';
    return 'badge-secondary';
  }
}
