import { Component, inject, TemplateRef, ViewChild } from '@angular/core';
import { CommonModule } from '@angular/common';
import { ModalService } from './modal.service';
import { ModalRef } from './modal-container.service';

@Component({
  selector: 'app-modal-example',
  standalone: true,
  imports: [CommonModule],
  template: `
    <div class="example-container">
      <h3>Modal Example - Multiple Modals</h3>
      
      <button class="btn btn-primary" (click)="openFirstModal()">Open First Modal</button>
      <button class="btn btn-secondary ms-2" (click)="openSecondModal()">Open Second Modal</button>
      <button class="btn btn-info ms-2" (click)="openNestedModals()">Open Nested Modals</button>
      
      <!-- Templates for modals -->
      <ng-template #firstModalBody>
        <p>This is the first modal. You can open another modal on top of this one!</p>
        <button class="btn btn-success" (click)="openSecondModal()">Open Second Modal</button>
      </ng-template>
      
      <ng-template #firstModalFooter>
        <button class="btn btn-secondary" (click)="closeFirstModal()">Close</button>
      </ng-template>
      
      <ng-template #secondModalBody>
        <p>This is the second modal stacked on top!</p>
        <p>Notice how both modals can be open simultaneously.</p>
      </ng-template>
      
      <ng-template #secondModalFooter>
        <button class="btn btn-secondary" (click)="closeSecondModal()">Close This Modal</button>
        <button class="btn btn-danger ms-2" (click)="closeAllModals()">Close All Modals</button>
      </ng-template>
      
      <ng-template #nestedModalBody>
        <p>This demonstrates opening multiple modals in sequence.</p>
        <p>Result from previous modal: {{ modalResult }}</p>
      </ng-template>
    </div>
  `
})
export class ModalExampleComponent {
  @ViewChild('firstModalBody', { static: true }) firstModalBody!: TemplateRef<any>;
  @ViewChild('firstModalFooter', { static: true }) firstModalFooter!: TemplateRef<any>;
  @ViewChild('secondModalBody', { static: true }) secondModalBody!: TemplateRef<any>;
  @ViewChild('secondModalFooter', { static: true }) secondModalFooter!: TemplateRef<any>;
  @ViewChild('nestedModalBody', { static: true }) nestedModalBody!: TemplateRef<any>;
  
  modalService = inject(ModalService);
  
  private firstModalRef?: ModalRef;
  private secondModalRef?: ModalRef;
  modalResult: any = null;
  
  openFirstModal() {
    this.firstModalRef = this.modalService.openModal(
      'First Modal',
      this.firstModalBody,
      this.firstModalFooter
    );
    
    this.firstModalRef.onClose.subscribe(result => {
      console.log('First modal closed with result:', result);
    });
  }
  
  openSecondModal() {
    this.secondModalRef = this.modalService.openModal(
      'Second Modal',
      this.secondModalBody,
      this.secondModalFooter
    );
    
    this.secondModalRef.onClose.subscribe(result => {
      console.log('Second modal closed with result:', result);
    });
  }
  
  openNestedModals() {
    const modal1 = this.modalService.openModal('Modal 1', this.firstModalBody, this.firstModalFooter);
    
    modal1.onClose.subscribe(result => {
      this.modalResult = result || 'No result from Modal 1';
      
      const modal2 = this.modalService.openModal('Modal 2', this.nestedModalBody);
      
      modal2.onClose.subscribe(result2 => {
        console.log('All modals closed');
      });
    });
  }
  
  closeFirstModal() {
    if (this.firstModalRef) {
      this.firstModalRef.close('Closed from button');
    }
  }
  
  closeSecondModal() {
    if (this.secondModalRef) {
      this.secondModalRef.close();
    }
  }
  
  closeAllModals() {
    this.modalService.closeAll();
  }
}