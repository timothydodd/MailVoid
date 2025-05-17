import { ChangeDetectionStrategy, Component, input, output, signal } from '@angular/core';
import { LucideAngularModule } from 'lucide-angular';
import { ClickOutsideDirective } from '../../../../_services/click-outside.directive';

@Component({
  selector: 'app-box-menu',
  imports: [LucideAngularModule, ClickOutsideDirective],
  template: ` <button type="button" class="btn-icon" (click)="isOpen.set(!isOpen())">
      <lucide-angular name="ellipsis-vertical"></lucide-angular>
    </button>
    @if (isOpen()) {
      <div class="menu" appClickOutside (clickOutside)="close()" [delayTime]="200">
        <button dropdown-item (click)="deleteClick()">Delete</button>
      </div>
    }`,
  styleUrl: './box-menu.component.scss',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class BoxMenuComponent {
  isOpen = signal(false);
  item = input.required<string>();
  deleteEvent = output<string>();
  deleteClick() {
    console.log('Deleted: ', this.item());
    this.deleteEvent.emit(this.item());
  }
  close() {
    debugger;
    this.isOpen.set(false);
  }
}
