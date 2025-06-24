import { CommonModule } from '@angular/common';
import { ChangeDetectionStrategy, Component, inject, signal, TemplateRef, viewChild } from '@angular/core';
import { ModalService } from '../modal/modal.service';
import { MailGroupComponent } from './mail-group/mail-group.component';

@Component({
  selector: 'app-mail-settings-modal',
  imports: [CommonModule, MailGroupComponent],
  template: `
    <ng-template #modalHeader>
      <div class="settings-header">
        <h3 class="settings-title">Mail Settings</h3>
        <p class="settings-subtitle">Manage your mail groups</p>
      </div>
    </ng-template>

    <ng-template #modalBody>
      <div class="mail-settings-container">
        <!-- Content -->
        <div class="settings-content">
          <app-mail-group></app-mail-group>
        </div>
      </div>
    </ng-template>
  `,
  styleUrl: './mail-settings-modal.component.scss',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class MailSettingsModalComponent {
  modalService = inject(ModalService);
  modalFooter = viewChild<TemplateRef<any>>('modalFooter');
  modalBody = viewChild<TemplateRef<any>>('modalBody');
  modalHeader = viewChild<TemplateRef<any>>('modalHeader');

  show() {
    this.modalService.open('Mail Settings', this.modalBody(), undefined, this.modalHeader());
  }
}
