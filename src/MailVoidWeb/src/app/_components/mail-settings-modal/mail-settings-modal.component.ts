import { ChangeDetectionStrategy, Component } from '@angular/core';
import { ModalContainerService } from '../modal/modal-container.service';
import { ModalLayoutComponent } from '../modal/modal-layout/modal-layout.component';
import { MailGroupComponent } from './mail-group/mail-group.component';

@Component({
  selector: 'app-mail-settings-modal',
  imports: [MailGroupComponent, ModalLayoutComponent],
  template: `
    <app-modal-layout>
      <div slot="header" class="settings-header">
        <h3 class="settings-title">Mail Settings</h3>
        <p class="settings-subtitle">Manage your mail groups</p>
      </div>

      <div slot="body" class="mail-settings-container">
        <!-- Content -->
        <div class="settings-content">
          <app-mail-group></app-mail-group>
        </div>
      </div>
    </app-modal-layout>
  `,
  styleUrl: './mail-settings-modal.component.scss',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class MailSettingsModalComponent {
  static show(modalContainerService: ModalContainerService) {
    return modalContainerService.openComponent(MailSettingsModalComponent);
  }
}
