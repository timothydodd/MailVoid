
import { ChangeDetectionStrategy, Component, inject, signal, TemplateRef, viewChild } from '@angular/core';

import { LucideAngularModule } from 'lucide-angular';
import { take } from 'rxjs';
import { AuthService, User } from '../../_services/auth-service';
import { ClickOutsideDirective } from '../../_services/click-outside.directive';
import { ModalService } from '../modal/modal.service';
import { UserSettingsComponent } from '../user-settings/user-settings.component';

@Component({
  selector: 'app-user-menu',
  standalone: true,
  imports: [LucideAngularModule, UserSettingsComponent, ClickOutsideDirective],
  template: `
    @if (user()) {
      <button type="button" class="btn-icon" (click)="isOpen.set(!isOpen())">
        <lucide-angular name="square-menu"></lucide-angular>
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
          <button dropdown-item (click)="settings.show()">Settings</button>
          <div class="divider"></div>
          <button dropdown-item (click)="logOut()">LogOut</button>
        </div>
      }

      <app-user-settings #settings></app-user-settings>
    }
  `,
  styleUrl: './user-menu.component.scss',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class UserMenuComponent {
  authService = inject(AuthService);
  modalService = inject(ModalService);
  modalFooter = viewChild<TemplateRef<any>>('modalFooter');
  modalBody = viewChild<TemplateRef<any>>('modalBody');
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
}
