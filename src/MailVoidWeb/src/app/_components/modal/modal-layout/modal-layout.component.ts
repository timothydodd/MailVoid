import { ChangeDetectionStrategy, Component, ElementRef, inject, Injector, input } from '@angular/core';
import { LucideAngularModule } from 'lucide-angular';
import { ModalContainerService } from '../modal-container.service';
import { ModalComponent } from '../modal.component';

@Component({
  selector: 'app-modal-layout',
  imports: [LucideAngularModule],
  template: `
    <div class="modal-header">
      <ng-content select="[slot=header]">
        <h4 class="modal-title">{{ title() }}</h4>
      </ng-content>

      <button class="close" aria-label="Close" (click)="close()">
        <lucide-angular name="x"></lucide-angular>
      </button>
    </div>

    <div class="modal-body">
      <ng-content select="[slot=body]"></ng-content>
    </div>

    <div class="modal-footer">
      <ng-content select="[slot=footer]"></ng-content>
    </div>
  `,
  styleUrl: './modal-layout.component.scss',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class ModalLayoutComponent {
  modalContainerService = inject(ModalContainerService);
  injector = inject(Injector);
  title = input<string | null>(null);
  elementRef = inject(ElementRef);
  modalComponent = inject(ModalComponent);
  constructor() {}
  close() {
    this.modalComponent.close();
  }
}
