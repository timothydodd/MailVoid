import { ChangeDetectionStrategy, Component, inject, signal } from '@angular/core';
import { form, FormField, minLength, required, validate } from '@angular/forms/signals';
import { ModalComponent, ModalContainerService, ModalLayoutComponent } from '@rd-ui';
import { Observable } from 'rxjs';
import { map } from 'rxjs/operators';
import { MIN_PASSWORD_LENGTH } from '../../../_models/user.model';

interface ResetPasswordModel {
  newPassword: string;
  confirmPassword: string;
}

// Admin "reset password" dialog: masked password + confirmation. Closes with the new
// password (string) when saved, or undefined when cancelled. The caller performs the API call.
@Component({
  selector: 'app-reset-password-dialog',
  standalone: true,
  imports: [ModalLayoutComponent, FormField],
  template: `
    <rd-modal-layout [title]="title()">
      <form slot="body" id="reset-password-form" class="flex-column gap10" (submit)="save($event)">
        <div class="form-group">
          <label for="rp-new">New Password</label>
          <input
            id="rp-new"
            type="password"
            autocomplete="new-password"
            class="form-control"
            [formField]="resetForm.newPassword"
          />
          @if ((submitted() || resetForm.newPassword().touched()) && resetForm.newPassword().invalid()) {
            <div class="text-danger">
              @for (error of resetForm.newPassword().errors(); track error.kind) {
                <div>{{ error.message }}</div>
              }
            </div>
          }
        </div>
        <div class="form-group">
          <label for="rp-confirm">Confirm New Password</label>
          <input
            id="rp-confirm"
            type="password"
            autocomplete="new-password"
            class="form-control"
            [formField]="resetForm.confirmPassword"
          />
          @if ((submitted() || resetForm.confirmPassword().touched()) && resetForm.confirmPassword().invalid()) {
            <div class="text-danger">
              @for (error of resetForm.confirmPassword().errors(); track error.kind) {
                <div>{{ error.message }}</div>
              }
            </div>
          }
        </div>
      </form>
      <div slot="footer" class="modal-actions">
        <button type="button" class="btn btn-secondary" (click)="cancel()">Cancel</button>
        <button type="submit" form="reset-password-form" class="btn btn-primary">Reset Password</button>
      </div>
    </rd-modal-layout>
  `,
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class ResetPasswordDialogComponent {
  private modalComponent = inject(ModalComponent);

  title = signal(this.buildTitle());
  submitted = signal(false);

  model = signal<ResetPasswordModel>({ newPassword: '', confirmPassword: '' });
  resetForm = form(this.model, (path) => {
    required(path.newPassword, { message: 'New Password is required' });
    minLength(path.newPassword, MIN_PASSWORD_LENGTH, {
      message: `Password must be at least ${MIN_PASSWORD_LENGTH} characters`,
    });
    required(path.confirmPassword, { message: 'Confirm New Password is required' });
    validate(path.confirmPassword, ({ value, valueOf }) =>
      value() !== valueOf(path.newPassword) ? { kind: 'noMatch', message: 'Passwords must match' } : null
    );
  });

  // Opens the dialog; emits the new password, or undefined if cancelled/dismissed.
  static show(modalContainerService: ModalContainerService, userName: string): Observable<string | undefined> {
    return modalContainerService
      .openComponent(ResetPasswordDialogComponent, { data: { userName }, size: 'small', centered: true })
      .onClose.pipe(map((result) => (typeof result === 'string' ? result : undefined)));
  }

  save(event: Event) {
    event.preventDefault();
    if (this.resetForm().invalid()) {
      this.submitted.set(true);
      return;
    }
    this.close(this.model().newPassword);
  }

  cancel() {
    this.close(undefined);
  }

  private close(result: string | undefined) {
    this.modalComponent.modalContainerService.close(this.modalComponent.modalId!, result);
  }

  private buildTitle(): string {
    const userName = this.modalComponent.config?.data?.userName;
    return userName ? `Reset Password for ${userName}` : 'Reset Password';
  }
}
