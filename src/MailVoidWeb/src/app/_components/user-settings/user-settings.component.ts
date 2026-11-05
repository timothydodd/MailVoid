import { ChangeDetectionStrategy, Component, inject, signal } from '@angular/core';
import { AuthService } from '../../_services/auth-service';
import { ModalContainerService, ModalLayoutComponent } from '@rd-ui';
import { UserManagementComponent } from '../user-management/user-management.component';
import { ApiKeysComponent } from './api-keys/api-keys.component';
import { ChangePasswordComponent } from './change-password/change-password.component';

@Component({
  selector: 'app-user-settings',
  standalone: true,
  imports: [ChangePasswordComponent, ApiKeysComponent, UserManagementComponent, ModalLayoutComponent],
  template: ` <rd-modal-layout>
    <div slot="header">
      <h3 class="settings-title">Settings</h3>
      <p class="settings-subtitle">Your account, API keys and users</p>
    </div>

    <div slot="body" class="settings-body">
      @if (isAdmin()) {
        <div class="tab-bar" role="tablist">
          <button
            type="button"
            role="tab"
            class="tab-bar-item"
            [class.active]="activeTab() === 'account'"
            [attr.aria-selected]="activeTab() === 'account'"
            (click)="activeTab.set('account')"
          >
            Account
          </button>
          <button
            type="button"
            role="tab"
            class="tab-bar-item"
            [class.active]="activeTab() === 'users'"
            [attr.aria-selected]="activeTab() === 'users'"
            (click)="activeTab.set('users')"
          >
            Users
          </button>
        </div>
      }

      @if (activeTab() === 'account') {
        <section class="settings-section">
          <div class="settings-section-head">
            <div>
              <h4>Password</h4>
              <p>Change the password you sign in with.</p>
            </div>
            @if (!changePassword()) {
              <button type="button" class="btn btn-sm" (click)="changePassword.set(true)">Change password</button>
            }
          </div>
          @if (changePassword()) {
            <app-change-password (saveEvent)="changePassword.set(false)" (cancelEvent)="changePassword.set(false)" />
          }
        </section>
        <app-api-keys class="settings-section" />
      }
      @if (activeTab() === 'users' && isAdmin()) {
        <app-user-management />
      }
    </div>
  </rd-modal-layout>`,
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class UserSettingsComponent {
  authService = inject(AuthService);
  changePassword = signal(false);
  activeTab = signal<'account' | 'users'>('account');

  static show(modalContainerService: ModalContainerService) {
    return modalContainerService.openComponent(UserSettingsComponent);
  }

  isAdmin(): boolean {
    return this.authService.isAdmin();
  }
}
