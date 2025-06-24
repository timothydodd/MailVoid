import { ChangeDetectionStrategy, Component, inject, input, output, signal } from '@angular/core';
import { LucideAngularModule } from 'lucide-angular';
import { MailService } from '../../../../_services/api/mail.service';
import { ClickOutsideDirective } from '../../../../_services/click-outside.directive';

@Component({
  selector: 'app-box-menu',
  imports: [LucideAngularModule, ClickOutsideDirective],
  template: ` <button type="button" class="btn-icon" (click)="isOpen.set(!isOpen())">
      <lucide-angular name="ellipsis-vertical"></lucide-angular>
    </button>
    @if (isOpen()) {
      <div class="menu" appClickOutside (clickOutside)="close()" [delayTime]="200">
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
  deleteEvent = output<string>();

  deleteClick() {
    this.deleteEvent.emit(this.item());
    this.close();
  }

  close() {
    this.isOpen.set(false);
  }
}
