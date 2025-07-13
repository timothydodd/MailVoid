import { animate, state, style, transition, trigger } from '@angular/animations';
import { CommonModule } from '@angular/common';
import { ChangeDetectionStrategy, Component, signal, TemplateRef } from '@angular/core';
import { LucideAngularModule } from 'lucide-angular';

@Component({
  selector: 'app-modal',
  imports: [CommonModule, LucideAngularModule],
  template: `
    @if (isOpen()) {
      <div class="modal-wrapper" (click)="onBackdropClick($event)">
        <div class="backdrop" [@fadeInOut]></div>
        <div class="modal-container">
          <div class="modal" [@fadeInOut] (click)="$event.stopPropagation()">
            <div class="modal-header">
              @if (headerTemplate()) {
                <ng-container *ngTemplateOutlet="headerTemplate()!"></ng-container>
              } @else {
                <h4 class="modal-title">{{ title() }}</h4>
              }
              
              <button class="close" aria-label="Close" (click)="close()">
                <lucide-angular name="x"></lucide-angular>
              </button>
            </div>

            <div class="modal-body">
              @if (bodyTemplate()) {
                <ng-container *ngTemplateOutlet="bodyTemplate()!"> </ng-container>
              }
            </div>
            @if (footerTemplate()) {
              <div class="modal-footer">
                <ng-container *ngTemplateOutlet="footerTemplate()!"> </ng-container>
              </div>
            }
          </div>
        </div>
      </div>
    }
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
  modalId?: string;
  footerTemplate = signal<TemplateRef<any> | null | undefined>(null);
  bodyTemplate = signal<TemplateRef<any> | null | undefined>(null);
  headerTemplate = signal<TemplateRef<any> | null | undefined>(null);
  isOpen = signal(false);
  title = signal<string | null>(null);
  closeOnBackdropClick = true;

  close() {
    // This will be overridden by the container service
  }

  open() {
    this.isOpen.set(true);
  }

  onBackdropClick(event: Event) {
    if (this.closeOnBackdropClick && event.target === event.currentTarget) {
      this.close();
    }
  }
}
