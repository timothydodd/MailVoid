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
import { LucideAngularModule } from 'lucide-angular';
import { MailGroup, MailService } from '../../../_services/api/mail.service';
import { CheckComponent } from '../../check/check.component';
@Component({
  selector: 'app-mail-group',
  imports: [ReactiveFormsModule, FormsModule, ValdemortModule, NgSelectModule, CheckComponent, LucideAngularModule],
  template: `
    <div class="mail-group-layout">
      <!-- Mail Groups List Panel -->
      <div class="groups-panel">
        <div class="panel-header">
          <h4 class="panel-title">Mail Groups</h4>
          <button class="btn btn-primary btn-sm" (click)="newForm()">
            <span>Add Group</span>
          </button>
        </div>
        <div class="groups-list">
          @if (mailGroups().length === 0) {
            <div class="empty-state">
              <p class="empty-message">No mail groups configured</p>
              <button class="btn btn-word" (click)="newForm()">Create your first group</button>
            </div>
          } @else {
            @for (item of mailGroups(); track item.id) {
              <div class="group-item" [class.active]="selectedMailGroup()?.value?.id === item.id">
                <div class="group-info">
                  <h5 class="group-name">{{ getMailNameFromPath(item.path) }}</h5>
                  <p class="group-path">{{ item.path }}</p>
                  <span class="group-badge" [class.public]="item.isPublic" [class.private]="!item.isPublic">
                    {{ item.isPublic ? 'Public' : 'Private' }}
                  </span>
                </div>
                <button class="btn btn-icon" (click)="buildForm(item)" title="Edit Group">
                  <lucide-icon name="pencil" size="16"></lucide-icon>
                </button>
              </div>
            }
          }
        </div>
      </div>

      <!-- Mail Group Editor Panel -->
      <div class="editor-panel">
        @if (selectedMailGroup(); as mg) {
          <div class="form-container" [formGroup]="mg">
            <div class="panel-header">
              <h4 class="panel-title">
                {{ mg.value.id ? 'Edit Mail Group' : 'New Mail Group' }}
              </h4>
            </div>

            <div class="form-content">
              <!-- Basic Settings Section -->
              <div class="form-section">
                <h5 class="section-title">Basic Settings</h5>

                <div class="form-group">
                  <label for="path" class="form-label required">Path</label>
                  <input
                    id="path"
                    type="text"
                    class="form-control"
                    formControlName="path"
                    placeholder="e.g., /orders, /notifications"
                  />
                  <val-errors controlName="path" label="Path"></val-errors>
                  <div class="form-text">The path pattern for grouping emails</div>
                </div>

                <div class="form-group">
                  <app-check formControlName="isPublic" label="Public Group"></app-check>
                  <div class="checkbox-description">
                    <small>Public groups are visible to all users</small>
                  </div>
                  <val-errors controlName="isPublic" label="Public setting"></val-errors>
                </div>
              </div>

              <!-- Rules Section -->
              <div class="form-section">
                <div class="section-header">
                  <h5 class="section-title">Routing Rules</h5>
                  <button type="button" class="btn btn-primary btn-sm" (click)="addRule()">Add Rule</button>
                </div>

                <div class="rules-container">
                  @if (mg.controls.rules.controls.length === 0) {
                    <div class="empty-rules">
                      <p class="empty-message">No rules defined</p>
                      <button type="button" class="btn btn-word" (click)="addRule()">Add your first rule</button>
                    </div>
                  } @else {
                    @for (rule of mg.controls.rules.controls; track $index) {
                      <div class="rule-card" [formGroup]="rule">
                        <div class="rule-header">
                          <span class="rule-number">Rule {{ $index + 1 }}</span>
                          <button
                            type="button"
                            class="btn btn-icon btn-danger"
                            (click)="mg.controls.rules.removeAt($index)"
                            title="Remove rule"
                          >
                            <lucide-icon name="trash-2" size="14"></lucide-icon>
                          </button>
                        </div>

                        <div class="rule-fields">
                          <div class="form-group">
                            <label class="form-label required">Rule Type</label>
                            <ng-select
                              class="form-select"
                              formControlName="typeId"
                              [items]="typeOptions"
                              bindLabel="name"
                              bindValue="id"
                              placeholder="Select rule type"
                            >
                            </ng-select>
                            <val-errors controlName="typeId" label="Type"></val-errors>
                          </div>

                          <div class="form-group">
                            <label class="form-label required">Pattern Value</label>
                            <input
                              type="text"
                              class="form-control"
                              formControlName="value"
                              [placeholder]="getPlaceholderForRuleType(rule.value.typeId)"
                            />
                            <val-errors controlName="value" label="Value"></val-errors>
                          </div>
                        </div>
                      </div>
                    }
                  }
                </div>
              </div>
            </div>

            <!-- Form Actions -->
            <div class="form-actions">
              <button type="button" class="btn btn-secondary" (click)="cancelForm()">Cancel</button>
              <button type="button" class="btn btn-primary" [disabled]="!mg.valid" (click)="saveForm()">
                {{ mg.value.id ? 'Update Group' : 'Create Group' }}
              </button>
            </div>
          </div>
        } @else {
          <div class="editor-empty">
            <div class="empty-editor">
              <lucide-icon name="plus-circle" size="48" class="empty-icon"></lucide-icon>
              <h5 class="empty-title">Select or Create a Mail Group</h5>
              <p class="empty-message">Choose a group from the list to edit, or create a new one to get started.</p>
            </div>
          </div>
        }
      </div>
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

  getPlaceholderForRuleType(typeId: number | null | undefined): string {
    switch (typeId) {
      case 1:
        return 'Text to search for (e.g., "order")';
      case 2:
        return 'Text to start with (e.g., "noreply@")';
      case 3:
        return 'Text to end with (e.g., ".com")';
      case 4:
        return 'Regular expression pattern (e.g., "^order-\\d+")';
      default:
        return 'Enter pattern value';
    }
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
