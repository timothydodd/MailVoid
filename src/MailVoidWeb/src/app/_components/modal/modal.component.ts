import { animate, state, style, transition, trigger } from '@angular/animations';
import { CommonModule } from '@angular/common';
import { ChangeDetectionStrategy, Component, inject, signal, Type } from '@angular/core';
import { LucideAngularModule } from 'lucide-angular';
import { ModalContainerService } from './modal-container.service';

@Component({
  selector: 'app-modal',
  imports: [CommonModule, LucideAngularModule],
  template: `
    <div class="backdrop" [@fadeInOut]></div>
    <div class="modal-wrapper" (click)="onBackdropClick($event)">
      <div class="modal-container">
        <div class="modal" [@fadeInOut] (click)="$event.stopPropagation()">
          <ng-container [ngComponentOutlet]="childType()"></ng-container>
        </div>
      </div>
    </div>
    <!-- Add this placeholder -->
  `,
  styleUrl: './modal.component.scss',
  changeDetection: ChangeDetectionStrategy.OnPush,
  animations: [
    trigger('fadeInOut', [
      state(
        'void',
        style({
          opacity: 0,
        })
      ),
      transition('void <=> *', animate('300ms ease-in-out')),
    ]),
  ],
})
export class ModalComponent {
  modalContainerService = inject(ModalContainerService);
  modalId?: string;

  closeOnBackdropClick = true;
  childType = signal<Type<any> | null>(null);
  constructor() {}
  close() {
    if (this.modalId) this.modalContainerService.close(this.modalId);
  }

  onBackdropClick(event: Event) {
    if (this.closeOnBackdropClick && event.target === event.currentTarget) {
      this.close();
    }
  }
}
