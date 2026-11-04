import { ChangeDetectionStrategy, Component, computed, inject, signal } from '@angular/core';

import { LucideDynamicIcon } from '@lucide/angular';
import { take } from 'rxjs';
import { AuthService, User } from '../../_services/auth-service';
import { ClickOutsideDirective } from '../../_services/click-outside.directive';
import { initialsFor } from '../../_services/mail-format';
import { ModalContainerService } from '@rd-ui';
import { UserSettingsComponent } from '../user-settings/user-settings.component';

@Component({
  selector: 'app-user-menu',
  standalone: true,
  imports: [LucideDynamicIcon, ClickOutsideDirective],
  template: `
    @if (user()) {
      <button
        type="button"
        class="avatar-initials user-menu-btn"
        (click)="isOpen.set(!isOpen())"
        [attr.aria-expanded]="isOpen()"
        aria-label="Account menu"
        [title]="user()?.userName ?? 'Account'"
      >
        {{ initials() }}
      </button>
      @if (isOpen()) {
        <div
          class="menu"
          appClickOutside
          (clickOutside)="isOpen.set(false)"
          (document:keydown.escape)="isOpen.set(false)"
          [delayTime]="200"
        >
          <div class="user-menu-info">
            <div class="avatar-spacer">
              <div class="avatar-wrap">
                <div class="avatar" style="width: 80px">
                  <svg lucideIcon="user"></svg>
                </div>
              </div>
            </div>
            <div class="basic-info">
              <div class="u-name">{{ user()?.userName }}</div>
            </div>
          </div>

          <div class="divider"></div>
          <button dropdown-item (click)="settingsClick()">Settings</button>
          <div class="divider"></div>
          <button dropdown-item (click)="logOut()">LogOut</button>
        </div>
      }
    }
  `,
  styleUrl: './user-menu.component.scss',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class UserMenuComponent {
  authService = inject(AuthService);
  modalContainerService = inject(ModalContainerService);
  user = signal<User | null>(null);
  isOpen = signal(false);
  initials = computed(() => initialsFor(this.user()?.userName));
  constructor() {
    this.authService
      .getUser()
      .pipe(take(1))
      .subscribe((user) => {
        this.user.update(() => user);
      });
  }

  logOut() {
    this.authService.logout();
  }
  settingsClick() {
    UserSettingsComponent.show(this.modalContainerService);
  }
}
