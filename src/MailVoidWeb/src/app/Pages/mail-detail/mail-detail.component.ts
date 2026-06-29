import { CommonModule } from '@angular/common';
import { Component, computed, effect, ElementRef, inject, signal, viewChild, ChangeDetectionStrategy } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { ActivatedRoute, Router } from '@angular/router';
import { LucideDynamicIcon } from '@lucide/angular';
import { ToastService } from '@rd-ui';
import { catchError, filter, of, switchMap } from 'rxjs';
import { Mail, MailAttachment, MailService } from '../../_services/api/mail.service';
import { MailAttachmentsComponent } from './mail-attachments/mail-attachments.component';

type Tab = 'preview' | 'headers' | 'raw';

@Component({
  selector: 'app-mail-detail',
  standalone: true,
  imports: [CommonModule, LucideDynamicIcon, MailAttachmentsComponent],
  templateUrl: './mail-detail.component.html',
  changeDetection: ChangeDetectionStrategy.Eager,
  styleUrl: './mail-detail.component.scss',
})
export class MailDetailComponent {
  mail = signal<Mail | null>(null);
  activeTab = signal<Tab>('preview');
  mailService = inject(MailService);
  private route = inject(ActivatedRoute);
  private router = inject(Router);
  private toastr = inject(ToastService);
  emailContent = viewChild<ElementRef<HTMLIFrameElement>>('emailContent');

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
      });

    effect(() => {
      const m = this.mail();
      const ref = this.emailContent();
      if (this.activeTab() !== 'preview') return;
      if (!m?.isHtml || !ref) return;
      const doc = ref.nativeElement.contentWindow?.document;
      if (!doc) return;
      doc.open();
      doc.write(m.text);
      doc.close();
    });
  }

  setTab(tab: Tab) {
    this.activeTab.set(tab);
  }

  copyEmail(email: string | null | undefined) {
    if (!email) return;
    navigator.clipboard.writeText(email).then(() => {
      this.toastr.success('Email address copied');
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
