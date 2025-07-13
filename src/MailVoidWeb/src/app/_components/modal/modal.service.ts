import { inject, Injectable, TemplateRef } from '@angular/core';
import { Router } from '@angular/router';
import { Subject } from 'rxjs';
import { ModalContainerService, ModalRef } from './modal-container.service';

@Injectable({
  providedIn: 'root',
})
export class ModalService {
  router = inject(Router);
  modalContainerService = inject(ModalContainerService);
  modalEvent = new Subject<ModalData | null>();
  private currentModalRef?: ModalRef;

  constructor() {}

  /**
   * Opens a modal using the new modal container service
   * Returns a ModalRef for advanced control
   */
  openModal(title: string, body?: TemplateRef<any>, footer?: TemplateRef<any>, header?: TemplateRef<any>): ModalRef {
    const modalRef = this.modalContainerService.open({
      title,
      body,
      footer,
      header,
    });

    // Store reference for legacy close() method
    this.currentModalRef = modalRef;

    // Emit event for backward compatibility if needed
    this.modalEvent.next({
      title,
      bodyTemplate: body,
      footerTemplate: footer,
      headerTemplate: header,
    });

    // Clean up when modal closes
    modalRef.onClose.subscribe(() => {
      this.modalEvent.next(null);
      if (this.currentModalRef === modalRef) {
        this.currentModalRef = undefined;
      }
    });

    return modalRef;
  }

  /**
   * Legacy open method - now uses the new modal container service
   * @deprecated Use openModal() for better control
   */
  open(title: string, body?: TemplateRef<any>, footer?: TemplateRef<any>, header?: TemplateRef<any>) {
    this.openModal(title, body, footer, header);
  }

  /**
   * Closes the most recently opened modal
   * @deprecated Use the ModalRef.close() method instead
   */
  close() {
    if (this.currentModalRef) {
      this.currentModalRef.close();
    }
    this.modalEvent.next(null);
  }

  /**
   * Closes all open modals
   */
  closeAll() {
    this.modalContainerService.closeAll();
  }
}

export interface ModalData {
  title: string;
  bodyTemplate?: TemplateRef<any>;
  footerTemplate?: TemplateRef<any>;
  headerTemplate?: TemplateRef<any>;
}
