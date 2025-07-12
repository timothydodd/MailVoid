import { ChangeDetectionStrategy, Component, ElementRef, inject, input, output, signal, viewChild } from '@angular/core';
import { LucideAngularModule } from 'lucide-angular';
import { MailService } from '../../../../_services/api/mail.service';
import { ClickOutsideDirective } from '../../../../_services/click-outside.directive';

@Component({
  selector: 'app-box-menu',
  imports: [LucideAngularModule, ClickOutsideDirective],
  template: ` <button type="button" class="btn-icon" #triggerButton (click)="toggleMenu()">
      <lucide-angular name="ellipsis-vertical"></lucide-angular>
    </button>
    @if (isOpen()) {
      <div 
        class="menu" 
        #menuElement
        [style.top.px]="menuPosition().top" 
        [style.left.px]="menuPosition().left"
        appClickOutside 
        (clickOutside)="close()" 
        [delayTime]="200"
      >
        @if (unreadCount() > 0) {
          <button dropdown-item (click)="markAllAsReadClick()">
            <lucide-angular name="check-circle" size="16"></lucide-angular>
            Mark all as read
          </button>
          <div class="menu-divider"></div>
        }
        <button dropdown-item (click)="deleteClick()">
          <lucide-angular name="trash-2" size="16"></lucide-angular>
          Delete
        </button>
      </div>
    }`,
  styleUrl: './box-menu.component.scss',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class BoxMenuComponent {
  isOpen = signal(false);
  item = input.required<string>();
  groupName = input<string>('');
  unreadCount = input<number>(0);
  deleteEvent = output<string>();
  markAllAsReadEvent = output<string>();
  
  triggerButton = viewChild<ElementRef>('triggerButton');
  menuElement = viewChild<ElementRef>('menuElement');
  menuPosition = signal({ top: 0, left: 0 });

  toggleMenu() {
    if (this.isOpen()) {
      this.close();
    } else {
      this.open();
    }
  }

  open() {
    this.isOpen.set(true);
    
    // Calculate position after the menu is opened
    setTimeout(() => {
      this.calculateMenuPosition();
    });
  }

  calculateMenuPosition() {
    const trigger = this.triggerButton()?.nativeElement;
    const menu = this.menuElement()?.nativeElement;
    
    if (!trigger || !menu) return;
    
    const triggerRect = trigger.getBoundingClientRect();
    const menuRect = menu.getBoundingClientRect();
    const viewportWidth = window.innerWidth;
    const viewportHeight = window.innerHeight;
    
    let top = triggerRect.bottom + 4; // 4px gap below trigger
    let left = triggerRect.right - menuRect.width; // Align right edge with trigger
    
    // Adjust if menu would go off-screen
    if (left < 8) {
      left = 8; // 8px margin from left edge
    }
    if (left + menuRect.width > viewportWidth - 8) {
      left = viewportWidth - menuRect.width - 8; // 8px margin from right edge
    }
    if (top + menuRect.height > viewportHeight - 8) {
      top = triggerRect.top - menuRect.height - 4; // Show above trigger instead
    }
    
    this.menuPosition.set({ top, left });
  }

  deleteClick() {
    this.deleteEvent.emit(this.item());
    this.close();
  }

  markAllAsReadClick() {
    this.markAllAsReadEvent.emit(this.item());
    this.close();
  }

  close() {
    this.isOpen.set(false);
  }
}
