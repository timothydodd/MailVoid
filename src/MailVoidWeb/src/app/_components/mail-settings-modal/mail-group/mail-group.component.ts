import { ChangeDetectionStrategy, Component, inject, signal } from '@angular/core';
import { FormControl, FormGroup, FormsModule, ReactiveFormsModule, Validators } from '@angular/forms';
import { SelectComponent } from '@rd-ui';
import { LucideAngularModule } from 'lucide-angular';
import { ValdemortModule } from 'ngx-valdemort';
import { ConfirmDialogService } from '@rd-ui';
import {
  AdminMailGroup,
  CreateMailGroupRequest,
  MailGroup,
  MailGroupUser,
  MailService,
  User,
} from '../../../_services/api/mail.service';
import { AuthService } from '../../../_services/auth-service';
@Component({
  selector: 'app-mail-group',
  imports: [LucideAngularModule, ReactiveFormsModule, FormsModule, SelectComponent, ValdemortModule],
  template: `
    <div class="mail-group-layout">
      <!-- Header with create button -->
      <div class="section-header">
        <div class="header-left">
          <h4 class="section-title">Mail Groups</h4>
          <span class="groups-count">{{ mailGroups().length }} groups</span>
        </div>
        @if (!creatingGroup()) {
          <button class="btn btn-primary btn-sm" (click)="showCreateForm()">
            <lucide-icon name="plus" size="14"></lucide-icon>
            Create Group
          </button>
        }
      </div>

      <!-- Create Group Inline Form -->
      @if (creatingGroup() && createForm()) {
        <div class="inline-form">
          <div class="inline-form-header">
            <h5>Create New Mail Group</h5>
            <button class="btn btn-icon" (click)="cancelCreate()" title="Close">
              <lucide-icon name="x" size="16"></lucide-icon>
            </button>
          </div>
          <div class="inline-form-body" [formGroup]="createForm()!">
            <div class="form-row">
              <div class="form-group">
                <label for="subdomain" class="form-label">Subdomain</label>
                <input
                  id="subdomain"
                  type="text"
                  class="form-control"
                  formControlName="subdomain"
                  placeholder="e.g., support, sales"
                />
                <val-errors controlName="subdomain" label="Subdomain"></val-errors>
              </div>
              <div class="form-group">
                <label for="description" class="form-label">Description</label>
                <input
                  id="description"
                  type="text"
                  class="form-control"
                  formControlName="description"
                  placeholder="Optional description"
                />
              </div>
              <div class="form-group form-actions-inline">
                <button type="button" class="btn btn-secondary btn-sm" (click)="cancelCreate()">Cancel</button>
                <button
                  type="button"
                  class="btn btn-primary btn-sm"
                  [disabled]="!createForm()?.valid || isLoading()"
                  (click)="createGroup()"
                >
                  {{ isLoading() ? 'Creating...' : 'Create' }}
                </button>
              </div>
            </div>
          </div>
        </div>
      }

      <!-- Groups Table -->
      @if (mailGroups().length === 0) {
        <div class="empty-state">
          <lucide-icon name="mail" size="36" class="empty-icon"></lucide-icon>
          <p class="empty-message">No mail groups yet. Groups are created automatically when emails arrive or you can create one manually.</p>
        </div>
      } @else {
        <div class="groups-table">
          <div class="table-header">
            <div class="col-subdomain">Subdomain</div>
            <div class="col-description">Description</div>
            <div class="col-retention">Retention</div>
            <div class="col-status">Status</div>
            <div class="col-actions">Actions</div>
          </div>
          @for (group of mailGroups(); track group.id) {
            <div class="table-row" [class.expanded]="expandedGroupId() === group.id">
              <div class="col-subdomain">
                <span class="subdomain-name">{{ group.subdomain || 'Unknown' }}</span>
              </div>
              <div class="col-description">
                <span class="description-text">{{ group.description || '--' }}</span>
              </div>
              <div class="col-retention">
                <span class="retention-text">{{ getRetentionLabel(group) }}</span>
              </div>
              <div class="col-status">
                @if (group.isOwner) {
                  <span class="badge badge-owner">Owner</span>
                }
                @if (!group.isOwner && authService.isAdmin()) {
                  <span class="badge badge-admin">Admin</span>
                }
                @if (group.isUserPrivate) {
                  <span class="badge badge-private">Private</span>
                }
              </div>
              <div class="col-actions">
                <button class="btn btn-icon-sm" (click)="toggleExpand(group)" title="Edit">
                  <lucide-icon name="edit" size="14"></lucide-icon>
                </button>
                @if (!group.isUserPrivate) {
                  <button class="btn btn-icon-sm" (click)="manageUsers(group)" title="Share">
                    <lucide-icon name="share-2" size="14"></lucide-icon>
                  </button>
                  @if (group.isOwner) {
                    <button class="btn btn-icon-sm btn-icon-danger" (click)="deleteGroup(group)" title="Delete">
                      <lucide-icon name="trash" size="14"></lucide-icon>
                    </button>
                  }
                }
              </div>
            </div>

            <!-- Inline Edit Panel -->
            @if (expandedGroupId() === group.id && editForm()) {
              <div class="expand-panel">
                <div class="expand-panel-content" [formGroup]="editForm()!">
                  <div class="edit-fields">
                    <div class="form-group">
                      <label class="form-label">Description</label>
                      <input type="text" class="form-control" formControlName="description" placeholder="Group description" />
                    </div>
                    <div class="form-group">
                      <label class="form-label">Retention (days)</label>
                      <div class="retention-input">
                        <input
                          type="number"
                          class="form-control"
                          formControlName="retentionDays"
                          min="0"
                          max="365"
                          placeholder="0 = no deletion"
                        />
                        <span class="retention-hint">
                          @if (editForm()?.get('retentionDays')?.value === 0 || editForm()?.get('retentionDays')?.value === null) {
                            No auto-deletion
                          } @else {
                            Auto-delete after {{ editForm()?.get('retentionDays')?.value }} days
                          }
                        </span>
                      </div>
                    </div>
                  </div>
                  <div class="expand-panel-actions">
                    <button class="btn btn-secondary btn-sm" (click)="cancelEdit()">Cancel</button>
                    <button
                      class="btn btn-primary btn-sm"
                      [disabled]="!editForm()?.valid || isLoading()"
                      (click)="saveGroup()"
                    >
                      {{ isLoading() ? 'Saving...' : 'Save' }}
                    </button>
                  </div>
                </div>
              </div>
            }
          }
        </div>
      }

      <!-- Admin: Browse All Groups -->
      @if (authService.isAdmin()) {
        <div class="section-header" style="margin-top: 24px;">
          <div class="header-left">
            <h4 class="section-title">All Groups</h4>
            <span class="groups-count">Admin</span>
          </div>
          @if (!browsingAllGroups()) {
            <button class="btn btn-secondary btn-sm" (click)="loadAllGroups()">
              <lucide-icon name="eye" size="14"></lucide-icon>
              Browse
            </button>
          } @else {
            <button class="btn btn-secondary btn-sm" (click)="browsingAllGroups.set(false)">
              <lucide-icon name="eye-off" size="14"></lucide-icon>
              Hide
            </button>
          }
        </div>
        @if (browsingAllGroups()) {
          <div class="groups-table">
            <div class="table-header">
              <div class="col-subdomain">Subdomain</div>
              <div class="col-description">Owner</div>
              <div class="col-retention">Status</div>
              <div class="col-actions">Actions</div>
            </div>
            @for (group of allGroups(); track group.id) {
              <div class="table-row">
                <div class="col-subdomain">
                  <span class="subdomain-name">{{ group.subdomain || 'Unknown' }}</span>
                </div>
                <div class="col-description">
                  <span class="description-text">{{ group.ownerUserName }}</span>
                </div>
                <div class="col-retention">
                  @if (group.hasAccess) {
                    <span class="badge badge-owner">Joined</span>
                  }
                </div>
                <div class="col-actions">
                  @if (!group.hasAccess) {
                    <button class="btn btn-primary btn-sm" (click)="joinGroup(group)" [disabled]="isLoading()">
                      <lucide-icon name="plus" size="14"></lucide-icon>
                      Join
                    </button>
                  } @else if (!group.isOwner) {
                    <button class="btn btn-secondary btn-sm" (click)="leaveGroup(group)" [disabled]="isLoading()">
                      <lucide-icon name="log-out" size="14"></lucide-icon>
                      Leave
                    </button>
                  }
                </div>
              </div>
            }
          </div>
        }
      }

      <!-- User Management Panel (overlay) -->
      @if (managingUsers() && selectedGroup()) {
        <div class="slide-panel-overlay" (click)="closeUserManagement()"></div>
        <div class="slide-panel">
          <div class="slide-panel-header">
            <div>
              <h5>Share Access</h5>
              <span class="slide-panel-subtitle">{{ selectedGroup()?.subdomain }}</span>
            </div>
            <button class="btn btn-icon" (click)="closeUserManagement()" title="Close">
              <lucide-icon name="x" size="16"></lucide-icon>
            </button>
          </div>
          <div class="slide-panel-body">
            <div class="add-user-row">
              <rd-select
                [(ngModel)]="selectedUserId"
                [items]="availableUsers()"
                bindLabel="userName"
                bindValue="id"
                [placeholder]="'Select user'"
                [searchable]="true"
                class="user-select"
              ></rd-select>
              <button class="btn btn-primary btn-sm" [disabled]="!selectedUserId || isLoading()" (click)="addUser()">
                <lucide-icon name="plus" size="14"></lucide-icon>
                Add
              </button>
            </div>

            @if (groupUsers().length === 0) {
              <p class="text-muted no-users-text">No users have been granted access.</p>
            } @else {
              <div class="shared-users-list">
                @for (groupUser of groupUsers(); track groupUser.id) {
                  <div class="shared-user-item">
                    <div class="shared-user-info">
                      <span class="shared-user-name">{{ groupUser.user.userName }}</span>
                      <span class="shared-user-date">Added {{ formatDate(groupUser.grantedAt) }}</span>
                    </div>
                    <button
                      class="btn btn-icon-sm btn-icon-danger"
                      (click)="removeUser(groupUser)"
                      title="Remove access"
                      [disabled]="isLoading()"
                    >
                      <lucide-icon name="x" size="14"></lucide-icon>
                    </button>
                  </div>
                }
              </div>
            }
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
  authService = inject(AuthService);
  private confirmDialog = inject(ConfirmDialogService);

  // Data signals
  mailGroups = signal<MailGroup[]>([]);
  users = signal<User[]>([]);
  groupUsers = signal<MailGroupUser[]>([]);

  // State signals
  selectedGroup = signal<MailGroup | null>(null);
  expandedGroupId = signal<number | null>(null);
  creatingGroup = signal<boolean>(false);
  managingUsers = signal<boolean>(false);
  isLoading = signal<boolean>(false);

  // Admin browse all groups
  allGroups = signal<AdminMailGroup[]>([]);
  browsingAllGroups = signal<boolean>(false);

  // Retention cache
  retentionCache = signal<Record<number, number | null>>({});

  // Form signals
  editForm = signal<FormGroup | null>(null);
  createForm = signal<FormGroup | null>(null);
  selectedUserId: string | null = null;

  constructor() {
    this.loadMailGroups();
    this.loadUsers();
  }

  loadMailGroups() {
    this.mailService.getMailGroups().subscribe((groups) => {
      this.mailGroups.set(groups);
    });
  }

  loadUsers() {
    this.mailService.getUsers().subscribe((users) => {
      this.users.set(users);
    });
  }

  loadAllGroups() {
    this.browsingAllGroups.set(true);
    this.mailService.getAllMailGroups().subscribe((groups) => {
      this.allGroups.set(groups);
    });
  }

  joinGroup(group: AdminMailGroup) {
    const userId = this.authService.getCurrentUserId();
    if (!userId) return;

    this.isLoading.set(true);
    this.mailService.grantUserAccess(group.id, userId).subscribe({
      next: () => {
        this.loadAllGroups();
        this.loadMailGroups();
        this.isLoading.set(false);
      },
      error: (error) => {
        console.error('Error joining group:', error);
        this.isLoading.set(false);
      },
    });
  }

  leaveGroup(group: AdminMailGroup) {
    const userId = this.authService.getCurrentUserId();
    if (!userId) return;

    this.isLoading.set(true);
    this.mailService.revokeUserAccess(group.id, userId).subscribe({
      next: () => {
        this.loadAllGroups();
        this.loadMailGroups();
        this.isLoading.set(false);
      },
      error: (error) => {
        console.error('Error leaving group:', error);
        this.isLoading.set(false);
      },
    });
  }

  loadGroupUsers(groupId: number) {
    this.mailService.getMailGroupUsers(groupId).subscribe((groupUsers) => {
      this.groupUsers.set(groupUsers);
      this.updateAvailableUsers();
    });
  }

  availableUsers = signal<User[]>([]);

  updateAvailableUsers() {
    const currentGroupUserIds = this.groupUsers().map((gu) => gu.userId);
    const available = this.users().filter((user) => !currentGroupUserIds.includes(user.id));
    this.availableUsers.set(available);
  }

  formatDate(dateString: string): string {
    const date = new Date(dateString);
    return date.toLocaleDateString('en-US', {
      year: 'numeric',
      month: 'short',
      day: 'numeric',
    });
  }

  getRetentionLabel(group: MailGroup): string {
    const cached = this.retentionCache()[group.id];
    if (cached !== undefined) {
      return cached === 0 || cached === null ? 'None' : `${cached}d`;
    }
    return '3d';
  }

  // Expand/collapse edit
  toggleExpand(group: MailGroup) {
    if (this.expandedGroupId() === group.id) {
      this.cancelEdit();
      return;
    }

    this.expandedGroupId.set(group.id);
    this.selectedGroup.set(group);

    const form = new FormGroup({
      description: new FormControl(group.description || ''),
      retentionDays: new FormControl<number | null>(null),
    });

    this.editForm.set(form);

    this.mailService.getRetentionSettings(group.id).subscribe({
      next: (settings) => {
        form.get('retentionDays')?.setValue(settings.retentionDays);
        this.retentionCache.update((cache) => ({ ...cache, [group.id]: settings.retentionDays }));
      },
      error: () => {
        form.get('retentionDays')?.setValue(3);
      },
    });
  }

  cancelEdit() {
    this.expandedGroupId.set(null);
    this.editForm.set(null);
    this.selectedGroup.set(null);
  }

  saveGroup() {
    const form = this.editForm();
    const group = this.selectedGroup();

    if (!form || !group || !form.valid) return;

    this.isLoading.set(true);

    const updateData = {
      id: group.id,
      description: form.value.description,
      isPublic: group.isPublic,
    };

    this.mailService.updateMailGroup(updateData).subscribe({
      next: (updatedGroup) => {
        if (form.value.retentionDays !== null) {
          this.mailService.updateRetentionSettings(group.id, form.value.retentionDays).subscribe({
            next: () => {
              this.retentionCache.update((cache) => ({ ...cache, [group.id]: form.value.retentionDays }));
              this.finishGroupUpdate(updatedGroup);
            },
            error: () => {
              this.finishGroupUpdate(updatedGroup);
            },
          });
        } else {
          this.finishGroupUpdate(updatedGroup);
        }
      },
      error: (error) => {
        console.error('Error updating group:', error);
        this.isLoading.set(false);
      },
    });
  }

  private finishGroupUpdate(updatedGroup: MailGroup) {
    const groups = this.mailGroups();
    const index = groups.findIndex((g) => g.id === updatedGroup.id);
    if (index !== -1) {
      groups[index] = updatedGroup;
      this.mailGroups.set([...groups]);
    }

    this.cancelEdit();
    this.isLoading.set(false);
  }

  // User management methods
  manageUsers(group: MailGroup) {
    this.selectedGroup.set(group);
    this.expandedGroupId.set(null);
    this.managingUsers.set(true);
    this.editForm.set(null);
    this.selectedUserId = null;

    this.loadGroupUsers(group.id);
  }

  closeUserManagement() {
    this.managingUsers.set(false);
    this.selectedGroup.set(null);
    this.groupUsers.set([]);
    this.availableUsers.set([]);
    this.selectedUserId = null;
  }

  addUser() {
    const group = this.selectedGroup();
    const userId = this.selectedUserId;

    if (!group || !userId) return;

    this.isLoading.set(true);

    this.mailService.grantUserAccess(group.id, userId).subscribe({
      next: () => {
        this.loadGroupUsers(group.id);
        this.selectedUserId = null;
        this.isLoading.set(false);
      },
      error: (error) => {
        console.error('Error adding user:', error);
        this.isLoading.set(false);
      },
    });
  }

  removeUser(groupUser: MailGroupUser) {
    const group = this.selectedGroup();
    if (!group) return;

    this.isLoading.set(true);

    this.mailService.revokeUserAccess(group.id, groupUser.userId).subscribe({
      next: () => {
        this.loadGroupUsers(group.id);
        this.isLoading.set(false);
      },
      error: (error) => {
        console.error('Error removing user:', error);
        this.isLoading.set(false);
      },
    });
  }

  // Create Group methods
  showCreateForm() {
    this.creatingGroup.set(true);
    this.expandedGroupId.set(null);
    this.managingUsers.set(false);

    const form = new FormGroup({
      subdomain: new FormControl('', [Validators.required, Validators.pattern(/^[a-z0-9-]+$/)]),
      description: new FormControl(''),
    });

    this.createForm.set(form);
  }

  cancelCreate() {
    this.creatingGroup.set(false);
    this.createForm.set(null);
  }

  createGroup() {
    const form = this.createForm();
    if (!form || !form.valid) return;

    this.isLoading.set(true);

    const request: CreateMailGroupRequest = {
      subdomain: form.get('subdomain')?.value,
      description: form.get('description')?.value || undefined,
      isPublic: false,
    };

    this.mailService.createMailGroup(request).subscribe({
      next: () => {
        this.loadMailGroups();
        this.cancelCreate();
        this.isLoading.set(false);
      },
      error: (error) => {
        console.error('Error creating group:', error);
        this.isLoading.set(false);
      },
    });
  }

  deleteGroup(group: MailGroup) {
    this.confirmDialog.confirmDelete(group.subdomain ?? undefined).subscribe((confirmed) => {
      if (!confirmed) return;

      this.isLoading.set(true);

      this.mailService.deleteMailGroup(group.id).subscribe({
        next: () => {
          this.loadMailGroups();
          this.isLoading.set(false);
        },
        error: (error) => {
          console.error('Error deleting group:', error);
          this.isLoading.set(false);
        },
      });
    });
  }
}
