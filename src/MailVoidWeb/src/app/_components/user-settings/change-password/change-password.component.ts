import { ChangeDetectionStrategy, Component, inject, output, signal } from '@angular/core';
import { form, minLength, required, validate, FormField } from '@angular/forms/signals';
import { ToastService } from '@rd-ui';
import { MIN_PASSWORD_LENGTH } from '../../../_models/user.model';
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
    <form class="settings-subform" (submit)="submit($event)">
      <div class="form-group">
        <label for="op">Current password</label>
        <input
          [formField]="changePasswordForm.oldPassword"
          type="password"
          class="form-control"
          id="op"
          autocomplete="current-password"
        />
        @if (
          (submitted() || changePasswordForm.oldPassword().touched()) && changePasswordForm.oldPassword().invalid()
        ) {
          <div class="text-danger">
            @for (error of changePasswordForm.oldPassword().errors(); track error.kind) {
              <div>{{ error.message }}</div>
            }
          </div>
        }
      </div>
      <div class="form-group">
        <label for="np">New password</label>
        <input
          [formField]="changePasswordForm.newPassword"
          type="password"
          class="form-control"
          id="np"
          autocomplete="new-password"
        />
        @if (
          (submitted() || changePasswordForm.newPassword().touched()) && changePasswordForm.newPassword().invalid()
        ) {
          <div class="text-danger">
            @for (error of changePasswordForm.newPassword().errors(); track error.kind) {
              <div>{{ error.message }}</div>
            }
          </div>
        }
      </div>
      <div class="form-group">
        <label for="np2">Confirm new password</label>
        <input
          [formField]="changePasswordForm.newPassword2"
          type="password"
          class="form-control"
          id="np2"
          autocomplete="new-password"
        />
        @if (
          (submitted() || changePasswordForm.newPassword2().touched()) && changePasswordForm.newPassword2().invalid()
        ) {
          <div class="text-danger">
            @for (error of changePasswordForm.newPassword2().errors(); track error.kind) {
              <div>{{ error.message }}</div>
            }
          </div>
        }
      </div>
      @if (errorMessage()) {
        <div class="message message-error">{{ errorMessage() }}</div>
      }
      <div class="form-actions">
        <button type="button" class="btn" (click)="cancelEvent.emit()">Cancel</button>
        <button type="submit" class="btn btn-primary">Save password</button>
      </div>
    </form>
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
  cancelEvent = output();

  model = signal<ChangePasswordModel>({ oldPassword: '', newPassword: '', newPassword2: '' });
  changePasswordForm = form(this.model, (path) => {
    required(path.oldPassword, { message: 'Old Password is required' });
    required(path.newPassword, { message: 'New Password is required' });
    minLength(path.newPassword, MIN_PASSWORD_LENGTH, {
      message: `Password must be at least ${MIN_PASSWORD_LENGTH} characters`,
    });
    required(path.newPassword2, { message: 'Confirm New Password is required' });
    validate(path.newPassword2, ({ value, valueOf }) => {
      if (value() !== valueOf(path.newPassword)) {
        return { kind: 'noMatch', message: 'Password must match' };
      }
      return null;
    });
  });

  submit(event: Event) {
    event.preventDefault();
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
          this.toastr.error('Password change failed');
          const message =
            error.status == 401
              ? 'Current password is incorrect'
              : error.status == 400 && typeof error.error === 'string' && error.error
                ? error.error // server-side password rule, e.g. minimum length
                : 'An error occurred';
          this.errorMessage.update(() => message);
        },
      });
  }
}
