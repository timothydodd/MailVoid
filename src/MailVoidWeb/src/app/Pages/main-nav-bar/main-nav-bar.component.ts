import { CommonModule } from '@angular/common';
import { ChangeDetectionStrategy, Component, inject, signal, TemplateRef } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { FormsModule } from '@angular/forms';
import { RouterModule } from '@angular/router';
import { LucideAngularModule } from 'lucide-angular';
import { UserMenuComponent } from '../../_components/user-menu/user-menu.component';
import { AuthService, User } from '../../_services/auth-service';
import { MobileMenuService } from '../../_services/mobile-menu.service';
import { ThemeService } from '../../_services/theme.service';

@Component({
  selector: 'app-main-nav',
  standalone: true,
  imports: [CommonModule, FormsModule, LucideAngularModule, RouterModule, UserMenuComponent],
  template: `
    <nav class="navbar">
      <div class="navbar-container">
        <img src="logo-s.png" width="32" alt="Logo" class="navbar-logo" />

        @if (templateRef()) {
          <div class="navbar-toolbar">
            <ng-container *ngTemplateOutlet="templateRef()"></ng-container>
          </div>
        }
        
        <div class="navbar-actions">
          @if (user() !== null) {
            <button class="btn btn-icon mobile-menu-btn" (click)="onMobileMenuClick()" title="Mailboxes">
              <lucide-icon name="inbox" size="20"></lucide-icon>
            </button>
            <button 
              class="btn btn-icon theme-toggle" 
              (click)="toggleTheme()" 
              [title]="themeService.theme() === 'dark' ? 'Switch to light theme' : 'Switch to dark theme'"
            >
              <lucide-icon 
                [name]="themeService.theme() === 'dark' ? 'sun' : 'moon'" 
                size="20"
              ></lucide-icon>
            </button>
            <app-user-menu></app-user-menu>
          }
        </div>
      </div>
    </nav>
  `,
  styleUrl: './main-nav-bar.component.css',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class MainNavBarComponent {
  private authService = inject(AuthService);
  private mobileMenuService = inject(MobileMenuService);
  themeService = inject(ThemeService);

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
  
  onMobileMenuClick() {
    this.mobileMenuService.toggleMenu();
  }

  toggleTheme() {
    this.themeService.toggleTheme();
  }
}
