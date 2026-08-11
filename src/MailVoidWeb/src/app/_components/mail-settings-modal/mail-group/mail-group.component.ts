import { ChangeDetectionStrategy, Component, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { form, required, pattern, min, max, FormField } from '@angular/forms/signals';
import { SelectComponent } from '@rd-ui';
import { LucideDynamicIcon } from '@lucide/angular';
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
  imports: [LucideDynamicIcon, FormsModule, SelectComponent, FormField],
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
            <svg lucideIcon="plus" size="14"></svg>
            Create Group
          </button>
        }
      </div>

      <!-- Create Group Inline Form -->
      @if (creatingGroup()) {
        <div class="inline-form">
          <div class="inline-form-header">
            <h5>Create New Mail Group</h5>
            <button class="btn btn-icon" (click)="cancelCreate()" title="Close">
              <svg lucideIcon="x" size="16"></svg>
            </button>
          </div>
          <div class="inline-form-body">
            <div class="form-row">
              <div class="form-group">
                <label for="subdomain" class="form-label">Subdomain</label>
                <input
                  id="subdomain"
                  type="text"
                  class="form-control"
                  [formField]="createGroupForm.subdomain"
                  placeholder="e.g., support, sales"
                />
                @if ((createSubmitted() || createGroupForm.subdomain().touched()) && createGroupForm.subdomain().invalid()) {
                  <div class="text-danger">
                    @for (error of createGroupForm.subdomain().errors(); track error.kind) {
                      <div>{{ error.message }}</div>
                    }
                  </div>
                }
              </div>
              <div class="form-group">
                <label for="description" class="form-label">Description</label>
                <input
                  id="description"
                  type="text"
                  class="form-control"
                  [formField]="createGroupForm.description"
                  placeholder="Optional description"
                />
              </div>
              <div class="form-group form-actions-inline">
                <button type="button" class="btn btn-secondary btn-sm" (click)="cancelCreate()">Cancel</button>
                <button
                  type="button"
                  class="btn btn-primary btn-sm"
                  [disabled]="createGroupForm().invalid() || isLoading()"
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
          <svg lucideIcon="mail" size="36" class="empty-icon"></svg>
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
                @if (group.isHidden) {
                  <span class="badge badge-secondary">Hidden</span>
                }
              </div>
              <div class="col-actions">
                <button
                  class="btn btn-icon-sm"
                  (click)="toggleHidden(group)"
                  [title]="group.isHidden ? 'Show in mailbox list' : 'Hide from mailbox list'"
                >
                  <svg [lucideIcon]="group.isHidden ? 'eye' : 'eye-off'" size="14"></svg>
                </button>
                <button class="btn btn-icon-sm" (click)="toggleExpand(group)" title="Edit">
                  <svg lucideIcon="square-pen" size="14"></svg>
                </button>
                @if (!group.isUserPrivate) {
                  <button class="btn btn-icon-sm" (click)="manageUsers(group)" title="Share">
                    <svg lucideIcon="share-2" size="14"></svg>
                  </button>
                  @if (group.isOwner) {
                    <button class="btn btn-icon-sm btn-icon-danger" (click)="deleteGroup(group)" title="Delete">
                      <svg lucideIcon="trash" size="14"></svg>
                    </button>
                  }
                }
              </div>
            </div>

            <!-- Inline Edit Panel -->
            @if (expandedGroupId() === group.id) {
              <div class="expand-panel">
                <div class="expand-panel-content">
                  <div class="edit-fields">
                    <div class="form-group">
                      <label class="form-label">Description</label>
                      <input type="text" class="form-control" [formField]="editGroupForm.description" placeholder="Group description" />
                    </div>
                    <div class="form-group">
                      <label class="form-label">Retention (days)</label>
                      <div class="retention-input">
                        <input
                          type="number"
                          class="form-control"
                          [formField]="editGroupForm.retentionDays"
                          placeholder="0 = no deletion"
                        />
                        <span class="retention-hint">
                          @if (editGroupModel().retentionDays === 0 || editGroupModel().retentionDays === null) {
                            No auto-deletion
                          } @else {
                            Auto-delete after {{ editGroupModel().retentionDays }} days
                          }
                        </span>
                      </div>
                    </div>
                  </div>
                  <div class="expand-panel-actions">
                    <button class="btn btn-secondary btn-sm" (click)="cancelEdit()">Cancel</button>
                    <button
                      class="btn btn-primary btn-sm"
                      [disabled]="editGroupForm().invalid() || isLoading()"
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
              <svg lucideIcon="eye" size="14"></svg>
              Browse
            </button>
          } @else {
            <button class="btn btn-secondary btn-sm" (click)="browsingAllGroups.set(false)">
              <svg lucideIcon="eye-off" size="14"></svg>
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
                      <svg lucideIcon="plus" size="14"></svg>
                      Join
                    </button>
                  } @else if (!group.isOwner) {
                    <button class="btn btn-secondary btn-sm" (click)="leaveGroup(group)" [disabled]="isLoading()">
                      <svg lucideIcon="log-out" size="14"></svg>
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
              <svg lucideIcon="x" size="16"></svg>
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
                <svg lucideIcon="plus" size="14"></svg>
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
                      <svg lucideIcon="x" size="14"></svg>
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

  // Signal forms: models are always present; visibility is driven by creatingGroup()/expandedGroupId().
  createGroupModel = signal<{ subdomain: string; description: string }>({ subdomain: '', description: '' });
  createGroupForm = form(this.createGroupModel, (path) => {
    required(path.subdomain, { message: 'Subdomain is required' });
    pattern(path.subdomain, /^[a-z0-9-]+$/, { message: 'Subdomain may only contain lowercase letters, numbers and hyphens' });
  });
  createSubmitted = signal(false);

  editGroupModel = signal<{ description: string; retentionDays: number | null }>({ description: '', retentionDays: null });
  editGroupForm = form(this.editGroupModel, (path) => {
    min(path.retentionDays, 0, { message: 'Retention cannot be negative' });
    max(path.retentionDays, 365, { message: 'Retention cannot exceed 365 days' });
  });

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

  // Per-user hide/show. Hidden groups drop out of the mailbox sidebar (the sidebar
  // refreshes when this settings modal closes).
  toggleHidden(group: MailGroup) {
    const request$ = group.isHidden
      ? this.mailService.unhideMailGroup(group.id)
      : this.mailService.hideMailGroup(group.id);
    request$.subscribe({
      next: () => this.loadMailGroups(),
      error: (error) => console.error('Error updating mailbox visibility:', error),
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

    this.editGroupModel.set({ description: group.description || '', retentionDays: null });

    this.mailService.getRetentionSettings(group.id).subscribe({
      next: (settings) => {
        this.editGroupModel.update((m) => ({ ...m, retentionDays: settings.retentionDays }));
        this.retentionCache.update((cache) => ({ ...cache, [group.id]: settings.retentionDays }));
      },
      error: () => {
        this.editGroupModel.update((m) => ({ ...m, retentionDays: 3 }));
      },
    });
  }

  cancelEdit() {
    this.expandedGroupId.set(null);
    this.selectedGroup.set(null);
  }

  saveGroup() {
    const group = this.selectedGroup();

    if (!group || this.editGroupForm().invalid()) return;

    this.isLoading.set(true);

    const { description, retentionDays } = this.editGroupModel();

    const updateData = {
      id: group.id,
      description,
      isPublic: group.isPublic,
    };

    this.mailService.updateMailGroup(updateData).subscribe({
      next: (updatedGroup) => {
        if (retentionDays !== null) {
          this.mailService.updateRetentionSettings(group.id, retentionDays).subscribe({
            next: () => {
              this.retentionCache.update((cache) => ({ ...cache, [group.id]: retentionDays }));
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

    this.createGroupModel.set({ subdomain: '', description: '' });
    this.createSubmitted.set(false);
  }

  cancelCreate() {
    this.creatingGroup.set(false);
    this.createSubmitted.set(false);
  }

  createGroup() {
    if (this.createGroupForm().invalid()) {
      this.createSubmitted.set(true);
      return;
    }

    this.isLoading.set(true);

    const { subdomain, description } = this.createGroupModel();
    const request: CreateMailGroupRequest = {
      subdomain,
      description: description || undefined,
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
