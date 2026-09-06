export interface OcrProviderConfig {
  id: string;
  providerKey: string;
  name: string;
  baseUrl: string;
  hasApiKey: boolean;
  isEnabled: boolean;
  lastTestedAtUtc: string | null;
  lastTestStatusKey: string | null;
  lastTestMessage: string | null;
  createdAtUtc: string;
  updatedAtUtc: string | null;
  version: number;
}

export interface UpdateOcrProviderConfigRequest {
  name: string;
  baseUrl: string;
  apiKey?: string | null;
  isEnabled: boolean;
  version: number;
}

export interface TestOcrProviderConfigResult {
  succeeded: boolean;
  message: string;
}

export interface OcrIdCardTask {
  id: string;
  sourceFileId: string;
  statusKey: string;
  recognizedName: string | null;
  recognizedIdNumber: string | null;
  recognizedGender: string | null;
  recognizedNation: string | null;
  recognizedAddress: string | null;
  recognizedBirthDate: string | null;
  confirmedName: string | null;
  confirmedIdNumber: string | null;
  confirmedGender: string | null;
  confirmedNation: string | null;
  confirmedAddress: string | null;
  confirmedBirthDate: string | null;
  failureMessage: string | null;
  recognizedAtUtc: string | null;
  confirmedAtUtc: string | null;
  rejectedAtUtc: string | null;
  createdAtUtc: string;
  updatedAtUtc: string | null;
  createdByUserId: string;
  version: number;
}

export interface OcrIdCardTaskPage {
  items: OcrIdCardTask[];
  page: number;
  pageSize: number;
  total: number;
}

export interface CreateOcrIdCardTaskRequest {
  sourceFileId: string;
}

export interface ConfirmOcrIdCardTaskRequest {
  name: string;
  idNumber: string;
  gender?: string | null;
  nation?: string | null;
  address?: string | null;
  birthDate?: string | null;
  version: number;
}

const guidPattern = /^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/i;

function isGuid(value: unknown): value is string {
  return typeof value === 'string' && guidPattern.test(value);
}

function isRecord(value: unknown): value is Record<string, unknown> {
  return typeof value === 'object' && value !== null && !Array.isArray(value);
}

export function isOcrProviderConfig(value: unknown): value is OcrProviderConfig {
  return (
    isRecord(value) &&
    isGuid(value.id) &&
    typeof value.providerKey === 'string' &&
    typeof value.name === 'string' &&
    typeof value.baseUrl === 'string' &&
    typeof value.hasApiKey === 'boolean' &&
    typeof value.isEnabled === 'boolean' &&
    (value.lastTestedAtUtc === null || typeof value.lastTestedAtUtc === 'string') &&
    (value.lastTestStatusKey === null || typeof value.lastTestStatusKey === 'string') &&
    (value.lastTestMessage === null || typeof value.lastTestMessage === 'string') &&
    typeof value.createdAtUtc === 'string' &&
    (value.updatedAtUtc === null || typeof value.updatedAtUtc === 'string') &&
    typeof value.version === 'number'
  );
}

export function isOcrIdCardTask(value: unknown): value is OcrIdCardTask {
  return (
    isRecord(value) &&
    isGuid(value.id) &&
    isGuid(value.sourceFileId) &&
    typeof value.statusKey === 'string' &&
    (value.recognizedName === null || typeof value.recognizedName === 'string') &&
    (value.recognizedIdNumber === null || typeof value.recognizedIdNumber === 'string') &&
    (value.confirmedName === null || typeof value.confirmedName === 'string') &&
    (value.confirmedIdNumber === null || typeof value.confirmedIdNumber === 'string') &&
    typeof value.createdAtUtc === 'string' &&
    isGuid(value.createdByUserId) &&
    typeof value.version === 'number'
  );
}

export function isOcrIdCardTaskPage(value: unknown): value is OcrIdCardTaskPage {
  return (
    isRecord(value) &&
    Array.isArray(value.items) &&
    value.items.every(isOcrIdCardTask) &&
    typeof value.page === 'number' &&
    typeof value.pageSize === 'number' &&
    typeof value.total === 'number'
  );
}
