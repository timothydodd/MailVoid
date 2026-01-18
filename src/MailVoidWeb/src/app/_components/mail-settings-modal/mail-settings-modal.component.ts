import { ChangeDetectionStrategy, Component } from '@angular/core';
import { ModalContainerService, ModalLayoutComponent } from '@rd-ui';
import { MailGroupComponent } from './mail-group/mail-group.component';

@Component({
  selector: 'app-mail-settings-modal',
  imports: [MailGroupComponent, ModalLayoutComponent],
  template: `
    <rd-modal-layout>
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
    </rd-modal-layout>
  `,
  styleUrl: './mail-settings-modal.component.scss',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class MailSettingsModalComponent {
  static show(modalContainerService: ModalContainerService) {
    return modalContainerService.openComponent(MailSettingsModalComponent);
  }
}
