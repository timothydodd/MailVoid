export interface User {
  id: string;
  userName: string;
  role: Role;
  timeStamp: string;
  subdomain: string | null;
}

export enum Role {
  User = 0,
  Admin = 1,
}

export interface CreateUserRequest {
  userName: string;
  password: string;
  role: Role;
  subdomain?: string;
}

export interface UpdateUserRoleRequest {
  role: Role;
}

export interface UpdateUserPasswordRequest {
  newPassword: string;
}

// Minimum length for new passwords; matches the API's check (create user, change
// password, admin reset), which returns 400 for anything shorter.
export const MIN_PASSWORD_LENGTH = 8;
