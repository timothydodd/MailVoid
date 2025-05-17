import { CommonModule } from '@angular/common';
import { ChangeDetectionStrategy, Component, inject, signal, TemplateRef } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { FormsModule } from '@angular/forms';
import { RouterModule } from '@angular/router';
import { LucideAngularModule } from 'lucide-angular';
import { UserMenuComponent } from '../../_components/user-menu/user-menu.component';
import { AuthService, User } from '../../_services/auth-service';

@Component({
  selector: 'app-main-nav',
  standalone: true,
  imports: [CommonModule, FormsModule, LucideAngularModule, RouterModule, UserMenuComponent],
  template: `
    <div class="container flex-row">
      <img src="logo-s.png" width="32" alt="Logo" class="logo" />

      @if (templateRef()) {
        <ng-container class="toolbar" *ngTemplateOutlet="templateRef()"></ng-container>
      }
      @if (user() !== null) {
        <app-user-menu></app-user-menu>
      }
    </div>
  `,
  styleUrl: './main-nav-bar.component.css',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class MainNavBarComponent {
  private authService = inject(AuthService);

  templateRef = signal<TemplateRef<any> | null>(null);
  user = signal<User | null>(null);
  constructor() {
    this.authService
      .getUser()
      .pipe(takeUntilDestroyed())
      .subscribe((x) => {
        this.user.set(x);
      });
  }
}
