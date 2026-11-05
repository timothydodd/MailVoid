import { ChangeDetectionStrategy, Component, ElementRef, HostListener, inject, signal, viewChild } from '@angular/core';
import { takeUntilDestroyed, toSignal } from '@angular/core/rxjs-interop';
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
  imports: [LucideDynamicIcon, RouterModule, UserMenuComponent],
  template: `
    <nav class="navbar" aria-label="Main">
      @if (user() !== null) {
        <button
          type="button"
          class="icon-btn mobile-menu-btn"
          (click)="onMobileMenuClick()"
          aria-label="Open sidebar"
          title="Sidebar"
        >
          <svg lucideIcon="menu" size="20"></svg>
        </button>
      }
      <a routerLink="/mail" class="brand">
        <img src="logo-s.png" width="28" height="28" alt="" />
        <span>MailVoid</span>
      </a>

      @if (user() !== null) {
        <div class="nav-links">
          <a routerLink="/mail" routerLinkActive="active" class="nav-link">
            <svg lucideIcon="mail" size="16"></svg>
            <span>Mail</span>
          </a>
          @if (authService.isAdmin() || user()?.webhooksEnabled) {
            <a routerLink="/hooks" routerLinkActive="active" class="nav-link">
              <svg lucideIcon="webhook" size="16"></svg>
              <span>Hooks</span>
            </a>
          }
        </div>
      }

      @if (showMailSearch() && user() !== null) {
        <label class="nav-search">
          <svg lucideIcon="search" size="16"></svg>
          <input
            #searchInput
            type="search"
            placeholder="Search sender, recipient or subject"
            aria-label="Search mail"
            [value]="mailSearchService.searchText()"
            (input)="onSearchInput($event)"
            (keydown.escape)="clearSearch(); searchInput.blur()"
          />
          @if (mailSearchService.searchText()) {
            <button type="button" class="icon-btn icon-btn-sm" (click)="clearSearch()" aria-label="Clear search">
              <svg lucideIcon="x" size="14"></svg>
            </button>
          } @else {
            <kbd>/</kbd>
          }
        </label>
      } @else {
        <span class="nav-spacer"></span>
      }

      @if (user() !== null) {
        <div class="nav-actions">
          <button
            type="button"
            class="icon-btn"
            (click)="toggleTheme()"
            [attr.aria-label]="themeService.theme() === 'dark' ? 'Switch to light theme' : 'Switch to dark theme'"
            [title]="themeService.theme() === 'dark' ? 'Switch to light theme' : 'Switch to dark theme'"
          >
            <svg [lucideIcon]="themeService.theme() === 'dark' ? 'sun' : 'moon'" size="18"></svg>
          </button>
          <app-user-menu></app-user-menu>
        </div>
      }
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

  private searchInput = viewChild<ElementRef<HTMLInputElement>>('searchInput');
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

  // "/" jumps to the mail search box unless the user is already typing somewhere.
  @HostListener('document:keydown', ['$event'])
  onDocumentKeydown(event: KeyboardEvent) {
    if (event.key !== '/' || event.ctrlKey || event.metaKey || event.altKey) return;
    const target = event.target as HTMLElement | null;
    if (target?.closest('input, textarea, select, [contenteditable="true"]')) return;
    const input = this.searchInput()?.nativeElement;
    if (!input) return;
    event.preventDefault();
    input.focus();
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
