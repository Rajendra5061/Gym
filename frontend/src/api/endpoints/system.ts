/**
 * System administration endpoints: user accounts, roles and permissions, the audit trail,
 * the recycle bin and the settings/licence/backup surface.
 *
 * Every call here is server-side paged and filtered; nothing is fetched wholesale and sliced
 * in the browser.
 */

import { api, ApiError } from '@/api/client';
import type { GymSettings, Lookup, PagedResult, UserStatus } from '@/api/types';

/* ------------------------------------------------------------------ users */

export interface UserListDto {
  id: number;
  userName: string;
  fullName: string;
  email: string;
  phone?: string | null;
  status: UserStatus;
  statusText: string;
  /** Comma separated role names, as the API flattens them for the grid. */
  roles: string;
  lastLoginAtUtc?: string | null;
  createdAt: string;
  isLockedOut: boolean;
}

export interface UserDetailDto extends UserListDto {
  memberId?: number | null;
  trainerId?: number | null;
  mustChangePassword: boolean;
  failedLoginAttempts: number;
  lockoutEndUtc?: string | null;
  profilePhotoPath?: string | null;
  roleIds: number[];
}

export interface CreateUserInput {
  userName: string;
  fullName: string;
  email: string;
  phone?: string | null;
  /** Left empty so the server generates — and returns — a temporary password. */
  password?: string | null;
  mustChangePassword: boolean;
  roleIds: number[];
  memberId?: number | null;
  trainerId?: number | null;
  status: UserStatus;
}

export interface UpdateUserInput {
  fullName: string;
  email: string;
  phone?: string | null;
  status: UserStatus;
  roleIds: number[];
}

export interface TemporaryPasswordDto {
  userId: number;
  userName: string;
  temporaryPassword: string;
}

export interface UserQuery {
  pageNumber?: number;
  pageSize?: number;
  search?: string;
  sortBy?: string;
  sortDescending?: boolean;
  status?: UserStatus | '';
  roleId?: number | '';
  includeDeleted?: boolean;
}

export const usersApi = {
  paged: (query: UserQuery, signal?: AbortSignal) =>
    api.get<PagedResult<UserListDto>>('/api/users', { ...query }, signal),

  byId: (id: number, signal?: AbortSignal) =>
    api.get<UserDetailDto>(`/api/users/${id}`, undefined, signal),

  create: (body: CreateUserInput) =>
    api.post<TemporaryPasswordDto>('/api/users', body),

  /**
   * The id has to travel in the body as well as the route. UpdateUserDto is validated during
   * model binding, before the controller copies the route id onto it, so a body without `id`
   * fails with "Select the user you want to update."
   */
  update: (id: number, body: UpdateUserInput) =>
    api.put<UserDetailDto>(`/api/users/${id}`, { ...body, id }),

  /** Returns the freshly generated password so the operator can hand it over. */
  resetPassword: (id: number) =>
    api.post<TemporaryPasswordDto>(`/api/users/${id}/reset-password`),

  /** The endpoint binds a bare enum value from the body, not an object. */
  setStatus: (id: number, status: UserStatus) =>
    api.post<void>(`/api/users/${id}/status`, status),

  unlock: (id: number) => api.post<void>(`/api/users/${id}/unlock`),

  remove: (id: number) => api.del<void>(`/api/users/${id}`),

  lookup: (signal?: AbortSignal) =>
    api.get<Lookup[]>('/api/users/lookup', undefined, signal),
};

/* ------------------------------------------------------------------ roles */

export interface RoleDto {
  id: number;
  name: string;
  description?: string | null;
  isSystemRole: boolean;
  userCount: number;
  permissions: string[];
}

export interface SaveRoleInput {
  name: string;
  description?: string | null;
  permissions: string[];
}

export interface PermissionDto {
  id: number;
  code: string;
  module: string;
  description: string;
}

export interface PermissionGroupDto {
  module: string;
  permissions: PermissionDto[];
}

export const rolesApi = {
  all: (signal?: AbortSignal) => api.get<RoleDto[]>('/api/roles', undefined, signal),

  byId: (id: number, signal?: AbortSignal) =>
    api.get<RoleDto>(`/api/roles/${id}`, undefined, signal),

  create: (body: SaveRoleInput) => api.post<RoleDto>('/api/roles', body),

  update: (id: number, body: SaveRoleInput) => api.put<RoleDto>(`/api/roles/${id}`, body),

  remove: (id: number) => api.del<void>(`/api/roles/${id}`),

  permissionGroups: (signal?: AbortSignal) =>
    api.get<PermissionGroupDto[]>('/api/roles/permissions', undefined, signal),

  /** Replaces the whole grant list; the body is a bare array of permission codes. */
  setPermissions: (id: number, codes: string[]) =>
    api.put<void>(`/api/roles/${id}/permissions`, codes),
};

/* ------------------------------------------------------------------ audit */

export interface AuditLogDto {
  id: number;
  userId?: number | null;
  userName?: string | null;
  action: string;
  entityName: string;
  entityId?: number | null;
  oldValues?: string | null;
  newValues?: string | null;
  description?: string | null;
  ipAddress?: string | null;
  deviceInfo?: string | null;
  changedAtUtc: string;
}

export interface LoginAttemptDto {
  id: number;
  userId?: number | null;
  userNameOrEmail: string;
  result: number;
  resultText: string;
  attemptedAtUtc: string;
  ipAddress?: string | null;
  deviceInfo?: string | null;
  failureReason?: string | null;
}

export interface AuditQuery {
  pageNumber?: number;
  pageSize?: number;
  search?: string;
  sortBy?: string;
  sortDescending?: boolean;
  userId?: number | '';
  action?: string;
  entityName?: string;
  entityId?: number | '';
  fromDate?: string;
  toDate?: string;
}

export interface PagedQuery {
  pageNumber?: number;
  pageSize?: number;
  search?: string;
  sortBy?: string;
  sortDescending?: boolean;
}

export const auditApi = {
  paged: (query: AuditQuery, signal?: AbortSignal) =>
    api.get<PagedResult<AuditLogDto>>('/api/audit', { ...query }, signal),

  loginAttempts: (query: PagedQuery, signal?: AbortSignal) =>
    api.get<PagedResult<LoginAttemptDto>>('/api/audit/login-attempts', { ...query }, signal),

  actions: (signal?: AbortSignal) =>
    api.get<string[]>('/api/audit/actions', undefined, signal),

  entities: (signal?: AbortSignal) =>
    api.get<string[]>('/api/audit/entities', undefined, signal),
};

/* ------------------------------------------------------------ recycle bin */

export interface RecycleBinItemDto {
  entityName: string;
  entityId: number;
  displayName: string;
  details?: string | null;
  deletedAt?: string | null;
  deletedBy?: number | null;
  deletedByName?: string | null;
}

export interface RecycleBinQuery {
  pageNumber?: number;
  pageSize?: number;
  search?: string;
  sortBy?: string;
  sortDescending?: boolean;
  entityName?: string;
  fromDate?: string;
  toDate?: string;
}

/** The phrase the API demands before it will permanently destroy anything. */
export const PURGE_CONFIRMATION = 'PERMANENTLY DELETE';

export const recycleBinApi = {
  paged: (query: RecycleBinQuery, signal?: AbortSignal) =>
    api.get<PagedResult<RecycleBinItemDto>>('/api/recycle-bin', { ...query }, signal),

  /** `extra` carries the number of deleted rows currently held for that entity. */
  entityTypes: (signal?: AbortSignal) =>
    api.get<Lookup[]>('/api/recycle-bin/entity-types', undefined, signal),

  restore: (entityName: string, entityIds: number[]) =>
    api.post<number>('/api/recycle-bin/restore', { entityName, entityIds }),

  purge: (entityName: string, entityIds: number[], confirmationText: string) =>
    api.post<number>('/api/recycle-bin/purge', { entityName, entityIds, confirmationText }),
};

/* --------------------------------------------------------------- settings */

/**
 * The wire shape of the gym profile behind `settings.view`. `GymBranding` in `types.ts` covers
 * what the shell needs and is readable by everyone; the settings screen edits the whole record,
 * so the extra columns are declared here and round-tripped untouched when they are not on the form.
 */
export interface GymSettingsFull extends GymSettings {
  legalName?: string | null;
  postalCode?: string | null;
  country?: string | null;
  upiQrImagePath?: string | null;
  /** TimeSpan over the wire: "HH:mm:ss". */
  openingTime: string;
  closingTime: string;
}

export interface SystemSettingDto {
  id: number;
  key: string;
  value?: string | null;
  dataType: string;
  category: string;
  description?: string | null;
  isReadOnly: boolean;
}

export interface LicenseStatusDto {
  status: number;
  statusText: string;
  licenseKey?: string | null;
  maskedLicenseKey?: string | null;
  customerName: string;
  gymIdentifier: string;
  isTrial: boolean;
  startDateUtc?: string | null;
  expiryDateUtc?: string | null;
  daysRemaining: number;
  isValid: boolean;
  isExpiringSoon: boolean;
  maxMembers?: number | null;
  currentMembers: number;
  maxUsers?: number | null;
  currentUsers: number;
  enabledFeatures: string[];
  message?: string | null;
  clockTamperDetected: boolean;
}

export interface BackupRecordDto {
  id: number;
  fileName: string;
  filePath: string;
  fileSizeBytes: number;
  fileSizeText: string;
  createdAtUtc: string;
  createdByName?: string | null;
  backupType: string;
  isSuccess: boolean;
  errorMessage?: string | null;
  restoredAtUtc?: string | null;
  notes?: string | null;
}

/* ----------------------------------------------- email & payment gateway */

/**
 * How mail is actually configured on the server that answered, as resolved at start-up from the
 * `Email` configuration section (appsettings / environment variables), never from the database.
 *
 * There is deliberately no password member and never will be: the SMTP password is supplied
 * through `Email__Smtp__Password` and the API does not return it. `smtpCredentialsConfigured`
 * says only *whether* one resolved.
 */
export interface EmailStatusDto {
  /** `None`, `File` or `Smtp` — the sender that was actually built. */
  provider: string;
  /** False for the null sender: every message is accepted and discarded. */
  isEnabled: boolean;
  fromAddress?: string | null;
  fromName?: string | null;
  replyToAddress?: string | null;
  smtpHost?: string | null;
  smtpPort?: number | null;
  smtpUseStartTls?: boolean | null;
  smtpUserName?: string | null;
  /** Whether a password resolved from configuration. Never the value. */
  smtpCredentialsConfigured?: boolean | null;
  /** Absolute path of the mail-drop folder, when the provider is `File`. */
  fileSinkDirectory?: string | null;
  /** Why sending is off, when the server knows — e.g. a provider that could not be built. */
  reason?: string | null;
}

/** What the server did with a test message. */
export interface EmailTestResultDto {
  sent: boolean;
  provider?: string | null;
  toAddress?: string | null;
  /** Where it landed: the mail-drop file for `File`, `host:port` for `Smtp`. */
  destination?: string | null;
  /** Why it was skipped, when it was. */
  reason?: string | null;
}

/**
 * Whether an online payment gateway is wired up, and where its webhook should point.
 *
 * The signing secret is not a member of this type. `signingSecretConfigured` reports only that
 * one is present, which is all an operator needs to see on a screen.
 */
export interface PaymentGatewayStatusDto {
  isConfigured: boolean;
  provider?: string | null;
  /** `Test` / `Live`, when the provider distinguishes them. */
  mode?: string | null;
  /** Absolute URL to hand the provider. Preferred over `webhookPath`. */
  webhookUrl?: string | null;
  /** Server-relative path, when the server cannot know its own public origin. */
  webhookPath?: string | null;
  signingSecretConfigured?: boolean;
  /** Mirrors `UpiPaymentIntentDto.requiresManualVerification`. */
  requiresManualVerification?: boolean;
  lastEventAtUtc?: string | null;
  message?: string | null;
}

/**
 * Runs a call whose endpoint may not exist on the server that answered, and reports "no such
 * endpoint" as `null` instead of as a failure.
 *
 * The email and gateway surfaces below are read by a screen whose whole job is to explain the
 * current state calmly. An API build without them is a state to describe, not an error to shout
 * about — but a 403 or a 500 still is, so only the status codes that mean "this route is not
 * here" are swallowed.
 */
async function optional<T>(call: () => Promise<T>): Promise<T | null> {
  try {
    return await call();
  } catch (e) {
    if (e instanceof ApiError && (e.status === 404 || e.status === 405 || e.status === 501)) return null;
    throw e;
  }
}

export const settingsApi = {
  gym: (signal?: AbortSignal) =>
    api.get<GymSettingsFull>('/api/settings/gym', undefined, signal),

  saveGym: (body: GymSettingsFull) =>
    api.put<GymSettingsFull>('/api/settings/gym', body),

  system: (category?: string, signal?: AbortSignal) =>
    api.get<SystemSettingDto[]>('/api/settings/system', { category }, signal),

  saveSystem: (body: SystemSettingDto) =>
    api.put<SystemSettingDto>('/api/settings/system', body),

  license: (signal?: AbortSignal) =>
    api.get<LicenseStatusDto>('/api/settings/license', undefined, signal),

  startTrial: (customerName: string, gymIdentifier?: string) =>
    api.post<LicenseStatusDto>('/api/settings/license/trial', { customerName, gymIdentifier }),

  activateLicense: (licenseKey: string, customerName?: string) =>
    api.post<LicenseStatusDto>('/api/settings/license/activate', { licenseKey, customerName }),

  backups: (signal?: AbortSignal) =>
    api.get<BackupRecordDto[]>('/api/settings/backups', undefined, signal),

  createBackup: (notes?: string) =>
    api.post<BackupRecordDto>('/api/settings/backups', { notes }),

  /** `confirmationText` must be the exact database name. */
  restoreBackup: (backupId: number, confirmationText: string) =>
    api.post<BackupRecordDto>('/api/settings/backups/restore', { backupId, confirmationText }),

  deleteBackup: (id: number, deleteFile = false) =>
    api.del<void>(`/api/settings/backups/${id}`, { deleteFile }),

  lookups: (signal?: AbortSignal) =>
    api.get<Record<string, Lookup[]>>('/api/settings/lookups', undefined, signal),

  enum: (enumName: string, signal?: AbortSignal) =>
    api.get<Lookup[]>(`/api/settings/enums/${enumName}`, undefined, signal),

  /* The three below resolve to `null` when the API build has no such route — see `optional`. */

  /** Effective mail configuration. Carries no password. */
  emailStatus: (signal?: AbortSignal) =>
    optional(() => api.get<EmailStatusDto>('/api/settings/email', undefined, signal)),

  /** Sends one message through whatever provider is live, so the operator can see where it goes. */
  sendTestEmail: (toAddress: string) =>
    optional(() => api.post<EmailTestResultDto>('/api/settings/email/test', { toAddress })),

  /** Gateway wiring for online payments. Carries no signing secret. */
  paymentGateway: (signal?: AbortSignal) =>
    optional(() => api.get<PaymentGatewayStatusDto>('/api/payments/gateway', undefined, signal)),
};
