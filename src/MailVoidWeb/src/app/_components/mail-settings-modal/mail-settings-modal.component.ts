import { ChangeDetectionStrategy, Component, inject, signal } from '@angular/core';
import { NgbActiveModal, NgbModal, NgbModalOptions, NgbModule } from '@ng-bootstrap/ng-bootstrap';
import { from } from 'rxjs';
import { ReasonResponse } from '../login/login.component';
import { MailGroupComponent } from './mail-group/mail-group.component';

@Component({
  selector: 'app-mail-settings-modal',
  imports: [MailGroupComponent, NgbModule],
  template: `
    <div class="modal-header">
      <h5 class="modal-title">Mail Settings</h5>
      <button type="button" class="btn-close" data-dismiss="modal" aria-label="Close" (click)="closeClick()"></button>
    </div>
    <div class="modal-body  d-flex flex-column gap20">
      <h3>Mail GroupSettings</h3>
      <app-mail-group></app-mail-group>
    </div>
    <div class="modal-footer"></div>
  `,
  styleUrl: './mail-settings-modal.component.scss',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class MailSettingsModalComponent {
  activeModal = inject(NgbActiveModal);
  errorMessage = signal('');
  changePassword = signal(false);

  closeClick() {
    this.activeModal.close();
  }
  static showModal(modalService: NgbModal) {
    const modalOption: NgbModalOptions = { backdrop: 'static', size: 'lg', centered: true };

    const modalRef = modalService.open(MailSettingsModalComponent, modalOption);

    return from(modalRef.result as Promise<ReasonResponse>);
  }
}
