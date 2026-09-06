export interface ReportingExecutionParameterValue {
  parameterKey: string;
  value: string | null;
}

export interface ExecuteReportingDefinitionRequest {
  versionNumber?: number | null;
  parameters: ReportingExecutionParameterValue[];
}

export interface ReportingExecutionColumnDefinition {
  columnKey: string;
  displayName: string;
}

export interface ReportingExecutionRow {
  values: Record<string, string | null>;
}

export interface ReportingExecutionPage {
  definitionId: string;
  definitionKey: string;
  definitionName: string;
  versionNumber: number;
  queryPortKey: string;
  columns: ReportingExecutionColumnDefinition[];
  rows: ReportingExecutionRow[];
  page: number;
  pageSize: number;
  hasMore: boolean;
  totalRows: number | null;
  commandTimeoutSeconds: number;
  executedAtUtc: string;
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

export function isReportingExecutionColumnDefinition(value: unknown): value is ReportingExecutionColumnDefinition {
  return isRecord(value)
    && typeof value.columnKey === 'string'
    && typeof value.displayName === 'string';
}

export function isReportingExecutionRow(value: unknown): value is ReportingExecutionRow {
  return isRecord(value) && isRecord(value.values);
}

export function isReportingExecutionPage(value: unknown): value is ReportingExecutionPage {
  return isRecord(value)
    && isGuid(value.definitionId)
    && typeof value.definitionKey === 'string'
    && typeof value.definitionName === 'string'
    && typeof value.versionNumber === 'number'
    && typeof value.queryPortKey === 'string'
    && Array.isArray(value.columns)
    && value.columns.every(isReportingExecutionColumnDefinition)
    && Array.isArray(value.rows)
    && value.rows.every(isReportingExecutionRow)
    && typeof value.page === 'number'
    && typeof value.pageSize === 'number'
    && typeof value.hasMore === 'boolean'
    && (value.totalRows === null || typeof value.totalRows === 'number')
    && typeof value.commandTimeoutSeconds === 'number'
    && typeof value.executedAtUtc === 'string';
}
