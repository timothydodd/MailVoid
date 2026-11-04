import { ChangeDetectionStrategy, Component, input, output } from '@angular/core';
import { RouterLink } from '@angular/router';
import { LucideDynamicIcon } from '@lucide/angular';
import { Mail } from '../../../_services/api/mail.service';
import { MailViewComponent } from '../../mail-detail/mail-view/mail-view.component';

/** Slide-over reader that opens a message beside the list. */
@Component({
  selector: 'app-mail-panel',
  standalone: true,
  imports: [LucideDynamicIcon, RouterLink, MailViewComponent],
  template: `
    <div class="slide-panel-overlay" (click)="closed.emit()"></div>
    <section
      class="slide-panel slide-panel-wide"
      role="dialog"
      aria-modal="true"
      aria-label="Message"
      (document:keydown.escape)="closed.emit()"
    >
      <div class="slide-panel-toolbar">
        <button type="button" class="icon-btn" (click)="closed.emit()" aria-label="Close" title="Close (Esc)">
          <svg lucideIcon="x" size="18"></svg>
        </button>
        <button
          type="button"
          class="icon-btn"
          (click)="prev.emit()"
          [disabled]="!hasPrev()"
          aria-label="Previous message"
          title="Previous"
        >
          <svg lucideIcon="chevron-up" size="18"></svg>
        </button>
        <button
          type="button"
          class="icon-btn"
          (click)="next.emit()"
          [disabled]="!hasNext()"
          aria-label="Next message"
          title="Next"
        >
          <svg lucideIcon="chevron-down" size="18"></svg>
        </button>
        @if (position()) {
          <span class="slide-panel-position">{{ position() }}</span>
        }
        <span class="spacer"></span>
        <a class="icon-btn" [routerLink]="['/mail', mailId()]" aria-label="Open full page" title="Open full page">
          <svg lucideIcon="maximize-2" size="16"></svg>
        </a>
      </div>
      <app-mail-view class="panel-view" [mailId]="mailId()" (loaded)="loaded.emit($event)" />
    </section>
  `,
  styles: `
    .panel-view {
      flex: 1;
    }
  `,
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class MailPanelComponent {
  mailId = input.required<number>();
  position = input<string>('');
  hasPrev = input(false);
  hasNext = input(false);

  closed = output<void>();
  prev = output<void>();
  next = output<void>();
  loaded = output<Mail>();
}
