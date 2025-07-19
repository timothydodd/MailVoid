import {
  ApplicationRef,
  ComponentRef,
  createComponent,
  EnvironmentInjector,
  inject,
  Injectable,
  Type,
} from '@angular/core';
import { Subject } from 'rxjs';
import { ModalComponent } from './modal.component';

@Injectable({
  providedIn: 'root',
})
export class ModalContainerService {
  private applicationRef = inject(ApplicationRef);
  private injector = inject(EnvironmentInjector);
  private modalInstances = new Map<string, ModalInstance>();
  private modalCounter = 0;

  openComponent<T>(component: Type<T>, config?: { data?: any }): ModalRef {
    const modalId = `modal-${++this.modalCounter}`;

    // Create modal wrapper with all nodes (they now have slot attributes)
    const modalRef = createComponent(ModalComponent, {
      environmentInjector: this.injector,
    });

    // Create modal instance
    const modalInstance: ModalInstance = {
      id: modalId,
      componentRef: modalRef,
      config: { data: config?.data },
      closeSubject: new Subject<any>(),
    };

    this.modalInstances.set(modalId, modalInstance);

    // Initialize modal with config
    modalRef.instance.modalId = modalId;

    // Override close method
    modalRef.instance.close = () => {
      this.close(modalId);
    };
    // Insert the content into the modal's ViewContainerRef
    modalRef.instance.childType.set(component);
    // Attach both components to application
    this.applicationRef.attachView(modalRef.hostView);

    // Append modal to body
    const modalDomElem = (modalRef.hostView as any).rootNodes[0] as HTMLElement;
    document.body.appendChild(modalDomElem);
    // Open modal

    // Update z-index based on stack position
    this.updateZIndexes();

    // Handle body scroll
    this.updateBodyScroll();

    return {
      id: modalId,
      close: (result?: any) => this.close(modalId, result),
      onClose: modalInstance.closeSubject.asObservable(),
    };
  }

  open(config: ModalConfig): ModalRef {
    const modalId = `modal-${++this.modalCounter}`;

    // Create component
    const componentRef = createComponent(ModalComponent, {
      environmentInjector: this.injector,
    });

    // Create modal instance
    const modalInstance: ModalInstance = {
      id: modalId,
      componentRef,
      config,
      closeSubject: new Subject<any>(),
    };

    this.modalInstances.set(modalId, modalInstance);

    // Initialize modal with config
    componentRef.instance.modalId = modalId;

    // Override close method
    componentRef.instance.close = () => {
      this.close(modalId);
    };

    // Attach to application
    this.applicationRef.attachView(componentRef.hostView);

    // Append to body
    const domElem = (componentRef.hostView as any).rootNodes[0] as HTMLElement;
    document.body.appendChild(domElem);

    // Update z-index based on stack position
    this.updateZIndexes();

    // Handle body scroll
    this.updateBodyScroll();

    return {
      id: modalId,
      close: (result?: any) => this.close(modalId, result),
      onClose: modalInstance.closeSubject.asObservable(),
    };
  }

  close(modalId: string, result?: any): void {
    const modalInstance = this.modalInstances.get(modalId);
    if (!modalInstance) return;

    // Emit close event
    modalInstance.closeSubject.next(result);
    modalInstance.closeSubject.complete();

    // Wait for animation to complete before destroying

    this.applicationRef.detachView(modalInstance.componentRef.hostView);
    modalInstance.componentRef.destroy();

    // Destroy content component if it exists
    if (modalInstance.contentRef) {
      this.applicationRef.detachView(modalInstance.contentRef.hostView);
      modalInstance.contentRef.destroy();
    }

    this.modalInstances.delete(modalId);

    // Update body scroll
    this.updateBodyScroll();

    // Update z-indexes
    this.updateZIndexes();
  }

  closeAll(): void {
    const modalIds = Array.from(this.modalInstances.keys());
    modalIds.forEach((id) => this.close(id));
  }

  private updateZIndexes(): void {
    let zIndex = 1000;
    this.modalInstances.forEach((instance) => {
      const domElem = (instance.componentRef.hostView as any).rootNodes[0] as HTMLElement;
      const wrapper = domElem.querySelector('.modal-wrapper') as HTMLElement;

      if (wrapper) {
        wrapper.style.zIndex = zIndex.toString();
      }

      zIndex += 10;
    });
  }

  private updateBodyScroll(): void {
    if (this.modalInstances.size > 0) {
      // Prevent body scroll when modals are open - wrapper will handle scrolling
      document.body.style.overflow = 'hidden';
      document.body.classList.add('modal-open');
    } else {
      // Restore body scroll when no modals are open
      document.body.style.overflow = '';
      document.body.classList.remove('modal-open');
    }
  }
}

export interface ModalConfig {
  data?: any;
}

export interface ModalRef {
  id: string;
  close: (result?: any) => void;
  onClose: ReturnType<Subject<any>['asObservable']>;
}

interface ModalInstance {
  id: string;
  componentRef: ComponentRef<ModalComponent>;
  config: ModalConfig | { title: string; data?: any };
  closeSubject: Subject<any>;
  contentRef?: ComponentRef<any>;
}
