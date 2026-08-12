import { ChangeDetectionStrategy, Component, DestroyRef, inject, signal } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { Router } from '@angular/router';
import { LucideDynamicIcon } from '@lucide/angular';
import { SignalRService } from '../../services/signalr.service';

interface MailToast {
  id: number;
  mailId: number;
  subject: string;
  from: string;
}

@Component({
  selector: 'app-mail-toast',
  standalone: true,
  imports: [LucideDynamicIcon],
  template: `
    <div class="mail-toast-container">
      @for (t of toasts(); track t.id) {
        <div
          class="mail-toast"
          role="button"
          tabindex="0"
          (click)="open(t)"
          (keydown.enter)="open(t)"
          (keydown.space)="open(t); $event.preventDefault()"
        >
          <svg lucideIcon="mail" size="18" class="mail-toast-icon"></svg>
          <div class="mail-toast-body">
            <div class="mail-toast-label">New email</div>
            <div class="mail-toast-title">{{ t.subject || '(No Subject)' }}</div>
            <div class="mail-toast-from">{{ t.from }}</div>
          </div>
          <button
            class="mail-toast-close"
            (click)="dismiss(t.id); $event.stopPropagation()"
            title="Dismiss"
            aria-label="Dismiss"
          >
            <svg lucideIcon="x" size="14"></svg>
          </button>
        </div>
      }
    </div>
  `,
  styleUrl: './mail-toast.component.scss',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class MailToastComponent {
  private router = inject(Router);
  private signalR = inject(SignalRService);
  private destroyRef = inject(DestroyRef);

  toasts = signal<MailToast[]>([]);
  private seq = 0;
  private timers = new Map<number, ReturnType<typeof setTimeout>>();

  constructor() {
    this.signalR.newMail$.pipe(takeUntilDestroyed(this.destroyRef)).subscribe((mail) => {
      const id = ++this.seq;
      const toast: MailToast = { id, mailId: mail.id, subject: mail.subject, from: mail.from };
      // Newest on top; keep at most 4 on screen.
      this.toasts.update((list) => [toast, ...list].slice(0, 4));
      this.timers.set(
        id,
        setTimeout(() => this.dismiss(id), 6000)
      );
    });
  }

  open(t: MailToast) {
    this.dismiss(t.id);
    this.router.navigate(['/mail', t.mailId]);
  }

  dismiss(id: number) {
    const timer = this.timers.get(id);
    if (timer) {
      clearTimeout(timer);
      this.timers.delete(id);
    }
    this.toasts.update((list) => list.filter((t) => t.id !== id));
  }
}
