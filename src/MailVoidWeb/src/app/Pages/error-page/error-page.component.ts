import { CommonModule } from '@angular/common';
import { Component, inject, OnInit } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { DomSanitizer } from '@angular/platform-browser';
import { ActivatedRoute, Router } from '@angular/router';
import { LucideAngularModule } from 'lucide-angular';
import { take } from 'rxjs';
import { HealthCheckService } from '../../_services/api/health-check.service';
import { AuthService } from '../../_services/auth-service';

@Component({
  selector: 'app-error-page',
  templateUrl: './error-page.component.html',
  styleUrls: ['./error-page.component.scss'],
  standalone: true,
  imports: [CommonModule, LucideAngularModule],
})
export class ErrorPageComponent implements OnInit {
  public sanitizer = inject(DomSanitizer);
  private healthCheckService = inject(HealthCheckService);
  private authService = inject(AuthService);
  private activeRoute = inject(ActivatedRoute);
  private router = inject(Router);
  showGoHome: boolean = false;
  showLogin: boolean = true;
  errorCode: string = '';
  showHealthCheck = true;
  healthData: HealthCheck | undefined;
  get errorCodes() {
    return ErrorCodes;
  }
  get isHealthy() {
    return this.healthData?.status === 'Healthy';
  }
  constructor() {
    this.activeRoute.params.pipe(takeUntilDestroyed()).subscribe((z) => {
      this.errorCode = z['errorCode'];
    });
  }
  ngOnInit(): void {
    this.healthCheckService
      .getHealth()
      .pipe(take(1))
      .subscribe({
        next: (z) => {
          if (!z) z = { status: 'Unhealthy' };
          this.healthData = z;
        },
        error: () => {
          const z = { status: 'Unhealthy' };
          this.healthData = z;
        },
      });
  }
  btnRelogin() {
    // this.authService.logout();
  }
}

export class ErrorCodes {
  public static UserLogin = 'user-login';
  public static UserBlocked = 'user-blocked';
  public static UserAccess = 'user-access';
  public static Unknown = '500';
  public static Timeout = '408';
}
export class HealthCheck {
  status: string | undefined;
}
