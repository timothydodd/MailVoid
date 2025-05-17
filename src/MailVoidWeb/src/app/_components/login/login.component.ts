import { CommonModule } from '@angular/common';
import { ChangeDetectionStrategy, Component, inject, signal, TemplateRef, viewChild } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { LucideAngularModule } from 'lucide-angular';
import { AuthService } from '../../_services/auth-service';
import { ModalService } from '../modal/modal.service';

@Component({
  selector: 'app-login',
  standalone: true,
  imports: [CommonModule, FormsModule, LucideAngularModule],
  template: `
    <ng-template #modalBody>
      <input type="email" class="form-control" [(ngModel)]="userName" placeholder="UserName" name="userName" required />
      <input
        type="password"
        class="form-control"
        [(ngModel)]="password"
        placeholder="Password"
        name="password"
        required
      />
    </ng-template>
    <ng-template #modalFooter>
      <div>
        @if (errorMessage()) {
          <div class="text-danger">{{ errorMessage() }}</div>
        }
      </div>
      <button class="btn btn-primary" type="submit" (click)="this.login()">Login</button>
    </ng-template>
  `,
  styleUrl: './login.component.scss',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class LoginComponent {
  authService = inject(AuthService);
  modalService = inject(ModalService);
  userName: string = '';
  password: string = '';
  errorMessage = signal('');
  modalFooter = viewChild<TemplateRef<any>>('modalFooter');
  modalBody = viewChild<TemplateRef<any>>('modalBody');
  login() {
    this.authService.login(this.userName, this.password).subscribe({
      next: () => {},
      error: (error) => {
        const message = error.status == 401 ? 'Invalid UserName or Password' : 'An error occurred';
        this.errorMessage.update(() => message);
      },
    });
  }

  show() {
    this.modalService.open('Login', this.modalBody(), this.modalFooter());
  }
}
export interface ReasonResponse {
  reasonText: string;
}
