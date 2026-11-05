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
import { initialsFor } from '../../../_services/mail-format';
@Component({
  selector: 'app-mail-group',
  imports: [LucideDynamicIcon, FormsModule, SelectComponent, FormField],
  template: `
    <section class="settings-section">
      <div class="settings-section-head">
        <div>
          <h4>
            Mail groups
            <span class="count-pill count-pill-muted">{{ mailGroups().length }}</span>
          </h4>
          <p>Groups are created automatically when mail arrives for a new subdomain, or you can add one here.</p>
        </div>
        @if (!creatingGroup()) {
          <button type="button" class="btn btn-sm" (click)="showCreateForm()">
            <svg lucideIcon="plus" size="14"></svg>
            New group
          </button>
        }
      </div>

      @if (creatingGroup()) {
        <form class="settings-subform" (submit)="$event.preventDefault(); createGroup()">
          <div class="form-grid">
            <div class="form-group">
              <label for="subdomain">Subdomain</label>
              <input
                id="subdomain"
                type="text"
                class="form-control"
                [formField]="createGroupForm.subdomain"
                placeholder="e.g. support"
              />
              @if (
                (createSubmitted() || createGroupForm.subdomain().touched()) && createGroupForm.subdomain().invalid()
              ) {
                <div class="text-danger">
                  @for (error of createGroupForm.subdomain().errors(); track error.kind) {
                    <div>{{ error.message }}</div>
                  }
                </div>
              }
            </div>
            <div class="form-group">
              <label for="description">Description</label>
              <input
                id="description"
                type="text"
                class="form-control"
                [formField]="createGroupForm.description"
                placeholder="Optional"
              />
            </div>
          </div>
          <div class="form-actions">
            <button type="button" class="btn btn-sm" (click)="cancelCreate()">Cancel</button>
            <button
              type="submit"
              class="btn btn-primary btn-sm"
              [disabled]="createGroupForm().invalid() || isLoading()"
            >
              {{ isLoading() ? 'Creating…' : 'Create group' }}
            </button>
          </div>
        </form>
      }

      <div class="data-table groups-table">
        <div class="data-table-head">
          <span>Subdomain</span>
          <span>Description</span>
          <span>Retention</span>
          <span>Status</span>
          <span></span>
        </div>
        @for (group of mailGroups(); track group.id) {
          <div class="data-table-row" [class.expanded]="expandedGroupId() === group.id">
            <span class="group-name cell-ellipsis">{{ group.subdomain || 'Unknown' }}</span>
            <span class="text-muted cell-ellipsis">{{ group.description || '—' }}</span>
            <span>{{ getRetentionLabel(group) }}</span>
            <span class="group-badges">
              @if (group.isOwner) {
                <span class="badge badge-accent">Owner</span>
              }
              @if (!group.isOwner && authService.isAdmin()) {
                <span class="badge badge-muted">Admin</span>
              }
              @if (group.isUserPrivate) {
                <span class="badge badge-warning">Private</span>
              }
              @if (group.isHidden) {
                <span class="badge badge-muted">Hidden</span>
              }
            </span>
            <div class="data-table-actions">
              <button
                type="button"
                class="icon-btn icon-btn-sm"
                (click)="toggleHidden(group)"
                [attr.aria-label]="group.isHidden ? 'Show in mailbox list' : 'Hide from mailbox list'"
                [title]="group.isHidden ? 'Show in mailbox list' : 'Hide from mailbox list'"
              >
                <svg [lucideIcon]="group.isHidden ? 'eye' : 'eye-off'" size="14"></svg>
              </button>
              <button
                type="button"
                class="icon-btn icon-btn-sm"
                [class.active]="expandedGroupId() === group.id"
                (click)="toggleExpand(group)"
                aria-label="Edit group"
                title="Edit"
              >
                <svg lucideIcon="square-pen" size="14"></svg>
              </button>
              @if (!group.isUserPrivate) {
                <button
                  type="button"
                  class="icon-btn icon-btn-sm"
                  (click)="manageUsers(group)"
                  aria-label="Share group"
                  title="Share"
                >
                  <svg lucideIcon="share-2" size="14"></svg>
                </button>
                @if (group.isOwner) {
                  <button
                    type="button"
                    class="icon-btn icon-btn-sm icon-btn-danger"
                    (click)="deleteGroup(group)"
                    aria-label="Delete group"
                    title="Delete"
                  >
                    <svg lucideIcon="trash-2" size="14"></svg>
                  </button>
                }
              }
            </div>
          </div>

          @if (expandedGroupId() === group.id) {
            <form class="data-table-detail" (submit)="$event.preventDefault(); saveGroup()">
              <div class="form-grid">
                <div class="form-group">
                  <label [attr.for]="'desc-' + group.id">Description</label>
                  <input
                    [id]="'desc-' + group.id"
                    type="text"
                    class="form-control"
                    [formField]="editGroupForm.description"
                    placeholder="Group description"
                  />
                </div>
                <div class="form-group">
                  <label [attr.for]="'ret-' + group.id">Retention (days)</label>
                  <input
                    [id]="'ret-' + group.id"
                    type="number"
                    class="form-control"
                    [formField]="editGroupForm.retentionDays"
                    placeholder="0 = keep forever"
                  />
                  <span class="form-text">
                    @if (editGroupModel().retentionDays === 0 || editGroupModel().retentionDays === null) {
                      Mail is kept until you delete it.
                    } @else {
                      Mail is deleted after {{ editGroupModel().retentionDays }} days.
                    }
                  </span>
                </div>
              </div>
              <div class="form-actions">
                <button type="button" class="btn btn-sm" (click)="cancelEdit()">Cancel</button>
                <button
                  type="submit"
                  class="btn btn-primary btn-sm"
                  [disabled]="editGroupForm().invalid() || isLoading()"
                >
                  {{ isLoading() ? 'Saving…' : 'Save' }}
                </button>
              </div>
            </form>
          }
        } @empty {
          <div class="data-table-empty">No mail groups yet.</div>
        }
      </div>
    </section>

    @if (authService.isAdmin()) {
      <section class="settings-section">
        <div class="settings-section-head">
          <div>
            <h4>All groups <span class="badge badge-muted">Admin</span></h4>
            <p>Every group on this server. Join one to see its mail.</p>
          </div>
          @if (!browsingAllGroups()) {
            <button type="button" class="btn btn-sm" (click)="loadAllGroups()">
              <svg lucideIcon="eye" size="14"></svg>
              Browse
            </button>
          } @else {
            <button type="button" class="btn btn-sm" (click)="browsingAllGroups.set(false)">
              <svg lucideIcon="eye-off" size="14"></svg>
              Hide
            </button>
          }
        </div>
        @if (browsingAllGroups()) {
          <div class="data-table all-groups-table">
            <div class="data-table-head">
              <span>Subdomain</span>
              <span>Owner</span>
              <span>Status</span>
              <span></span>
            </div>
            @for (group of allGroups(); track group.id) {
              <div class="data-table-row">
                <span class="group-name cell-ellipsis">{{ group.subdomain || 'Unknown' }}</span>
                <span class="text-muted cell-ellipsis">{{ group.ownerUserName }}</span>
                <span>
                  @if (group.hasAccess) {
                    <span class="badge badge-success">Joined</span>
                  }
                </span>
                <div class="data-table-actions">
                  @if (!group.hasAccess) {
                    <button type="button" class="btn btn-sm" (click)="joinGroup(group)" [disabled]="isLoading()">
                      <svg lucideIcon="plus" size="14"></svg>
                      Join
                    </button>
                  } @else if (!group.isOwner) {
                    <button type="button" class="btn btn-sm" (click)="leaveGroup(group)" [disabled]="isLoading()">
                      <svg lucideIcon="log-out" size="14"></svg>
                      Leave
                    </button>
                  }
                </div>
              </div>
            } @empty {
              <div class="data-table-empty">No groups.</div>
            }
          </div>
        }
      </section>
    }

    <!-- Share panel (slide-over) -->
    @if (managingUsers() && selectedGroup()) {
      <div class="slide-panel-overlay" (click)="closeUserManagement()"></div>
      <div class="slide-panel" role="dialog" aria-modal="true" aria-label="Share access">
        <div class="slide-panel-header">
          <div>
            <h5>Share access</h5>
            <span class="slide-panel-subtitle">{{ selectedGroup()?.subdomain }}</span>
          </div>
          <button type="button" class="icon-btn" (click)="closeUserManagement()" aria-label="Close" title="Close">
            <svg lucideIcon="x" size="18"></svg>
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
            <button
              type="button"
              class="btn btn-primary"
              [disabled]="!selectedUserId || isLoading()"
              (click)="addUser()"
            >
              Add
            </button>
          </div>

          @for (groupUser of groupUsers(); track groupUser.id) {
            <div class="shared-user">
              <span class="avatar-initials">{{ initials(groupUser.user.userName) }}</span>
              <div class="shared-user-info">
                <span class="shared-user-name">{{ groupUser.user.userName }}</span>
                <span class="text-muted">Added {{ formatDate(groupUser.grantedAt) }}</span>
              </div>
              <button
                type="button"
                class="icon-btn icon-btn-sm icon-btn-danger"
                (click)="removeUser(groupUser)"
                [disabled]="isLoading()"
                aria-label="Remove access"
                title="Remove access"
              >
                <svg lucideIcon="x" size="14"></svg>
              </button>
            </div>
          } @empty {
            <p class="side-empty">Nobody else has access yet.</p>
          }
        </div>
      </div>
    }
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
    pattern(path.subdomain, /^[a-z0-9-]+$/, {
      message: 'Subdomain may only contain lowercase letters, numbers and hyphens',
    });
  });
  createSubmitted = signal(false);

  editGroupModel = signal<{ description: string; retentionDays: number | null }>({
    description: '',
    retentionDays: null,
  });
  editGroupForm = form(this.editGroupModel, (path) => {
    min(path.retentionDays, 0, { message: 'Retention cannot be negative' });
    max(path.retentionDays, 365, { message: 'Retention cannot exceed 365 days' });
  });

  selectedUserId: string | null = null;
  initials = initialsFor;

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
