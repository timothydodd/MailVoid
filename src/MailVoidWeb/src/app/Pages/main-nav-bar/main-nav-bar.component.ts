import { CommonModule } from '@angular/common';
import { ChangeDetectionStrategy, Component, inject, signal, TemplateRef } from '@angular/core';
import { takeUntilDestroyed, toSignal } from '@angular/core/rxjs-interop';
import { FormsModule } from '@angular/forms';
import { NavigationEnd, Router, RouterModule } from '@angular/router';
import { LucideDynamicIcon } from '@lucide/angular';
import { filter, map, startWith } from 'rxjs';
import { UserMenuComponent } from '../../_components/user-menu/user-menu.component';
import { AuthService, User } from '../../_services/auth-service';
import { MailSearchService } from '../../_services/mail-search.service';
import { MobileMenuService } from '../../_services/mobile-menu.service';
import { ThemeService } from '../../_services/theme.service';

@Component({
  selector: 'app-main-nav',
  standalone: true,
  imports: [CommonModule, FormsModule, LucideDynamicIcon, RouterModule, UserMenuComponent],
  template: `
    <nav class="navbar">
      <div class="navbar-container">
        <img src="logo-s.png" width="32" alt="Logo" class="navbar-logo" />

        @if (user() !== null) {
          <div class="navbar-nav">
            <a routerLink="/mail" routerLinkActive="active" class="nav-link">
              <svg lucideIcon="mail" size="18"></svg>
              <span>Mail</span>
            </a>
            @if (authService.isAdmin()) {
              <a routerLink="/hooks" routerLinkActive="active" class="nav-link">
                <svg lucideIcon="webhook" size="18"></svg>
                <span>Hooks</span>
              </a>
            }
          </div>
        }

        @if (templateRef()) {
          <div class="navbar-toolbar">
            <ng-container *ngTemplateOutlet="templateRef()"></ng-container>
          </div>
        }

        @if (showMailSearch() && user() !== null) {
          <div class="navbar-search">
            <svg lucideIcon="search" size="16"></svg>
            <input
              type="search"
              placeholder="Search mail..."
              [value]="mailSearchService.searchText()"
              (input)="onSearchInput($event)"
            />
            @if (mailSearchService.searchText()) {
              <button class="btn btn-icon search-clear" (click)="clearSearch()" title="Clear search">
                <svg lucideIcon="x" size="14"></svg>
              </button>
            }
          </div>
        }

        <div class="navbar-actions">
          @if (user() !== null) {
            <button class="btn btn-icon mobile-menu-btn" (click)="onMobileMenuClick()" title="Mailboxes">
              <svg lucideIcon="inbox" size="20"></svg>
            </button>
            <button 
              class="btn btn-icon theme-toggle" 
              (click)="toggleTheme()" 
              [title]="themeService.theme() === 'dark' ? 'Switch to light theme' : 'Switch to dark theme'"
            >
              <svg 
                [lucideIcon]="themeService.theme() === 'dark' ? 'sun' : 'moon'" 
                size="20"
              ></svg>
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
  authService = inject(AuthService);
  private mobileMenuService = inject(MobileMenuService);
  themeService = inject(ThemeService);
  mailSearchService = inject(MailSearchService);
  private router = inject(Router);

  templateRef = signal<TemplateRef<any> | null>(null);
  user = signal<User | null>(null);

  showMailSearch = toSignal(
    this.router.events.pipe(
      filter((e) => e instanceof NavigationEnd),
      map(() => this.isMailListRoute(this.router.url)),
      startWith(this.isMailListRoute(this.router.url))
    ),
    { initialValue: this.isMailListRoute(this.router.url) }
  );

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

  onSearchInput(event: Event) {
    this.mailSearchService.setSearch((event.target as HTMLInputElement).value);
  }

  clearSearch() {
    this.mailSearchService.clear();
  }

  private isMailListRoute(url: string): boolean {
    const path = url.split('?')[0].split('#')[0];
    return path === '/mail' || path === '/';
  }
}
