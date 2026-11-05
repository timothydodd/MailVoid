import { ChangeDetectionStrategy, Component } from '@angular/core';
import { ModalContainerService, ModalLayoutComponent } from '@rd-ui';
import { MailGroupComponent } from './mail-group/mail-group.component';

@Component({
  selector: 'app-mail-settings-modal',
  imports: [MailGroupComponent, ModalLayoutComponent],
  template: `
    <rd-modal-layout>
      <div slot="header">
        <h3 class="settings-title">Mail settings</h3>
        <p class="settings-subtitle">Mail groups, retention and sharing</p>
      </div>
      <div slot="body"><app-mail-group /></div>
    </rd-modal-layout>
  `,
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class MailSettingsModalComponent {
  static show(modalContainerService: ModalContainerService) {
    return modalContainerService.openComponent(MailSettingsModalComponent);
  }
}
