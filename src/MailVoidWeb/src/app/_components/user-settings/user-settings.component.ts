import { CommonModule } from '@angular/common';
import { ChangeDetectionStrategy, Component, inject, signal, TemplateRef, viewChild } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { LucideAngularModule } from 'lucide-angular';
import { ModalService } from '../modal/modal.service';
import { ChangePasswordComponent } from './change-password/change-password.component';

@Component({
  selector: 'app-user-settings',
  standalone: true,
  imports: [CommonModule, FormsModule, LucideAngularModule, ChangePasswordComponent],
  template: ` <ng-template #modalBody>
      <h5>Change Password</h5>
      @if (changePassword()) {
        <app-change-password (saveEvent)="changePassword.set(false)"></app-change-password>
      } @else {
        <button class="btn btn-primary" (click)="changePassword.set(true)">Change Password</button>
      }
    </ng-template>
    <ng-template #modalFooter>
      <div>
        @if (errorMessage()) {
          <div class="text-danger">{{ errorMessage() }}</div>
        }
      </div>
    </ng-template>`,
  styleUrl: './user-settings.component.scss',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class UserSettingsComponent {
  modalService = inject(ModalService);
  modalFooter = viewChild<TemplateRef<any>>('modalFooter');
  modalBody = viewChild<TemplateRef<any>>('modalBody');
  errorMessage = signal('');
  changePassword = signal(false);

  show() {
    this.modalService.open('Settings', this.modalBody(), this.modalFooter());
  }
}
