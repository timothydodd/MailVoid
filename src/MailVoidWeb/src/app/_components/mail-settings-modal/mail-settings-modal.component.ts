import { CommonModule } from '@angular/common';
import { ChangeDetectionStrategy, Component, inject, signal, TemplateRef, viewChild } from '@angular/core';
import { MailboxClaimComponent } from '../mailbox-claim/mailbox-claim.component';
import { ModalService } from '../modal/modal.service';
import { MailGroupComponent } from './mail-group/mail-group.component';

@Component({
  selector: 'app-mail-settings-modal',
  imports: [CommonModule, MailGroupComponent, MailboxClaimComponent],
  template: `
    <ng-template #modalHeader>
      <div class="settings-header">
        <h3 class="settings-title">Mail Settings</h3>
        <p class="settings-subtitle">Configure mail groups, routing rules, and manage your mailboxes</p>
      </div>
    </ng-template>

    <ng-template #modalBody>
      <div class="mail-settings-container">
        <!-- Sub Header with Navigation Tabs -->
        <div class="settings-subheader">
          <div class="settings-nav">
            <button class="nav-tab" [class.active]="activeTab() === 'groups'" (click)="setActiveTab('groups')">
              Mail Groups
            </button>
            <button class="nav-tab" [class.active]="activeTab() === 'mailboxes'" (click)="setActiveTab('mailboxes')">
              Manage Mailboxes
            </button>
          </div>
        </div>

        <!-- Content Sections -->
        <div class="settings-content">
          @if (activeTab() === 'groups') {
            <app-mail-group></app-mail-group>
          }
          @if (activeTab() === 'mailboxes') {
            <app-mailbox-claim></app-mailbox-claim>
          }
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
  errorMessage = signal('');
  changePassword = signal(false);
  activeTab = signal<'groups' | 'mailboxes'>('groups');

  show() {
    this.modalService.open('Mail Settings', this.modalBody(), undefined, this.modalHeader());
  }

  setActiveTab(tab: 'groups' | 'mailboxes') {
    this.activeTab.set(tab);
  }
}
