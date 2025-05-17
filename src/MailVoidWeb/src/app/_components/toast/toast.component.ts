import { animate, state, style, transition, trigger } from '@angular/animations';
import { CommonModule } from '@angular/common';
import { ChangeDetectionStrategy, Component, inject } from '@angular/core';
import { ToastService } from '../../_services/toast.service';
@Component({
  selector: 'app-toast',
  imports: [CommonModule],
  template: `
    <div class="c-toast-container">
      @for (toast of toastService.toasts(); track $index) {
        <div [@fadeInOut] [ngClass]="['c-toast', toast.type]" (click)="removeToast(toast.id)">
          <div class="c-toast-content">
            <div class="toast-message">{{ toast.message }}</div>
          </div>
          <button class="close-button" (click)="removeToast(toast.id); $event.stopPropagation()">×</button>
        </div>
      }
    </div>
  `,
  styleUrl: './toast.component.scss',
  changeDetection: ChangeDetectionStrategy.OnPush,
  animations: [
    trigger('fadeInOut', [
      state(
        'void',
        style({
          opacity: 0,
          transform: 'translateX(20px)',
        })
      ),
      transition('void <=> *', animate('300ms ease-in-out')),
    ]),
  ],
})
export class ToastComponent {
  toastService = inject(ToastService);

  removeToast(id: number) {
    this.toastService.remove(id);
  }
}
