import { ChangeDetectionStrategy, Component, inject, signal } from '@angular/core';
import { FormControl, FormGroup, FormsModule, ReactiveFormsModule, Validators } from '@angular/forms';
import { NgSelectModule } from '@ng-select/ng-select';
import { LucideAngularModule } from 'lucide-angular';
import { ValdemortModule } from 'ngx-valdemort';
import {
  CreateMailGroupRequest,
  MailGroup,
  MailGroupUser,
  MailService,
  User,
} from '../../../_services/api/mail.service';
import { AuthService } from '../../../_services/auth-service';
@Component({
  selector: 'app-mail-group',
  imports: [LucideAngularModule, ReactiveFormsModule, FormsModule, NgSelectModule, ValdemortModule],
  template: `
    <div class="mail-group-layout">
      <div class="info-banner">
        <div class="info-content">
          <lucide-icon name="info" size="20" class="info-icon"></lucide-icon>
          <div class="info-text">
            <h5>Mail Group Management</h5>
            <p>
              Mail groups are automatically created based on email subdomains. You can manage group settings and user
              access for groups you own.
            </p>
          </div>
        </div>
      </div>

      <!-- Mail Groups List -->
      @if (!editingGroup() && !managingUsers() && !creatingGroup()) {
        <div class="groups-container">
          <div class="container-header">
            <h4 class="container-title">Available Mail Groups</h4>
            <div class="header-actions">
              <span class="groups-count">{{ mailGroups().length }} groups</span>
              <button class="btn btn-primary btn-sm" (click)="showCreateForm()">
                <lucide-icon name="plus" size="14"></lucide-icon>
                Create Group
              </button>
            </div>
          </div>

          @if (mailGroups().length === 0) {
            <div class="empty-state">
              <lucide-icon name="mail" size="48" class="empty-icon"></lucide-icon>
              <h5 class="empty-title">No Mail Groups Yet</h5>
              <p class="empty-message">Mail groups will appear here automatically when emails are received.</p>
            </div>
          } @else {
            <div class="groups-grid">
              @for (group of mailGroups(); track group.id) {
                <div class="group-card" [class.selected]="selectedGroup()?.id === group.id">
                  <div class="group-header">
                    <div class="group-icon">
                      <lucide-icon name="mail" size="24"></lucide-icon>
                    </div>
                    <div class="group-info">
                      <h5 class="group-subdomain">{{ group.subdomain || 'Unknown' }}</h5>
                      <p class="group-path">{{ group.path || 'No path assigned' }}</p>
                    </div>
                    <div class="group-badges">
                      <span class="group-badge" [class.public]="group.isPublic" [class.private]="!group.isPublic">
                        {{ group.isPublic ? 'Public' : 'Private' }}
                      </span>
                      @if (group.isOwner) {
                        <span class="owner-badge">Owner</span>
                      }
                      @if (!group.isOwner && authService.isAdmin() && group.isPublic) {
                        <span class="admin-badge">Admin Access</span>
                      }
                    </div>
                  </div>

                  @if (group.description) {
                    <p class="group-description">{{ group.description }}</p>
                  }

                  <div class="group-meta">
                    <div class="meta-item">
                      <lucide-icon name="calendar" size="14"></lucide-icon>
                      <span>Created {{ formatDate(group.createdAt) }}</span>
                    </div>
                  </div>

                  @if (canEditGroup(group) && !group.isUserPrivate) {
                    <div class="group-actions">
                      <button class="btn btn-outline btn-sm" (click)="editGroup(group)" title="Edit Group">
                        <lucide-icon name="edit" size="14"></lucide-icon>
                        Edit
                      </button>
                      <button class="btn btn-outline btn-sm" (click)="manageUsers(group)" title="Manage Users">
                        <lucide-icon name="users" size="14"></lucide-icon>
                        Users
                      </button>
                      @if (group.isOwner) {
                        <button
                          class="btn btn-outline btn-sm btn-danger"
                          (click)="deleteGroup(group)"
                          title="Delete Group"
                        >
                          <lucide-icon name="trash" size="14"></lucide-icon>
                          Delete
                        </button>
                      }
                    </div>
                  }
                  @if (group.isUserPrivate) {
                    <div class="group-actions">
                      <span class="private-label">Private Mailbox</span>
                    </div>
                  }
                </div>
              }
            </div>
          }
        </div>
      }

      <!-- Create Group Panel -->
      @if (creatingGroup() && createForm()) {
        <div class="panel">
          <div class="panel-header">
            <h4 class="panel-title">Create New Mail Group</h4>
            <button class="btn btn-icon" (click)="cancelCreate()" title="Close">
              <lucide-icon name="x" size="16"></lucide-icon>
            </button>
          </div>
          <div class="panel-content" [formGroup]="createForm()!">
            <div class="form-group">
              <label for="subdomain" class="form-label">Subdomain</label>
              <input
                id="subdomain"
                type="text"
                class="form-control"
                formControlName="subdomain"
                placeholder="e.g., support, sales, notifications"
              />
              <div class="form-text">Emails sent to anything&#64;[subdomain].mailvoid.com will go to this group</div>
              <val-errors controlName="subdomain" label="Subdomain"></val-errors>
            </div>

            <div class="form-group">
              <label for="description" class="form-label">Description</label>
              <textarea
                id="description"
                class="form-control"
                formControlName="description"
                placeholder="Enter a description for this mail group"
                rows="3"
              ></textarea>
              <val-errors controlName="description" label="Description"></val-errors>
            </div>

            <div class="form-group">
              <div class="form-check">
                <input type="checkbox" id="isPublic" class="form-check-input" formControlName="isPublic" />
                <label for="isPublic" class="form-check-label"> Public Group </label>
              </div>
              <div class="form-text">
                Public groups are accessible to all users. Private groups require explicit user access.
              </div>
            </div>

            <div class="form-actions">
              <button type="button" class="btn btn-secondary" (click)="cancelCreate()">Cancel</button>
              <button
                type="button"
                class="btn btn-primary"
                [disabled]="!createForm()?.valid || isLoading()"
                (click)="createGroup()"
              >
                @if (isLoading()) {
                  <span>Creating...</span>
                }
                @if (!isLoading()) {
                  <span>Create Group</span>
                }
              </button>
            </div>
          </div>
        </div>
      }

      <!-- Edit Group Panel -->
      @if (editingGroup() && editForm()) {
        <div class="panel">
          <div class="panel-header">
            <div class="panel-title-section">
              <h4 class="panel-title">Edit Mail Group</h4>
              <div class="panel-subtitle">
                <span class="group-name">{{ editingGroup()?.subdomain }}</span>
                <span class="group-path">{{ editingGroup()?.path }}</span>
              </div>
            </div>
            <button class="btn btn-icon" (click)="cancelEdit()" title="Close">
              <lucide-icon name="x" size="16"></lucide-icon>
            </button>
          </div>

          <div class="panel-content" [formGroup]="editForm()!">
            <!-- Basic Settings -->
            <div class="settings-section">
              <h5 class="section-title">
                <lucide-icon name="edit" size="16"></lucide-icon>
                Basic Settings
              </h5>

              <div class="form-group">
                <label for="description" class="form-label">Description</label>
                <textarea
                  id="description"
                  class="form-control"
                  formControlName="description"
                  placeholder="Enter a description for this mail group"
                  rows="3"
                ></textarea>
                <val-errors controlName="description" label="Description"></val-errors>
              </div>

              @if (!editingGroup()?.isUserPrivate) {
                <div class="form-group">
                  <div class="form-check">
                    <input type="checkbox" id="isPublic" class="form-check-input" formControlName="isPublic" />
                    <label for="isPublic" class="form-check-label">Public Group</label>
                  </div>
                  <div class="form-text">
                    Public groups are accessible to all users. Private groups require explicit user access.
                  </div>
                </div>
              }
            </div>

            <!-- Retention Settings -->
            @if (editingGroup()?.isOwner) {
              <div class="settings-section">
                <h5 class="section-title">
                  <lucide-icon name="clock" size="16"></lucide-icon>
                  Email Retention
                </h5>

                <div class="retention-info">
                  <lucide-icon name="info" size="16" class="info-icon"></lucide-icon>
                  <div class="info-text">
                    <p>Set how long emails should be kept in this mailbox before automatic deletion.</p>
                    <p>Set to 0 to disable automatic deletion.</p>
                  </div>
                </div>

                <div class="form-group">
                  <label for="retentionDays" class="form-label">Retention Period (days)</label>
                  <div class="retention-input-group">
                    <input
                      type="number"
                      id="retentionDays"
                      class="form-control"
                      formControlName="retentionDays"
                      min="0"
                      max="365"
                      placeholder="Enter days (0-365)"
                    />
                    <span class="retention-hint">
                      @if (
                        editForm()?.get('retentionDays')?.value === 0 ||
                        editForm()?.get('retentionDays')?.value === null
                      ) {
                        No automatic deletion
                      } @else if (editForm()?.get('retentionDays')?.value === 1) {
                        Emails older than 1 day will be deleted
                      } @else {
                        Emails older than {{ editForm()?.get('retentionDays')?.value }} days will be deleted
                      }
                    </span>
                  </div>
                </div>
              </div>
            }

            <div class="form-actions">
              <button type="button" class="btn btn-secondary" (click)="cancelEdit()">Cancel</button>
              <button
                type="button"
                class="btn btn-primary"
                [disabled]="!editForm()?.valid || isLoading()"
                (click)="saveGroup()"
              >
                @if (isLoading()) {
                  <lucide-icon name="loader-2" size="14" class="loading-icon"></lucide-icon>
                  Saving...
                } @else {
                  Save Changes
                }
              </button>
            </div>
          </div>
        </div>
      }

      <!-- User Management Panel -->
      @if (managingUsers() && selectedGroup()) {
        <div class="panel">
          <div class="panel-header">
            <h4 class="panel-title">Manage Users - {{ selectedGroup()?.subdomain }}</h4>
            <button class="btn btn-icon" (click)="closeUserManagement()" title="Close">
              <lucide-icon name="x" size="16"></lucide-icon>
            </button>
          </div>
          <div class="panel-content">
            <!-- Add User Section -->
            <div class="add-user-section">
              <h5>Add User Access</h5>
              <div class="add-user-form">
                <ng-select
                  [(ngModel)]="selectedUserId"
                  [items]="availableUsers()"
                  bindLabel="userName"
                  bindValue="id"
                  placeholder="Select a user to grant access"
                  class="user-select"
                >
                  @for (user of availableUsers(); track user) {
                    <ng-option [value]="user.id">
                      {{ user.userName }}
                    </ng-option>
                  }
                </ng-select>
                <button class="btn btn-primary" [disabled]="!selectedUserId || isLoading()" (click)="addUser()">
                  <lucide-icon name="plus" size="14"></lucide-icon>
                  Add User
                </button>
              </div>
            </div>

            <!-- Current Users List -->
            <div class="current-users-section">
              <h5>Current Users</h5>
              @if (groupUsers().length === 0) {
                <p class="text-muted">No users have been granted access to this group.</p>
              } @else {
                <div class="users-list">
                  @for (groupUser of groupUsers(); track groupUser.id) {
                    <div class="user-item">
                      <div class="user-info">
                        <span class="user-name">{{ groupUser.user.userName }}</span>
                        <span class="user-granted">Granted {{ formatDate(groupUser.grantedAt) }}</span>
                      </div>
                      <button
                        class="btn btn-danger btn-sm"
                        (click)="removeUser(groupUser)"
                        title="Remove user access"
                        [disabled]="isLoading()"
                      >
                        <lucide-icon name="trash-2" size="14"></lucide-icon>
                      </button>
                    </div>
                  }
                </div>
              }
            </div>

            @if (selectedGroup()?.isPublic) {
              <div class="public-notice">
                <lucide-icon name="info" size="16"></lucide-icon>
                <span>This is a public group. All users automatically have access regardless of the list above.</span>
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

  // Data signals
  mailGroups = signal<MailGroup[]>([]);
  users = signal<User[]>([]);
  groupUsers = signal<MailGroupUser[]>([]);

  // State signals
  selectedGroup = signal<MailGroup | null>(null);
  editingGroup = signal<MailGroup | null>(null);
  creatingGroup = signal<boolean>(false);
  managingUsers = signal<boolean>(false);
  isLoading = signal<boolean>(false);

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

  canEditGroup(group: MailGroup): boolean {
    // Owner can always edit their own groups
    if (group.isOwner) return true;

    // Admin can edit public groups
    if (this.authService.isAdmin() && group.isPublic) return true;

    return false;
  }

  // Group editing methods
  editGroup(group: MailGroup) {
    this.editingGroup.set(group);
    this.selectedGroup.set(group);
    this.managingUsers.set(false);

    const form = new FormGroup({
      description: new FormControl(group.description || ''),
      isPublic: new FormControl(group.isPublic),
      retentionDays: new FormControl<number | null>(null),
    });

    this.editForm.set(form);

    // Load current retention settings if user is owner
    if (group.isOwner) {
      this.isLoading.set(true);
      this.mailService.getRetentionSettings(group.id).subscribe({
        next: (settings) => {
          form.get('retentionDays')?.setValue(settings.retentionDays);
          this.isLoading.set(false);
        },
        error: (error) => {
          console.error('Error loading retention settings:', error);
          form.get('retentionDays')?.setValue(3); // Default value
          this.isLoading.set(false);
        },
      });
    }
  }

  cancelEdit() {
    this.editingGroup.set(null);
    this.editForm.set(null);
    this.selectedGroup.set(null);
  }

  saveGroup() {
    const form = this.editForm();
    const group = this.editingGroup();

    if (!form || !group || !form.valid) return;

    this.isLoading.set(true);

    const updateData = {
      id: group.id,
      description: form.value.description,
      isPublic: form.value.isPublic,
    };

    // Update basic group settings first
    this.mailService.updateMailGroup(updateData).subscribe({
      next: (updatedGroup) => {
        // Update retention settings if user is owner
        if (group.isOwner && form.value.retentionDays !== null) {
          this.mailService.updateRetentionSettings(group.id, form.value.retentionDays).subscribe({
            next: () => {
              this.finishGroupUpdate(updatedGroup);
            },
            error: (error) => {
              console.error('Error updating retention settings:', error);
              // Still finish the group update even if retention fails
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
    // Update the group in the list
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
    this.editingGroup.set(null);
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
    this.editingGroup.set(null);
    this.managingUsers.set(false);

    const form = new FormGroup({
      subdomain: new FormControl('', [Validators.required, Validators.pattern(/^[a-z0-9-]+$/)]),
      description: new FormControl(''),
      isPublic: new FormControl(true),
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
      isPublic: form.get('isPublic')?.value,
    };

    this.mailService.createMailGroup(request).subscribe({
      next: (group) => {
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
    if (
      !confirm(`Are you sure you want to delete the mail group "${group.subdomain}"? This action cannot be undone.`)
    ) {
      return;
    }

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
  }
}
