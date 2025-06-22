import { HttpClient } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { firstValueFrom } from 'rxjs';
import { environment } from '../../environments/environment';
import { CreateUserRequest, UpdateUserPasswordRequest, UpdateUserRoleRequest, User } from '../_models/user.model';

@Injectable({
  providedIn: 'root',
})
export class UserManagementService {
  private http = inject(HttpClient);
  private baseUrl = `${environment.apiUrl}/api/UserManagement`;

  async getAllUsers(): Promise<User[]> {
    try {
      return await firstValueFrom(this.http.get<User[]>(this.baseUrl));
    } catch (error: any) {
      throw new Error(error.error?.message || 'Failed to load users');
    }
  }

  async getUserById(id: string): Promise<User> {
    try {
      return await firstValueFrom(this.http.get<User>(`${this.baseUrl}/${id}`));
    } catch (error: any) {
      throw new Error(error.error?.message || 'Failed to load user');
    }
  }

  async createUser(request: CreateUserRequest): Promise<User> {
    try {
      return await firstValueFrom(this.http.post<User>(this.baseUrl, request));
    } catch (error: any) {
      if (error.status === 400) {
        throw new Error(error.error || 'Invalid user data');
      }
      throw new Error(error.error?.message || 'Failed to create user');
    }
  }

  async updateUserRole(id: string, request: UpdateUserRoleRequest): Promise<void> {
    try {
      await firstValueFrom(this.http.put<void>(`${this.baseUrl}/${id}/role`, request));
    } catch (error: any) {
      if (error.status === 403) {
        throw new Error('You do not have permission to update user roles');
      }
      if (error.status === 404) {
        throw new Error('User not found');
      }
      throw new Error(error.error?.message || 'Failed to update user role');
    }
  }

  async updateUserPassword(id: string, request: UpdateUserPasswordRequest): Promise<void> {
    try {
      await firstValueFrom(this.http.put<void>(`${this.baseUrl}/${id}/password`, request));
    } catch (error: any) {
      if (error.status === 403) {
        throw new Error('You do not have permission to update this password');
      }
      if (error.status === 404) {
        throw new Error('User not found');
      }
      throw new Error(error.error?.message || 'Failed to update password');
    }
  }

  async deleteUser(id: string): Promise<void> {
    try {
      await firstValueFrom(this.http.delete<void>(`${this.baseUrl}/${id}`));
    } catch (error: any) {
      if (error.status === 403) {
        throw new Error('You do not have permission to delete users');
      }
      if (error.status === 404) {
        throw new Error('User not found');
      }
      if (error.status === 400) {
        throw new Error(error.error || 'Cannot delete this user');
      }
      throw new Error(error.error?.message || 'Failed to delete user');
    }
  }
}
