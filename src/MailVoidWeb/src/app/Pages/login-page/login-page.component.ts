
import { Component, ChangeDetectionStrategy, signal } from '@angular/core';
import { form, required, FormField } from '@angular/forms/signals';
import { ActivatedRoute, Router } from '@angular/router';
import { AuthService } from '../../_services/auth-service';

@Component({
  selector: 'app-login-page',
  standalone: true,
  imports: [FormField],
  templateUrl: './login-page.component.html',
  changeDetection: ChangeDetectionStrategy.Eager,
  styleUrls: ['./login-page.component.css'],
})
export class LoginPageComponent {
  loginModel = signal<{ username: string; password: string }>({ username: '', password: '' });
  loginForm = form(this.loginModel, (path) => {
    required(path.username);
    required(path.password);
  });
  error = '';
  loading = false;
  returnUrl: string;

  constructor(
    private authService: AuthService,
    private router: Router,
    private route: ActivatedRoute
  ) {
    // Get return url from route parameters or default to '/'. Only accept a local
    // path (single leading slash) to prevent open-redirect to another origin.
    const requested = this.route.snapshot.queryParams['returnUrl'];
    this.returnUrl = this.isSafeReturnUrl(requested) ? requested : '/';

    // Redirect if already authenticated
    if (this.authService.isAuthenticated()) {
      this.router.navigate([this.returnUrl]);
    }
  }

  async login(event: Event) {
    event.preventDefault();
    const { username, password } = this.loginModel();
    if (!username || !password) {
      this.error = 'Please enter username and password';
      return;
    }

    this.loading = true;
    this.error = '';

    try {
      await this.authService.login(username, password).toPromise();
      this.router.navigate([this.returnUrl]);
    } catch (error: any) {
      const status = error?.status;
      if (status === 400 || status === 401) {
        this.error = 'Invalid username or password';
      } else if (status === 0) {
        this.error = 'Unable to reach the server. Please check your connection.';
      } else {
        this.error = error?.error?.message || error?.message || 'Login failed. Please try again.';
      }
      this.loading = false;
    }
  }

  private isSafeReturnUrl(url: unknown): url is string {
    return typeof url === 'string' && url.startsWith('/') && !url.startsWith('//');
  }
}
