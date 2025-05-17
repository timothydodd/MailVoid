import { ChangeDetectionStrategy, Component, inject, signal } from '@angular/core';
import {
    AbstractControl,
    FormArray,
    FormControl,
    FormGroup,
    FormsModule,
    ReactiveFormsModule,
    ValidationErrors,
    ValidatorFn,
    Validators,
} from '@angular/forms';
import { NgSelectModule } from '@ng-select/ng-select';
import { ValdemortModule } from 'ngx-valdemort';
import { MailGroup, MailService } from '../../../_services/api/mail.service';
import { InputSwitchComponent } from '../../input-switch/input-switch.component';
@Component({
  selector: 'app-mail-group',
  imports: [ReactiveFormsModule, FormsModule, ValdemortModule, InputSwitchComponent, NgSelectModule],
  template: `
    <div class="flex-column gap20 flex-grow">
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
    <div class="flex-column gap20 flex-grow">
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
            <div class="control-title required-star">Is Public</div>
            <div class="control-value">
              <app-switch formControlName="isPublic" label="Is Public"></app-switch>
              <val-errors controlName="isPublic" label="Is Public"></val-errors>
            </div>
          </div>
          <div class="divider"></div>
          <h3>Rules</h3>
          <button class="btn btn-primary" (click)="addRule()">Add Rule</button>
          <div class="flex-column gap10">
            <div class="rule-list">
              @for (rule of mg.controls.rules.controls; track $index) {
                <div [formGroup]="rule">
                  <div class="control-row">
                    <div class="control-title required-star">Value</div>
                    <div class="control-value">
                      <input type="text" class="form-control" formControlName="value" />
                      <val-errors controlName="value" label="Value"></val-errors>
                    </div>
                  </div>
                  <div class="control-row">
                    <div class="control-title required-star">Type</div>
                    <div class="control-value">
                      <ng-select
                        class="form-control"
                        formControlName="typeId"
                        [items]="typeOptions"
                        bindLabel="name"
                        bindValue="id"
                        >]>
                      </ng-select>
                      <val-errors controlName="typeId" label="Type"></val-errors>
                    </div>
                  </div>
                  <div class="control-row">
                    <button class="btn btn-danger" (click)="mg.controls.rules.removeAt($index)">Remove</button>
                  </div>
                </div>
              }
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
  typeOptions = [
    { id: 1, name: 'Contains' },
    { id: 2, name: 'Starts With' },
    { id: 3, name: 'Ends With' },
    { id: 4, name: 'Regex' },
  ];
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
  addRule() {
    const form = this.selectedMailGroup()?.controls.rules;
    if (!form) return;
    form.push(
      new FormGroup<MailRuleForm>({
        value: new FormControl(null, [Validators.required]),
        typeId: new FormControl(null, [Validators.required]),
      })
    );
  }
  buildForm(group: MailGroup) {
    let rules = [];
    if (group.rules) {
      try {
        rules = JSON.parse(group.rules);
      } catch {
        rules = [];
      }
    }
    const ruleGroup: FormGroup<MailRuleForm>[] = [];
    for (const rule of rules) {
      ruleGroup.push(
        new FormGroup<MailRuleForm>({
          value: new FormControl(rule.value, [Validators.required]),
          typeId: new FormControl<number>(rule.typeId, [Validators.required]),
        })
      );
    }
    const form = new FormGroup<MailGroupForm>({
      id: new FormControl(group.id),
      path: new FormControl(group.path, [Validators.required]),
      rules: new FormArray(ruleGroup),
      isPublic: new FormControl(group.isPublic, [Validators.required]),
    });
    this.selectedMailGroup.set(form);
  }
  saveForm() {
    if (this.selectedMailGroup()?.valid) {
      const form = this.selectedMailGroup()?.value;
      if (!form) return;
      const mailGroup: MailGroup = {
        id: form.id ?? 0,
        path: form.path ?? '',
        rules: JSON.stringify(form.rules ?? []),
        isPublic: form.isPublic ?? false,
        ownerUserId: '',
      };
      if (!form) return;
      this.mailService.saveMailGroup(mailGroup).subscribe((group) => {
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
  rules: FormArray<FormGroup<MailRuleForm>>;
  isPublic: FormControl<boolean | null>;
}
export interface MailRuleForm {
  value: FormControl<string | null>;
  typeId: FormControl<number | null>;
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
