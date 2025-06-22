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
import { CheckComponent } from '../../check/check.component';
@Component({
  selector: 'app-mail-group',
  imports: [ReactiveFormsModule, FormsModule, ValdemortModule, NgSelectModule, CheckComponent],
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
                  <svg width="16" height="16" fill="currentColor" viewBox="0 0 16 16">
                    <path
                      d="M12.146.146a.5.5 0 0 1 .708 0l3 3a.5.5 0 0 1 0 .708L10.5 9.207l-3-3L12.146.146zM11.207 10.5l-3-3L2.5 13.207 2.293 13H2a.5.5 0 0 1-.5-.5v-.293l-.207-.207L7 6.293l3 3-1.793 1.207z"
                    />
                  </svg>
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
                            <svg width="14" height="14" fill="currentColor" viewBox="0 0 16 16">
                              <path
                                d="M5.5 5.5A.5.5 0 0 1 6 6v6a.5.5 0 0 1-1 0V6a.5.5 0 0 1 .5-.5Zm2.5 0a.5.5 0 0 1 .5.5v6a.5.5 0 0 1-1 0V6a.5.5 0 0 1 .5-.5Zm3 .5a.5.5 0 0 0-1 0v6a.5.5 0 0 0 1 0V6Z"
                              />
                              <path
                                d="M14.5 3a1 1 0 0 1-1 1H13v9a2 2 0 0 1-2 2H5a2 2 0 0 1-2-2V4h-.5a1 1 0 0 1-1-1V2a1 1 0 0 1 1-1H6a1 1 0 0 1 1-1h2a1 1 0 0 1 1 1h3.5a1 1 0 0 1 1 1v1ZM4.118 4 4 4.059V13a1 1 0 0 0 1 1h6a1 1 0 0 0 1-1V4.059L11.882 4H4.118ZM2.5 3h11V2h-11v1Z"
                              />
                            </svg>
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
              <svg width="48" height="48" fill="currentColor" viewBox="0 0 16 16" class="empty-icon">
                <path d="M8 15A7 7 0 1 1 8 1a7 7 0 0 1 0 14zm0 1A8 8 0 1 0 8 0a8 8 0 0 0 0 16z" />
                <path
                  d="M8 4a.5.5 0 0 1 .5.5v3h3a.5.5 0 0 1 0 1h-3v3a.5.5 0 0 1-1 0v-3h-3a.5.5 0 0 1 0-1h3v-3A.5.5 0 0 1 8 4z"
                />
              </svg>
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
