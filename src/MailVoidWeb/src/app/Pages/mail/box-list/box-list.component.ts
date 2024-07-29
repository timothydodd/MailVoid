import { CommonModule } from '@angular/common';
import { ChangeDetectionStrategy, Component, computed, input, model, output } from '@angular/core';
import { NgbModule } from '@ng-bootstrap/ng-bootstrap';
import { LucideAngularModule } from 'lucide-angular';

@Component({
  selector: 'app-box-list',
  standalone: true,
  imports: [CommonModule, LucideAngularModule, NgbModule],
  template: `
    <ul class="list-group">
      @if (mailboxView()) {
        @for (item of mailboxView(); track item.id) {
          <li class="list-group-item">
            <button class="btn btn-link" (click)="clickBox(item.name)">{{ item.name }}</button>
            <div ngbDropdown class="borderless-menu" placement="bottom-right" hover-stop-propagation>
              <button class="btn btn-outline-primary" ngbDropdownToggle click-stop-propagation>
                <lucide-icon name="ellipsis-vertical"></lucide-icon>
              </button>
              <div ngbDropdownMenu>
                <button ngbDropdownItem click-stop-propagation (click)="deleteClick(item.name)">Delete</button>
              </div>
            </div>
          </li>
        }
      }
    </ul>
  `,
  styleUrl: './box-list.component.scss',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class BoxListComponent {
  mailboxes = input.required<string[] | null>();
  mailboxView = computed(() => {
    let i = 0;
    const mb = this.mailboxes() ?? [];

    return mb.map((x) => {
      return {
        id: i++,
        name: x,
      };
    });
  });
  selectedBox = model<string | null>();
  deleteEvent = output<string>();
  clickBox(box: string) {
    this.selectedBox.set(box);
  }
  deleteClick(item: string) {
    console.log('Deleted: ', item);
    this.deleteEvent.emit(item);
  }
}
