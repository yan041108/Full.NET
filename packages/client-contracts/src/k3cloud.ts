export interface K3CloudConnectionConfig {
  id: string;
  name: string;
  baseUrl: string;
  acctId: string;
  username: string;
  lcid: number;
  hasPassword: boolean;
  isDefault: boolean;
  isEnabled: boolean;
  lastTestedAtUtc: string | null;
  lastTestStatusKey: string | null;
  lastTestMessage: string | null;
  createdAtUtc: string;
  updatedAtUtc: string | null;
  version: number;
}

export interface CreateK3CloudConnectionConfigRequest {
  name: string;
  baseUrl: string;
  acctId: string;
  username: string;
  password: string;
  lcid: number;
  isDefault: boolean;
  isEnabled: boolean;
}

export interface UpdateK3CloudConnectionConfigRequest {
  name: string;
  baseUrl: string;
  acctId: string;
  username: string;
  password?: string | null;
  lcid: number;
  isDefault: boolean;
  isEnabled: boolean;
  version: number;
}

export interface TestK3CloudConnectionConfigResult {
  succeeded: boolean;
  message: string;
}

export interface K3CloudDocumentSync {
  id: string;
  connectionConfigId: string;
  documentTypeKey: string;
  businessKey: string;
  statusKey: string;
  lastStepKey: string | null;
  externalBillId: string | null;
  externalBillNo: string | null;
  lastErrorCode: string | null;
  lastErrorMessage: string | null;
  submittedAtUtc: string | null;
  createdAtUtc: string;
  updatedAtUtc: string | null;
  createdByUserId: string;
  version: number;
}

export interface K3CloudDocumentSyncPage {
  items: K3CloudDocumentSync[];
  page: number;
  pageSize: number;
  total: number;
}

export interface CreateK3CloudDocumentSyncRequest {
  connectionConfigId: string;
  documentTypeKey: string;
  businessKey: string;
  payloadJson: string;
}

const guidPattern = /^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/i;

function isGuid(value: unknown): value is string {
  return typeof value === 'string' && guidPattern.test(value);
}

function isRecord(value: unknown): value is Record<string, unknown> {
  return typeof value === 'object' && value !== null && !Array.isArray(value);
}

export function isK3CloudConnectionConfig(value: unknown): value is K3CloudConnectionConfig {
  return (
    isRecord(value) &&
    isGuid(value.id) &&
    typeof value.name === 'string' &&
    typeof value.baseUrl === 'string' &&
    typeof value.acctId === 'string' &&
    typeof value.username === 'string' &&
    typeof value.lcid === 'number' &&
    typeof value.hasPassword === 'boolean' &&
    typeof value.isDefault === 'boolean' &&
    typeof value.isEnabled === 'boolean' &&
    typeof value.createdAtUtc === 'string' &&
    (value.updatedAtUtc === null || typeof value.updatedAtUtc === 'string') &&
    typeof value.version === 'number'
  );
}

export function isK3CloudDocumentSync(value: unknown): value is K3CloudDocumentSync {
  return (
    isRecord(value) &&
    isGuid(value.id) &&
    isGuid(value.connectionConfigId) &&
    typeof value.documentTypeKey === 'string' &&
    typeof value.businessKey === 'string' &&
    typeof value.statusKey === 'string' &&
    typeof value.createdAtUtc === 'string' &&
    isGuid(value.createdByUserId) &&
    typeof value.version === 'number'
  );
}

export function isK3CloudDocumentSyncPage(value: unknown): value is K3CloudDocumentSyncPage {
  return (
    isRecord(value) &&
    Array.isArray(value.items) &&
    value.items.every(isK3CloudDocumentSync) &&
    typeof value.page === 'number' &&
    typeof value.pageSize === 'number' &&
    typeof value.total === 'number'
  );
}
