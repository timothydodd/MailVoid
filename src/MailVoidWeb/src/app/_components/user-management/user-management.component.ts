import { CommonModule } from '@angular/common';
import { ChangeDetectionStrategy, Component, inject, signal } from '@angular/core';
import { FormBuilder, FormGroup, ReactiveFormsModule, Validators } from '@angular/forms';
import { CreateUserRequest, Role, UpdateUserRoleRequest, User } from '../../_models/user.model';
import { AuthService } from '../../_services/auth-service';
import { UserManagementService } from '../../_services/user-management.service';

@Component({
  selector: 'app-user-management',
  imports: [CommonModule, ReactiveFormsModule],
  template: `
    <div class="user-management">
      <!-- Create User Form -->
      <div class="create-user-section">
        <h4>Create New User</h4>
        <form [formGroup]="createUserForm" (ngSubmit)="createUser()" class="create-form">
          <div class="form-row">
            <div class="form-group">
              <label for="username">Username</label>
              <input
                id="username"
                type="text"
                formControlName="username"
                class="form-control"
                placeholder="Enter username"
              />
            </div>
            <div class="form-group">
              <label for="password">Password</label>
              <input
                id="password"
                type="password"
                formControlName="password"
                class="form-control"
                placeholder="Enter password"
              />
            </div>
            <div class="form-group">
              <label for="role">Role</label>
              <select id="role" formControlName="role" class="form-control">
                <option value="0">User</option>
                <option value="1">Admin</option>
              </select>
            </div>
            <div class="form-group">
              <button type="submit" class="btn btn-primary" [disabled]="createUserForm.invalid || isCreatingUser()">
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
            <div class="table-header">
              <div class="header-cell">Username</div>
              <div class="header-cell">Role</div>
              <div class="header-cell">Created</div>
              <div class="header-cell">Actions</div>
            </div>
            @for (user of users(); track user.id) {
              <div class="table-row">
                <div class="cell username">{{ user.userName }}</div>
                <div class="cell role">
                  @if (editingRoleUserId() === user.id) {
                    <select class="role-select" [value]="user.role" (change)="onRoleChange($event, user.id)">
                      <option value="0">User</option>
                      <option value="1">Admin</option>
                    </select>
                  } @else {
                    <span class="role-badge" [class]="getRoleClass(user.role)">
                      {{ getRoleDisplayName(user.role) }}
                    </span>
                  }
                </div>
                <div class="cell timestamp">{{ formatDate(user.timeStamp) }}</div>
                <div class="cell actions">
                  @if (editingRoleUserId() === user.id) {
                    <button class="btn btn-sm btn-secondary" (click)="cancelRoleEdit()">Cancel</button>
                  } @else {
                    <button class="btn btn-sm btn-outline" (click)="editRole(user.id)">Edit Role</button>
                    <button
                      class="btn btn-sm btn-outline"
                      (click)="resetPassword(user.id)"
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
        <div class="error-message">{{ errorMessage() }}</div>
      }

      @if (successMessage()) {
        <div class="success-message">{{ successMessage() }}</div>
      }
    </div>
  `,
  styleUrl: './user-management.component.scss',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class UserManagementComponent {
  private userManagementService = inject(UserManagementService);
  private authService = inject(AuthService);
  private fb = inject(FormBuilder);

  users = signal<User[]>([]);
  isLoading = signal(false);
  isCreatingUser = signal(false);
  editingRoleUserId = signal<string | null>(null);
  isResettingPassword = signal<string | null>(null);
  isDeletingUser = signal<string | null>(null);
  errorMessage = signal('');
  successMessage = signal('');
  currentUserId = signal<string | null>(null);

  createUserForm: FormGroup;

  constructor() {
    this.createUserForm = this.fb.group({
      username: ['', [Validators.required, Validators.minLength(3)]],
      password: ['', [Validators.required, Validators.minLength(6)]],
      role: [0, Validators.required],
    });

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

  async createUser() {
    if (this.createUserForm.invalid) return;

    this.isCreatingUser.set(true);
    this.clearMessages();

    const formValue = this.createUserForm.value;
    const request: CreateUserRequest = {
      userName: formValue.username,
      password: formValue.password,
      role: formValue.role,
    };

    try {
      await this.userManagementService.createUser(request);
      this.successMessage.set('User created successfully');
      this.createUserForm.reset({ role: 0 });
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

  async resetPassword(userId: string) {
    const newPassword = prompt('Enter new password for this user:');
    if (!newPassword || newPassword.length < 6) {
      this.errorMessage.set('Password must be at least 6 characters long');
      return;
    }

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

  async deleteUser(userId: string) {
    const confirmed = confirm('Are you sure you want to delete this user? This action cannot be undone.');
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
