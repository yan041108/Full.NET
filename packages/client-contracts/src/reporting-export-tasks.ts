export interface CreateReportingExportTaskRequest {
  definitionId: string;
  formatKey: string;
  versionNumber?: number | null;
  parameters: ReportingExportTaskParameterValue[];
}

export interface ReportingExportTaskParameterValue {
  parameterKey: string;
  value: string | null;
}

export interface ReportingExportTask {
  id: string;
  definitionId: string;
  definitionKey: string;
  definitionName: string;
  versionNumber: number;
  formatKey: string;
  statusKey: string;
  rowCount: number;
  outputFileName: string | null;
  errorCode: string | null;
  errorMessage: string | null;
  requestedByUserId: string;
  createdAtUtc: string;
  completedAtUtc: string | null;
}

export interface ReportingExportTaskDetail extends ReportingExportTask {
  outputFileId: string | null;
  parameters: ReportingExportTaskParameterValue[];
}

export interface ReportingExportTaskPage {
  items: ReportingExportTask[];
  page: number;
  pageSize: number;
  total: number;
}

const guidPattern = /^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/i;

function isGuid(value: unknown): value is string {
  return typeof value === 'string' && guidPattern.test(value);
}

function isRecord(value: unknown): value is Record<string, unknown> {
  return typeof value === 'object' && value !== null && !Array.isArray(value);
}

function isNullableString(value: unknown): value is string | null {
  return value === null || typeof value === 'string';
}

export function isReportingExportTaskParameterValue(value: unknown): value is ReportingExportTaskParameterValue {
  return isRecord(value)
    && typeof value.parameterKey === 'string'
    && isNullableString(value.value);
}

export function isReportingExportTask(value: unknown): value is ReportingExportTask {
  return isRecord(value)
    && isGuid(value.id)
    && isGuid(value.definitionId)
    && typeof value.definitionKey === 'string'
    && typeof value.definitionName === 'string'
    && typeof value.versionNumber === 'number'
    && typeof value.formatKey === 'string'
    && typeof value.statusKey === 'string'
    && typeof value.rowCount === 'number'
    && isNullableString(value.outputFileName)
    && isNullableString(value.errorCode)
    && isNullableString(value.errorMessage)
    && isGuid(value.requestedByUserId)
    && typeof value.createdAtUtc === 'string'
    && (value.completedAtUtc === null || typeof value.completedAtUtc === 'string');
}

export function isReportingExportTaskDetail(value: unknown): value is ReportingExportTaskDetail {
  return isReportingExportTask(value)
    && isRecord(value)
    && (value.outputFileId === null || isGuid(value.outputFileId))
    && Array.isArray(value.parameters)
    && value.parameters.every(isReportingExportTaskParameterValue);
}

export function isReportingExportTaskPage(value: unknown): value is ReportingExportTaskPage {
  return isRecord(value)
    && Array.isArray(value.items)
    && value.items.every(isReportingExportTask)
    && typeof value.page === 'number'
    && typeof value.pageSize === 'number'
    && typeof value.total === 'number';
}
