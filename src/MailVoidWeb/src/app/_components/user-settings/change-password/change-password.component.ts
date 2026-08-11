import { ChangeDetectionStrategy, Component, inject, output, signal } from '@angular/core';
import { form, required, validate, FormField } from '@angular/forms/signals';
import { ToastService } from '@rd-ui';
import { AuthService } from '../../../_services/auth-service';

interface ChangePasswordModel {
  oldPassword: string;
  newPassword: string;
  newPassword2: string;
}

@Component({
  selector: 'app-change-password',
  standalone: true,
  imports: [FormField],
  template: `
    <div class="flex-column gap10">
      <div class="form-group">
        <label for="op">Old Password</label>
        <input [formField]="changePasswordForm.oldPassword" type="password" class="form-control" id="op" />
        @if ((submitted() || changePasswordForm.oldPassword().touched()) && changePasswordForm.oldPassword().invalid()) {
          <div class="text-danger">
            @for (error of changePasswordForm.oldPassword().errors(); track error.kind) {
              <div>{{ error.message }}</div>
            }
          </div>
        }
      </div>
      <div class="form-group">
        <label for="np">New Password</label>
        <input [formField]="changePasswordForm.newPassword" type="password" class="form-control" id="np" />
        @if ((submitted() || changePasswordForm.newPassword().touched()) && changePasswordForm.newPassword().invalid()) {
          <div class="text-danger">
            @for (error of changePasswordForm.newPassword().errors(); track error.kind) {
              <div>{{ error.message }}</div>
            }
          </div>
        }
      </div>
      <div class="form-group">
        <label for="np2">Confirm New Password</label>
        <input [formField]="changePasswordForm.newPassword2" type="password" class="form-control" id="np2" />
        @if ((submitted() || changePasswordForm.newPassword2().touched()) && changePasswordForm.newPassword2().invalid()) {
          <div class="text-danger">
            @for (error of changePasswordForm.newPassword2().errors(); track error.kind) {
              <div>{{ error.message }}</div>
            }
          </div>
        }
      </div>
      <button class="btn btn-primary" (click)="submit()">Save</button>
      @if (errorMessage()) {
        <div class="text-danger">{{ errorMessage() }}</div>
      }
    </div>
  `,
  styleUrl: './change-password.component.scss',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class ChangePasswordComponent {
  private authService = inject(AuthService);
  private toastr = inject(ToastService);
  errorMessage = signal('');
  submitted = signal(false);
  saveEvent = output();

  model = signal<ChangePasswordModel>({ oldPassword: '', newPassword: '', newPassword2: '' });
  changePasswordForm = form(this.model, (path) => {
    required(path.oldPassword, { message: 'Old Password is required' });
    required(path.newPassword, { message: 'New Password is required' });
    required(path.newPassword2, { message: 'Confirm New Password is required' });
    validate(path.newPassword2, ({ value, valueOf }) => {
      if (value() !== valueOf(path.newPassword)) {
        return { kind: 'noMatch', message: 'Password must match' };
      }
      return null;
    });
  });

  submit() {
    if (this.changePasswordForm().invalid()) {
      this.submitted.set(true);
      this.errorMessage.set('Please correct the errors');
      return;
    }
    const value = this.model();
    this.authService
      .changePassword({
        oldPassword: value.oldPassword,
        newPassword: value.newPassword,
      })
      .subscribe({
        next: () => {
          this.toastr.success('Password changed');
          this.saveEvent.emit();
        },
        error: (error) => {
          this.toastr.error('Password changed Failed');
          const message = error.status == 401 ? 'Invalid UserName or Password' : 'An error occurred';
          this.errorMessage.update(() => message);
        },
      });
  }
}
