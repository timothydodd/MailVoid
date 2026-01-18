import { ChangeDetectionStrategy, Component, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { LucideAngularModule } from 'lucide-angular';
import { AuthService } from '../../_services/auth-service';
import { ModalContainerService, ModalLayoutComponent } from '@rd-ui';
import { UserManagementComponent } from '../user-management/user-management.component';
import { ChangePasswordComponent } from './change-password/change-password.component';

@Component({
  selector: 'app-user-settings',
  standalone: true,
  imports: [FormsModule, LucideAngularModule, ChangePasswordComponent, UserManagementComponent, ModalLayoutComponent],
  template: ` <rd-modal-layout>
    <div slot="header" class="settings-header">
      <h3 class="settings-title">User Settings</h3>
      <p class="settings-subtitle">Manage your account and system settings</p>
    </div>

    <div slot="body" class="user-settings-container">
      <!-- Sub Header with Navigation Tabs -->
      <div class="settings-subheader">
        <div class="settings-nav">
          <button class="nav-tab" [class.active]="activeTab() === 'account'" (click)="setActiveTab('account')">
            Account
          </button>
          @if (isAdmin()) {
            <button class="nav-tab" [class.active]="activeTab() === 'users'" (click)="setActiveTab('users')">
              Manage Users
            </button>
          }
        </div>
      </div>

      <!-- Content Sections -->
      <div class="settings-content">
        @if (activeTab() === 'account') {
          <div class="account-section">
            <h4>Change Password</h4>
            @if (changePassword()) {
              <app-change-password (saveEvent)="changePassword.set(false)"></app-change-password>
            } @else {
              <button class="btn btn-primary" (click)="changePassword.set(true)">Change Password</button>
            }
          </div>
        }
        @if (activeTab() === 'users' && isAdmin()) {
          <app-user-management></app-user-management>
        }
      </div>
    </div>

    <div slot="footer">
      @if (errorMessage()) {
        <div class="text-danger">{{ errorMessage() }}</div>
      }
    </div>
  </rd-modal-layout>`,
  styleUrl: './user-settings.component.scss',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class UserSettingsComponent {
  authService = inject(AuthService);
  errorMessage = signal('');
  changePassword = signal(false);
  activeTab = signal<'account' | 'users'>('account');

  static show(modalContainerService: ModalContainerService) {
    return modalContainerService.openComponent(UserSettingsComponent);
  }

  setActiveTab(tab: 'account' | 'users') {
    this.activeTab.set(tab);
  }

  isAdmin(): boolean {
    return this.authService.isAdmin();
  }
}
