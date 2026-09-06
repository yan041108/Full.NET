export interface LdapConnection {
  id: string;
  tenantId: string | null;
  name: string;
  host: string;
  port: number;
  useTls: boolean;
  baseDn: string;
  bindDn: string;
  userSearchFilter: string;
  userAccountAttribute: string;
  employeeIdAttribute: string | null;
  departmentCodeAttribute: string | null;
  syncSearchBaseDn: string;
  isEnabled: boolean;
  createdAtUtc: string;
  updatedAtUtc: string | null;
  version: number;
}

export interface CreateLdapConnectionRequest {
  tenantId?: string | null;
  name: string;
  host: string;
  port: number;
  useTls: boolean;
  baseDn: string;
  bindDn: string;
  bindPassword: string;
  userSearchFilter: string;
  userAccountAttribute: string;
  employeeIdAttribute?: string | null;
  departmentCodeAttribute?: string | null;
  syncSearchBaseDn: string;
  isEnabled: boolean;
}

export interface UpdateLdapConnectionRequest {
  name: string;
  host: string;
  port: number;
  useTls: boolean;
  baseDn: string;
  bindDn: string;
  bindPassword?: string | null;
  userSearchFilter: string;
  userAccountAttribute: string;
  employeeIdAttribute?: string | null;
  departmentCodeAttribute?: string | null;
  syncSearchBaseDn: string;
  isEnabled: boolean;
  version: number;
}

export interface LdapConnectionPage {
  items: LdapConnection[];
  page: number;
  pageSize: number;
  total: number;
}

export interface LdapConnectionListQuery {
  page?: number;
  pageSize?: number;
  tenantId?: string;
  nameContains?: string;
  isEnabled?: boolean;
}

export interface TestLdapConnectionResult {
  succeeded: boolean;
  message: string;
}

export interface TestLdapAuthenticationRequest {
  account: string;
  password: string;
}

export interface TestLdapAuthenticationResult {
  succeeded: boolean;
  matchedDn: string | null;
  message: string;
}

export interface PreviewLdapSyncRequest {
  searchBaseDn?: string | null;
  maxEntries?: number | null;
}

export interface LdapSyncPreviewEntry {
  dn: string;
  entryKind: 'user' | 'organizationalUnit' | string;
  account: string | null;
  displayName: string | null;
  mail: string | null;
  departmentCode: string | null;
}

export interface PreviewLdapSyncResponse {
  searchBaseDn: string;
  entries: LdapSyncPreviewEntry[];
}

const guidPattern = /^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/i;

export function isLdapConnection(value: unknown): value is LdapConnection {
  return isRecord(value)
    && isGuid(value.id)
    && (value.tenantId === null || isGuid(value.tenantId))
    && typeof value.name === 'string'
    && typeof value.host === 'string'
    && Number.isInteger(value.port)
    && typeof value.useTls === 'boolean'
    && typeof value.baseDn === 'string'
    && typeof value.bindDn === 'string'
    && typeof value.userSearchFilter === 'string'
    && typeof value.userAccountAttribute === 'string'
    && (value.employeeIdAttribute === null || typeof value.employeeIdAttribute === 'string')
    && (value.departmentCodeAttribute === null || typeof value.departmentCodeAttribute === 'string')
    && typeof value.syncSearchBaseDn === 'string'
    && typeof value.isEnabled === 'boolean'
    && typeof value.createdAtUtc === 'string'
    && (value.updatedAtUtc === null || typeof value.updatedAtUtc === 'string')
    && Number.isInteger(value.version);
}

export function isLdapConnectionPage(value: unknown): value is LdapConnectionPage {
  return isRecord(value)
    && Array.isArray(value.items)
    && value.items.every(isLdapConnection)
    && Number.isInteger(value.page)
    && Number.isInteger(value.pageSize)
    && Number.isInteger(value.total);
}

export function isTestLdapConnectionResult(value: unknown): value is TestLdapConnectionResult {
  return isRecord(value)
    && typeof value.succeeded === 'boolean'
    && typeof value.message === 'string';
}

export function isTestLdapAuthenticationResult(value: unknown): value is TestLdapAuthenticationResult {
  return isRecord(value)
    && typeof value.succeeded === 'boolean'
    && (value.matchedDn === null || typeof value.matchedDn === 'string')
    && typeof value.message === 'string';
}

export function isPreviewLdapSyncResponse(value: unknown): value is PreviewLdapSyncResponse {
  return isRecord(value)
    && typeof value.searchBaseDn === 'string'
    && Array.isArray(value.entries);
}

function isRecord(value: unknown): value is Record<string, unknown> {
  return typeof value === 'object' && value !== null && !Array.isArray(value);
}

function isGuid(value: unknown): value is string {
  return typeof value === 'string' && guidPattern.test(value);
}
