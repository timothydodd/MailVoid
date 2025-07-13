import { ApplicationRef, ComponentRef, createComponent, EnvironmentInjector, inject, Injectable, TemplateRef, Type } from '@angular/core';
import { ModalComponent } from './modal.component';
import { Subject } from 'rxjs';

@Injectable({
  providedIn: 'root'
})
export class ModalContainerService {
  private applicationRef = inject(ApplicationRef);
  private injector = inject(EnvironmentInjector);
  private modalInstances = new Map<string, ModalInstance>();
  private modalCounter = 0;

  open(config: ModalConfig): ModalRef {
    const modalId = `modal-${++this.modalCounter}`;
    
    // Create component
    const componentRef = createComponent(ModalComponent, {
      environmentInjector: this.injector
    });

    // Create modal instance
    const modalInstance: ModalInstance = {
      id: modalId,
      componentRef,
      config,
      closeSubject: new Subject<any>()
    };

    this.modalInstances.set(modalId, modalInstance);

    // Initialize modal with config
    componentRef.instance.modalId = modalId;
    componentRef.instance.title.set(config.title);
    componentRef.instance.bodyTemplate.set(config.body || null);
    componentRef.instance.footerTemplate.set(config.footer || null);
    componentRef.instance.headerTemplate.set(config.header || null);
    
    // Override close method
    componentRef.instance.close = () => {
      this.close(modalId);
    };

    // Attach to application
    this.applicationRef.attachView(componentRef.hostView);
    
    // Append to body
    const domElem = (componentRef.hostView as any).rootNodes[0] as HTMLElement;
    document.body.appendChild(domElem);

    // Open modal
    componentRef.instance.open();

    // Update z-index based on stack position
    this.updateZIndexes();

    // Handle body scroll
    this.updateBodyScroll();

    return {
      id: modalId,
      close: (result?: any) => this.close(modalId, result),
      onClose: modalInstance.closeSubject.asObservable()
    };
  }

  close(modalId: string, result?: any): void {
    const modalInstance = this.modalInstances.get(modalId);
    if (!modalInstance) return;

    // Emit close event
    modalInstance.closeSubject.next(result);
    modalInstance.closeSubject.complete();

    // Close and destroy component
    modalInstance.componentRef.instance.isOpen.set(false);
    
    // Wait for animation to complete before destroying
    setTimeout(() => {
      this.applicationRef.detachView(modalInstance.componentRef.hostView);
      modalInstance.componentRef.destroy();
      this.modalInstances.delete(modalId);
      
      // Update body scroll
      this.updateBodyScroll();
      
      // Update z-indexes
      this.updateZIndexes();
    }, 300); // Match animation duration
  }

  closeAll(): void {
    const modalIds = Array.from(this.modalInstances.keys());
    modalIds.forEach(id => this.close(id));
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
  title: string;
  body?: TemplateRef<any>;
  footer?: TemplateRef<any>;
  header?: TemplateRef<any>;
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
  config: ModalConfig;
  closeSubject: Subject<any>;
}