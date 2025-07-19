import { ChangeDetectionStrategy, Component, inject, signal } from '@angular/core';

import { LucideAngularModule } from 'lucide-angular';
import { take } from 'rxjs';
import { AuthService, User } from '../../_services/auth-service';
import { ClickOutsideDirective } from '../../_services/click-outside.directive';
import { ModalContainerService } from '../modal/modal-container.service';
import { UserSettingsComponent } from '../user-settings/user-settings.component';

@Component({
  selector: 'app-user-menu',
  standalone: true,
  imports: [LucideAngularModule, UserSettingsComponent, ClickOutsideDirective],
  template: `
    @if (user()) {
      <button type="button" class="btn btn-icon user-menu-btn" (click)="isOpen.set(!isOpen())" title="User menu">
        <lucide-icon name="user" size="20"></lucide-icon>
      </button>
      @if (isOpen()) {
        <div class="menu" appClickOutside (clickOutside)="isOpen.set(false)" [delayTime]="200">
          <div class="user-menu-info">
            <div class="avatar-spacer">
              <div class="avatar-wrap">
                <div class="avatar" style="width: 80px">
                  <lucide-icon name="user"></lucide-icon>
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
