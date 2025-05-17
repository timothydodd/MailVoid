import { ChangeDetectionStrategy, Component, inject, signal, TemplateRef, viewChild } from '@angular/core';
import { ModalService } from '../modal/modal.service';
import { MailGroupComponent } from './mail-group/mail-group.component';

@Component({
  selector: 'app-mail-settings-modal',
  imports: [MailGroupComponent],
  template: `
    <ng-template #modalBody>
      <h3>Mail GroupSettings</h3>
      <app-mail-group></app-mail-group>
    </ng-template>
  `,
  styleUrl: './mail-settings-modal.component.scss',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class MailSettingsModalComponent {
  modalService = inject(ModalService);
  modalFooter = viewChild<TemplateRef<any>>('modalFooter');
  modalBody = viewChild<TemplateRef<any>>('modalBody');
  errorMessage = signal('');
  changePassword = signal(false);

  show() {
    this.modalService.open('Mail Settings', this.modalBody());
  }
}
