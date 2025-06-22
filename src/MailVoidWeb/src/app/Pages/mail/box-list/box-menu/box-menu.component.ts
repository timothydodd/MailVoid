import { ChangeDetectionStrategy, Component, inject, input, output, signal } from '@angular/core';
import { LucideAngularModule } from 'lucide-angular';
import { ClickOutsideDirective } from '../../../../_services/click-outside.directive';
import { MailService } from '../../../../_services/api/mail.service';

@Component({
  selector: 'app-box-menu',
  imports: [LucideAngularModule, ClickOutsideDirective],
  template: ` <button type="button" class="btn-icon" (click)="isOpen.set(!isOpen())">
      <lucide-angular name="ellipsis-vertical"></lucide-angular>
    </button>
    @if (isOpen()) {
      <div class="menu" appClickOutside (clickOutside)="close()" [delayTime]="200">
        @if (!isEmailClaimed()) {
          <button dropdown-item (click)="claimClick()">
            <lucide-angular name="inbox" size="16"></lucide-angular>
            Claim Mailbox
          </button>
        } @else {
          <button dropdown-item (click)="unclaimClick()">
            <lucide-angular name="inbox-x" size="16"></lucide-angular>
            Release Mailbox
          </button>
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
  private mailService = inject(MailService);
  
  isOpen = signal(false);
  item = input.required<string>();
  groupName = input<string>('');
  deleteEvent = output<string>();
  claimEvent = output<string>();
  unclaimEvent = output<string>();
  
  isEmailClaimed() {
    return this.groupName() === 'My Boxes';
  }
  
  deleteClick() {
    this.deleteEvent.emit(this.item());
    this.close();
  }
  
  claimClick() {
    this.claimEvent.emit(this.item());
    this.close();
  }
  
  unclaimClick() {
    this.unclaimEvent.emit(this.item());
    this.close();
  }
  
  close() {
    this.isOpen.set(false);
  }
}
