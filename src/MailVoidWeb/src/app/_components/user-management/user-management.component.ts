import { ChangeDetectionStrategy, Component, inject, signal } from '@angular/core';
import { form, required, minLength, pattern, FormField } from '@angular/forms/signals';
import { LucideDynamicIcon } from '@lucide/angular';
import { ConfirmDialogService, ModalContainerService } from '@rd-ui';
import { CreateUserRequest, MIN_PASSWORD_LENGTH, Role, UpdateUserRoleRequest, User } from '../../_models/user.model';
import { AuthService } from '../../_services/auth-service';
import { initialsFor } from '../../_services/mail-format';
import { UserManagementService } from '../../_services/user-management.service';
import { ResetPasswordDialogComponent } from './reset-password-dialog/reset-password-dialog.component';

@Component({
  selector: 'app-user-management',
  imports: [FormField, LucideDynamicIcon],
  template: `
    <section class="settings-section">
      <div class="settings-section-head">
        <div>
          <h4>Create user</h4>
          <p>Add someone who can sign in to MailVoid. A subdomain gives them their own private mailboxes.</p>
        </div>
      </div>
      <form (submit)="createUser($event)" class="form-grid">
        <div class="form-group">
          <label for="username">Username</label>
          <input
            id="username"
            type="text"
            [formField]="createUserForm.username"
            class="form-control"
            placeholder="Username"
          />
          @if (createUserForm.username().touched() && createUserForm.username().invalid()) {
            <div class="text-danger">
              @for (error of createUserForm.username().errors(); track error.kind) {
                <div>{{ error.message }}</div>
              }
            </div>
          }
        </div>
        <div class="form-group">
          <label for="password">Password</label>
          <input
            id="password"
            type="password"
            [formField]="createUserForm.password"
            class="form-control"
            placeholder="At least 8 characters"
            autocomplete="new-password"
          />
          @if (createUserForm.password().touched() && createUserForm.password().invalid()) {
            <div class="text-danger">
              @for (error of createUserForm.password().errors(); track error.kind) {
                <div>{{ error.message }}</div>
              }
            </div>
          }
        </div>
        <div class="form-group">
          <label for="subdomain">Subdomain</label>
          <input
            id="subdomain"
            type="text"
            [formField]="createUserForm.subdomain"
            class="form-control"
            placeholder="Optional, e.g. sales"
          />
          @if (createUserForm.subdomain().touched() && createUserForm.subdomain().invalid()) {
            <div class="text-danger">
              @for (error of createUserForm.subdomain().errors(); track error.kind) {
                <div>{{ error.message }}</div>
              }
            </div>
          }
        </div>
        <div class="form-group">
          <label for="role">Role</label>
          <select id="role" [formField]="createUserForm.role" class="form-control">
            <option value="0">User</option>
            <option value="1">Admin</option>
          </select>
        </div>
        <button type="submit" class="btn btn-primary" [disabled]="createUserForm().invalid() || isCreatingUser()">
          <svg lucideIcon="plus" size="14"></svg>
          {{ isCreatingUser() ? 'Creating…' : 'Create user' }}
        </button>
      </form>
    </section>

    <section class="settings-section">
      <div class="settings-section-head">
        <div>
          <h4>
            Users
            @if (users().length) {
              <span class="count-pill count-pill-muted">{{ users().length }}</span>
            }
          </h4>
        </div>
        <button
          type="button"
          class="icon-btn icon-btn-bordered"
          (click)="loadUsers()"
          aria-label="Refresh users"
          title="Refresh"
        >
          <svg lucideIcon="refresh-cw" size="15"></svg>
        </button>
      </div>

      @if (errorMessage()) {
        <div class="message message-error">{{ errorMessage() }}</div>
      }
      @if (successMessage()) {
        <div class="message message-success">{{ successMessage() }}</div>
      }

      <div class="data-table users-table">
        <div class="data-table-head">
          <span>User</span>
          <span>Subdomain</span>
          <span>Access</span>
          <span>Created</span>
          <span></span>
        </div>
        @if (isLoading()) {
          <div class="data-table-empty">Loading users…</div>
        } @else {
          @for (user of users(); track user.id) {
            <div class="data-table-row">
              <span class="user-name">
                <span class="avatar-initials">{{ initials(user.userName) }}</span>
                <span class="cell-ellipsis">{{ user.userName }}</span>
              </span>
              <span>
                @if (user.subdomain) {
                  <code class="inline-code">{{ user.subdomain }}</code>
                } @else {
                  <span class="text-muted">—</span>
                }
              </span>
              <span class="user-access">
                @if (editingRoleUserId() === user.id) {
                  <select
                    class="role-select"
                    [value]="user.role"
                    (change)="onRoleChange($event, user.id)"
                    aria-label="Role"
                  >
                    <option value="0">User</option>
                    <option value="1">Admin</option>
                  </select>
                } @else {
                  <span class="badge" [class]="user.role === Role.Admin ? 'badge-accent' : 'badge-muted'">
                    {{ getRoleDisplayName(user.role) }}
                  </span>
                  @if (user.role !== Role.Admin && user.webhooksEnabled) {
                    <span class="badge" title="Can use the Hooks page">Hooks</span>
                  }
                }
              </span>
              <span class="text-muted">{{ formatDate(user.timeStamp) }}</span>
              <div class="data-table-actions">
                @if (editingRoleUserId() === user.id) {
                  <button
                    type="button"
                    class="icon-btn icon-btn-sm"
                    (click)="cancelRoleEdit()"
                    aria-label="Cancel role change"
                    title="Cancel"
                  >
                    <svg lucideIcon="x" size="14"></svg>
                  </button>
                } @else {
                  <button
                    type="button"
                    class="icon-btn icon-btn-sm"
                    (click)="editRole(user.id)"
                    aria-label="Change role"
                    title="Change role"
                  >
                    <svg lucideIcon="square-pen" size="14"></svg>
                  </button>
                  @if (user.role !== Role.Admin) {
                    <button
                      type="button"
                      class="icon-btn icon-btn-sm"
                      [class.active]="user.webhooksEnabled"
                      (click)="toggleWebhooks(user)"
                      [disabled]="isTogglingWebhooks() === user.id"
                      [attr.aria-pressed]="user.webhooksEnabled"
                      aria-label="Hooks page access"
                      [title]="
                        user.webhooksEnabled ? 'Hide the Hooks page from this user' : 'Let this user see the Hooks page'
                      "
                    >
                      <svg lucideIcon="webhook" size="14"></svg>
                    </button>
                  }
                  <button
                    type="button"
                    class="icon-btn icon-btn-sm"
                    (click)="resetPassword(user)"
                    [disabled]="isResettingPassword() === user.id"
                    aria-label="Reset password"
                    title="Reset password"
                  >
                    <svg lucideIcon="lock" size="14"></svg>
                  </button>
                  @if (currentUserId() !== user.id) {
                    <button
                      type="button"
                      class="icon-btn icon-btn-sm icon-btn-danger"
                      (click)="deleteUser(user.id)"
                      [disabled]="isDeletingUser() === user.id"
                      aria-label="Delete user"
                      title="Delete user"
                    >
                      <svg lucideIcon="trash-2" size="14"></svg>
                    </button>
                  }
                }
              </div>
            </div>
          } @empty {
            <div class="data-table-empty">No users found.</div>
          }
        }
      </div>
    </section>
  `,
  styleUrl: './user-management.component.scss',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class UserManagementComponent {
  private userManagementService = inject(UserManagementService);
  private authService = inject(AuthService);
  private confirmDialog = inject(ConfirmDialogService);
  private modalContainerService = inject(ModalContainerService);

  users = signal<User[]>([]);
  isLoading = signal(false);
  isCreatingUser = signal(false);
  editingRoleUserId = signal<string | null>(null);
  isResettingPassword = signal<string | null>(null);
  isDeletingUser = signal<string | null>(null);
  isTogglingWebhooks = signal<string | null>(null);
  readonly Role = Role;
  initials = initialsFor;
  errorMessage = signal('');
  successMessage = signal('');
  currentUserId = signal<string | null>(null);

  createUserModel = signal<{ username: string; password: string; subdomain: string; role: string }>({
    username: '',
    password: '',
    subdomain: '',
    role: '0',
  });
  createUserForm = form(this.createUserModel, (path) => {
    required(path.username, { message: 'Username is required' });
    minLength(path.username, 3, { message: 'Username must be at least 3 characters' });
    required(path.password, { message: 'Password is required' });
    minLength(path.password, MIN_PASSWORD_LENGTH, {
      message: `Password must be at least ${MIN_PASSWORD_LENGTH} characters`,
    });
    pattern(path.subdomain, /^[a-z0-9-]*$/, { message: 'Subdomain has an invalid format' });
    required(path.role, { message: 'Role is required' });
  });

  constructor() {
    this.loadUsers();
    this.getCurrentUserId();
  }

  private getCurrentUserId() {
    this.currentUserId.set(this.authService.getCurrentUserId());
  }

  async loadUsers() {
    this.isLoading.set(true);
    this.clearMessages();

    try {
      const users = await this.userManagementService.getAllUsers();
      this.users.set(users);
    } catch (error) {
      this.errorMessage.set('Failed to load users');
    } finally {
      this.isLoading.set(false);
    }
  }

  async createUser(event: Event) {
    event.preventDefault();
    if (this.createUserForm().invalid()) return;

    this.isCreatingUser.set(true);
    this.clearMessages();

    const formValue = this.createUserModel();
    const request: CreateUserRequest = {
      userName: formValue.username,
      password: formValue.password,
      role: Number(formValue.role) as Role,
      subdomain: formValue.subdomain || undefined,
    };

    try {
      await this.userManagementService.createUser(request);
      this.successMessage.set('User created successfully');
      this.createUserModel.set({ username: '', password: '', subdomain: '', role: '0' });
      await this.loadUsers();
    } catch (error: any) {
      this.errorMessage.set(error.message || 'Failed to create user');
    } finally {
      this.isCreatingUser.set(false);
    }
  }

  editRole(userId: string) {
    this.editingRoleUserId.set(userId);
  }

  cancelRoleEdit() {
    this.editingRoleUserId.set(null);
  }

  onRoleChange(event: Event, userId: string) {
    const value = (event.target as HTMLSelectElement).value;
    const role = Number(value) as Role;
    this.updateUserRole(userId, role);
  }

  async updateUserRole(userId: string, role: Role | null | undefined) {
    if (role === null || role === undefined) {
      this.errorMessage.set('Invalid role selected');
      return;
    }
    this.clearMessages();

    try {
      const request: UpdateUserRoleRequest = { role };
      await this.userManagementService.updateUserRole(userId, request);
      this.successMessage.set('User role updated successfully');
      this.editingRoleUserId.set(null);
      await this.loadUsers();
    } catch (error: any) {
      this.errorMessage.set(error.message || 'Failed to update user role');
    }
  }

  async toggleWebhooks(user: User) {
    this.isTogglingWebhooks.set(user.id);
    this.clearMessages();
    try {
      await this.userManagementService.updateUserWebhooks(user.id, !user.webhooksEnabled);
      this.successMessage.set(user.webhooksEnabled ? 'Hooks disabled for user' : 'Hooks enabled for user');
      await this.loadUsers();
    } catch (error: any) {
      this.errorMessage.set(error.message || 'Failed to update webhook access');
    } finally {
      this.isTogglingWebhooks.set(null);
    }
  }

  resetPassword(user: User) {
    // Masked password + confirmation in a modal; the dialog enforces the minimum length.
    ResetPasswordDialogComponent.show(this.modalContainerService, user.userName).subscribe((newPassword) => {
      if (newPassword) {
        this.applyPasswordReset(user.id, newPassword);
      }
    });
  }

  private async applyPasswordReset(userId: string, newPassword: string) {
    this.isResettingPassword.set(userId);
    this.clearMessages();

    try {
      await this.userManagementService.updateUserPassword(userId, { newPassword });
      this.successMessage.set('Password reset successfully');
    } catch (error: any) {
      this.errorMessage.set(error.message || 'Failed to reset password');
    } finally {
      this.isResettingPassword.set(null);
    }
  }

  deleteUser(userId: string) {
    this.confirmDialog.confirmDelete('this user').subscribe(async (confirmed) => {
      if (!confirmed) return;

      this.isDeletingUser.set(userId);
      this.clearMessages();

      try {
        await this.userManagementService.deleteUser(userId);
        this.successMessage.set('User deleted successfully');
        await this.loadUsers();
      } catch (error: any) {
        this.errorMessage.set(error.message || 'Failed to delete user');
      } finally {
        this.isDeletingUser.set(null);
      }
    });
  }

  getRoleDisplayName(role: Role): string {
    return role === Role.Admin ? 'Admin' : 'User';
  }

  formatDate(date: string): string {
    return new Date(date).toLocaleDateString();
  }

  private clearMessages() {
    this.errorMessage.set('');
    this.successMessage.set('');
  }
}
