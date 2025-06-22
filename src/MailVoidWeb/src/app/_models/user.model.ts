export interface User {
  id: string;
  userName: string;
  role: Role;
  timeStamp: string;
}

export enum Role {
  User = 0,
  Admin = 1,
}

export interface CreateUserRequest {
  userName: string;
  password: string;
  role: Role;
}

export interface UpdateUserRoleRequest {
  role: Role;
}

export interface UpdateUserPasswordRequest {
  newPassword: string;
}
