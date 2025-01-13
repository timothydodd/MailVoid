import { ChangeDetectionStrategy, Component, inject, signal } from '@angular/core';
import {
  AbstractControl,
  FormControl,
  FormGroup,
  FormsModule,
  ReactiveFormsModule,
  ValidationErrors,
  ValidatorFn,
  Validators,
} from '@angular/forms';
import { ValdemortModule } from 'ngx-valdemort';
import { MailGroup, MailService } from '../../../_services/api/mail.service';
import { InputSwitchComponent } from '../../input-switch/input-switch.component';

@Component({
  selector: 'app-mail-group',
  imports: [ReactiveFormsModule, FormsModule, ValdemortModule, InputSwitchComponent],
  template: `
    <div class="flex-column gap20 flex-grow-1">
      <div class="toolbar">
        <button class="btn btn-primary" (click)="newForm()">Add</button>
      </div>
      <div class="mail-group-list">
        @for (item of mailGroups(); track $index) {
          <div class="item">
            {{ getMailNameFromPath(item.path) }} <button class="btn-link" (click)="buildForm(item)">Edit</button>
          </div>
        }
      </div>
    </div>
    <div class="flex-column gap20 flex-grow-1">
      @if (selectedMailGroup(); as mg) {
        <div [formGroup]="mg">
          <div class="control-row">
            <div class="control-title required-star">Path</div>
            <div class="control-value">
              <input type="text" class="form-control" formControlName="path" />
              <val-errors controlName="path" label="Path"></val-errors>
            </div>
          </div>
          <div class="control-row">
            <div class="control-title required-star">Rules RegEx</div>
            <div class="control-value">
              <input type="text" class="form-control" formControlName="rules" />
              <val-errors controlName="rules" label="Rules"></val-errors>
            </div>
          </div>
          <div class="control-row">
            <div class="control-title required-star">Is Public</div>
            <div class="control-value">
              <app-switch formControlName="isPublic" label="Is Public"></app-switch>
              <val-errors controlName="isPublic" label="Is Public"></val-errors>
            </div>
          </div>
          <div class="flex-row gap20 space-between">
            <button class="btn btn-primary" (click)="saveForm()">Save</button>
            <button class="btn btn-secondary" (click)="cancelForm()">Cancel</button>
          </div>
        </div>
      }
    </div>
  `,
  styleUrl: './mail-group.component.scss',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class MailGroupComponent {
  mailService = inject(MailService);
  mailGroups = signal<MailGroup[]>([]);
  selectedMailGroup = signal<FormGroup<MailGroupForm> | null>(null);

  constructor() {
    this.mailService.getMailGroups().subscribe((groups) => {
      this.mailGroups.set(groups);
    });
  }
  getMailNameFromPath(path: string) {
    const x = path.split('/');
    return x[x.length - 1];
  }
  newForm() {
    this.buildForm({ path: '', rules: null, isPublic: true } as MailGroup);
  }
  buildForm(group: MailGroup) {
    const form = new FormGroup<MailGroupForm>({
      id: new FormControl(group.id),
      path: new FormControl(group.path, [Validators.required]),
      rules: new FormControl(group.rules, [Validators.required, validCSharpRegexValidator()]),
      isPublic: new FormControl(group.isPublic, [Validators.required]),
    });
    this.selectedMailGroup.set(form);
  }
  saveForm() {
    if (this.selectedMailGroup()?.valid) {
      const form = this.selectedMailGroup()?.value as MailGroup;
      if (!form) return;
      this.mailService.saveMailGroup(form).subscribe((group) => {
        this.mailGroups.set([...this.mailGroups(), group]);
        this.selectedMailGroup.set(null);
      });
    }
  }
  cancelForm() {
    this.selectedMailGroup.set(null);
  }
}

export interface MailGroupForm {
  id: FormControl<number | null>;
  path: FormControl<string | null>;
  rules: FormControl<string | null>;
  isPublic: FormControl<boolean | null>;
}
export function validCSharpRegexValidator(): ValidatorFn {
  return (control: AbstractControl): ValidationErrors | null => {
    const pattern = control.value;

    if (!pattern) {
      // If no value, consider it valid (use required validator if needed)
      return null;
    }

    try {
      // Attempt to compile the regex
      new RegExp(pattern);
      return null; // Valid regex
    } catch (e) {
      return { invalidRegex: e }; // Invalid regex
    }
  };
}
