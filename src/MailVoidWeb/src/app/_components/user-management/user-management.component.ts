import { ChangeDetectionStrategy, Component, inject, signal } from '@angular/core';
import { form, required, minLength, pattern, FormField } from '@angular/forms/signals';
import { ConfirmDialogService, ModalContainerService } from '@rd-ui';
import { CreateUserRequest, MIN_PASSWORD_LENGTH, Role, UpdateUserRoleRequest, User } from '../../_models/user.model';
import { AuthService } from '../../_services/auth-service';
import { UserManagementService } from '../../_services/user-management.service';
import { ResetPasswordDialogComponent } from './reset-password-dialog/reset-password-dialog.component';

@Component({
  selector: 'app-user-management',
  imports: [FormField],
  template: `
    <div class="user-management">
      <!-- Create User Form -->
      <div class="create-user-section">
        <h4>Create New User</h4>
        <form (submit)="createUser($event)" class="create-form">
          <div class="form-row">
            <div class="form-group">
              <label for="username">Username</label>
              <input
                id="username"
                type="text"
                [formField]="createUserForm.username"
                class="form-control"
                placeholder="Enter username"
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
                autocomplete="new-password"
                placeholder="At least 8 characters"
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
                placeholder="e.g., sales, support"
              />
            </div>
            <div class="form-group">
              <label for="role">Role</label>
              <select id="role" [formField]="createUserForm.role" class="form-control">
                <option value="0">User</option>
                <option value="1">Admin</option>
              </select>
            </div>
            <div class="form-group">
              <button type="submit" class="btn btn-primary" [disabled]="createUserForm().invalid() || isCreatingUser()">
                {{ isCreatingUser() ? 'Creating...' : 'Create User' }}
              </button>
            </div>
          </div>
        </form>
      </div>

      <!-- Users List -->
      <div class="users-section">
        <div class="section-header">
          <h4>Manage Users</h4>
          <button class="btn btn-secondary" (click)="loadUsers()">Refresh</button>
        </div>

        @if (isLoading()) {
          <div class="loading">Loading users...</div>
        } @else if (users().length === 0) {
          <div class="no-users">No users found</div>
        } @else {
          <div class="users-table">
            <div class="table-grid header">
              <div class="header-cell">Username</div>
              <div class="header-cell">Subdomain</div>
              <div class="header-cell">Role</div>
              <div class="header-cell">Created</div>
              <div class="header-cell">Actions</div>
            </div>
            @for (user of users(); track user.id) {
              <div class="table-grid row">
                <div class="cell username" data-label="Username">{{ user.userName }}</div>
                <div class="cell subdomain" data-label="Subdomain">
                  @if (user.subdomain) {
                    <span class="subdomain-badge">{{ user.subdomain }}</span>
                  } @else {
                    <span class="text-muted">--</span>
                  }
                </div>
                <div class="cell role" data-label="Role">
                  @if (editingRoleUserId() === user.id) {
                    <select class="role-select" [value]="user.role" (change)="onRoleChange($event, user.id)">
                      <option value="0">User</option>
                      <option value="1">Admin</option>
                    </select>
                  } @else {
                    <span class="role-badge" [class]="getRoleClass(user.role)">
                      {{ getRoleDisplayName(user.role) }}
                    </span>
                    @if (user.role !== Role.Admin && user.webhooksEnabled) {
                      <span class="badge badge-primary" title="Can use the Hooks page">Hooks</span>
                    }
                  }
                </div>
                <div class="cell timestamp" data-label="Created">{{ formatDate(user.timeStamp) }}</div>
                <div class="cell actions" data-label="Actions">
                  @if (editingRoleUserId() === user.id) {
                    <button class="btn btn-sm btn-secondary" (click)="cancelRoleEdit()">Cancel</button>
                  } @else {
                    <button class="btn btn-sm btn-outline" (click)="editRole(user.id)">Edit Role</button>
                    @if (user.role !== Role.Admin) {
                      <button
                        class="btn btn-sm btn-outline"
                        (click)="toggleWebhooks(user)"
                        [disabled]="isTogglingWebhooks() === user.id"
                        [title]="
                          user.webhooksEnabled
                            ? 'Hide the Hooks page from this user'
                            : 'Let this user see the Hooks page'
                        "
                      >
                        {{ user.webhooksEnabled ? 'Disable Hooks' : 'Enable Hooks' }}
                      </button>
                    }
                    <button
                      class="btn btn-sm btn-outline"
                      (click)="resetPassword(user)"
                      [disabled]="isResettingPassword() === user.id"
                    >
                      {{ isResettingPassword() === user.id ? 'Resetting...' : 'Reset Password' }}
                    </button>
                    @if (currentUserId() !== user.id) {
                      <button
                        class="btn btn-sm btn-danger"
                        (click)="deleteUser(user.id)"
                        [disabled]="isDeletingUser() === user.id"
                      >
                        {{ isDeletingUser() === user.id ? 'Deleting...' : 'Delete' }}
                      </button>
                    }
                  }
                </div>
              </div>
            }
          </div>
        }
      </div>

      @if (errorMessage()) {
        <div class="message message-error">{{ errorMessage() }}</div>
      }

      @if (successMessage()) {
        <div class="message message-success">{{ successMessage() }}</div>
      }
    </div>
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

  getRoleClass(role: Role): string {
    return role === Role.Admin ? 'role-admin' : 'role-user';
  }

  formatDate(date: string): string {
    return new Date(date).toLocaleDateString();
  }

  private clearMessages() {
    this.errorMessage.set('');
    this.successMessage.set('');
  }
}
