import {
  isCodeGenerationPreviewRequest,
  isCodeGenerationPreviewResponse,
  type CodeGenerationPreviewRequest,
  type CodeGenerationPreviewResponse
} from './code-generation-previews.js';

export type CodeGenerationRunOperationKind = 'preview' | 'apply' | 'rollback';

export type CodeGenerationRunStatus = 'running' | 'succeeded' | 'failed';

export type CodeGenerationRunPreviewRequest =
  | {
      templateId: string;
      templateVersion: number;
      schema?: never;
    }
  | {
      templateId?: never;
      templateVersion?: never;
      schema: CodeGenerationPreviewRequest;
    };

export interface CodeGenerationRunPreviewResponse {
  runId: string;
  preview: CodeGenerationPreviewResponse;
}

export interface CodeGenerationRunApplyRequest {
  previewRunId: string;
}

export interface CodeGenerationRunApplyResponse {
  runId: string;
  previewRunId: string;
  artifactCount: number;
  changedArtifactCount: number;
  manifestSha256: string;
}

export interface CodeGenerationRunRollbackRequest {
  applyRunId: string;
}

export interface CodeGenerationRunRollbackResponse {
  runId: string;
  applyRunId: string;
  artifactCount: number;
  changedArtifactCount: number;
  manifestSha256: string;
}

export interface CodeGenerationRunResponse {
  id: string;
  templateId: string | null;
  templateVersion: number | null;
  operationKind: CodeGenerationRunOperationKind;
  status: CodeGenerationRunStatus;
  moduleKey: string | null;
  entityKey: string | null;
  schemaSha256: string | null;
  artifactCount: number;
  manifestSha256: string | null;
  errorCode: string | null;
  requestedByUserId: string;
  startedAtUtc: string;
  finishedAtUtc: string;
  sourceApplyRunId: string | null;
}

export interface CodeGenerationRunPage {
  items: CodeGenerationRunResponse[];
  page: number;
  pageSize: number;
  total: number;
}

const uuidPattern =
  /^[0-9a-f]{8}-[0-9a-f]{4}-[1-8][0-9a-f]{3}-[89ab][0-9a-f]{3}-[0-9a-f]{12}$/i;
const sha256Pattern = /^[0-9a-f]{64}$/;
const errorCodePattern = /^[a-z0-9]+(?:[._][a-z0-9]+)*$/;
const runKeys = new Set([
  'id',
  'templateId',
  'templateVersion',
  'operationKind',
  'status',
  'moduleKey',
  'entityKey',
  'schemaSha256',
  'artifactCount',
  'manifestSha256',
  'errorCode',
  'requestedByUserId',
  'startedAtUtc',
  'finishedAtUtc',
  'sourceApplyRunId'
]);

export function isCodeGenerationRunPreviewRequest(
  value: unknown
): value is CodeGenerationRunPreviewRequest {
  if (!isRecord(value)) {
    return false;
  }

  if (hasOnlyKeys(value, new Set(['schema']))) {
    return isCodeGenerationPreviewRequest(value.schema);
  }

  return hasOnlyKeys(value, new Set(['templateId', 'templateVersion']))
    && isUuid(value.templateId)
    && Number.isSafeInteger(value.templateVersion)
    && (value.templateVersion as number) >= 1;
}

export function isCodeGenerationRunPreviewResponse(
  value: unknown
): value is CodeGenerationRunPreviewResponse {
  return isRecord(value)
    && hasOnlyKeys(value, new Set(['runId', 'preview']))
    && isUuid(value.runId)
    && isCodeGenerationPreviewResponse(value.preview);
}

export function isCodeGenerationRunApplyRequest(
  value: unknown
): value is CodeGenerationRunApplyRequest {
  return isRecord(value)
    && hasOnlyKeys(value, new Set(['previewRunId']))
    && isUuid(value.previewRunId);
}

export function isCodeGenerationRunApplyResponse(
  value: unknown
): value is CodeGenerationRunApplyResponse {
  return isRecord(value)
    && hasOnlyKeys(value, new Set([
      'runId',
      'previewRunId',
      'artifactCount',
      'changedArtifactCount',
      'manifestSha256'
    ]))
    && isUuid(value.runId)
    && isUuid(value.previewRunId)
    && Number.isSafeInteger(value.artifactCount)
    && (value.artifactCount as number) > 0
    && Number.isSafeInteger(value.changedArtifactCount)
    && (value.changedArtifactCount as number) >= 0
    && (value.changedArtifactCount as number)
      <= (value.artifactCount as number)
    && isSha256(value.manifestSha256);
}


export function isCodeGenerationRunRollbackRequest(
  value: unknown
): value is CodeGenerationRunRollbackRequest {
  return isRecord(value)
    && hasOnlyKeys(value, new Set(['applyRunId']))
    && isUuid(value.applyRunId);
}

export function isCodeGenerationRunRollbackResponse(
  value: unknown
): value is CodeGenerationRunRollbackResponse {
  return isRecord(value)
    && hasOnlyKeys(value, new Set([
      'runId',
      'applyRunId',
      'artifactCount',
      'changedArtifactCount',
      'manifestSha256'
    ]))
    && isUuid(value.runId)
    && isUuid(value.applyRunId)
    && Number.isSafeInteger(value.artifactCount)
    && (value.artifactCount as number) >= 0
    && Number.isSafeInteger(value.changedArtifactCount)
    && (value.changedArtifactCount as number) >= 0
    && isSha256(value.manifestSha256);
}
export function isCodeGenerationRunResponse(
  value: unknown
): value is CodeGenerationRunResponse {
  if (!isRecord(value)
    || !hasOnlyKeys(value, runKeys)
    || !isUuid(value.id)
    || !hasTemplatePair(value.templateId, value.templateVersion)
    || (value.operationKind !== 'preview'
      && value.operationKind !== 'apply'
      && value.operationKind !== 'rollback')
    || !isUuid(value.requestedByUserId)
    || !isDateTime(value.startedAtUtc)
    || !isDateTime(value.finishedAtUtc)
    || Date.parse(value.finishedAtUtc) < Date.parse(value.startedAtUtc)
    || !hasSourceApplyPair(value.operationKind, value.sourceApplyRunId)) {
    return false;
  }

  if (value.status === 'running' || value.status === 'succeeded') {
    const minArtifacts = value.operationKind === 'rollback' ? 0 : 1;
    return (value.operationKind !== 'apply'
        || (isUuid(value.templateId)
          && Number.isSafeInteger(value.templateVersion)))
      && (value.operationKind !== 'rollback'
        || (value.templateId === null && value.templateVersion === null))
      && (value.status !== 'running'
        || value.operationKind === 'apply'
        || value.operationKind === 'rollback')
      && isNonEmptyString(value.moduleKey)
      && isNonEmptyString(value.entityKey)
      && isSha256(value.schemaSha256)
      && Number.isSafeInteger(value.artifactCount)
      && (value.artifactCount as number) >= minArtifacts
      && isSha256(value.manifestSha256)
      && value.errorCode === null;
  }

  return value.status === 'failed'
    && value.moduleKey === null
    && value.entityKey === null
    && value.schemaSha256 === null
    && value.artifactCount === 0
    && value.manifestSha256 === null
    && typeof value.errorCode === 'string'
    && value.errorCode.length <= 128
    && errorCodePattern.test(value.errorCode);
}

export function isCodeGenerationRunPage(
  value: unknown
): value is CodeGenerationRunPage {
  return isRecord(value)
    && hasOnlyKeys(value, new Set(['items', 'page', 'pageSize', 'total']))
    && Array.isArray(value.items)
    && value.items.every(isCodeGenerationRunResponse)
    && Number.isSafeInteger(value.page)
    && (value.page as number) >= 1
    && Number.isSafeInteger(value.pageSize)
    && (value.pageSize as number) >= 1
    && (value.pageSize as number) <= 100
    && Number.isSafeInteger(value.total)
    && (value.total as number) >= 0;
}

function hasSourceApplyPair(
  operationKind: unknown,
  sourceApplyRunId: unknown
): boolean {
  if (operationKind === 'rollback') {
    return isUuid(sourceApplyRunId);
  }

  return sourceApplyRunId === null;
}

function hasTemplatePair(
  templateId: unknown,
  templateVersion: unknown
): boolean {
  return (templateId === null && templateVersion === null)
    || (isUuid(templateId)
      && Number.isSafeInteger(templateVersion)
      && (templateVersion as number) >= 1);
}

function hasOnlyKeys(
  value: Record<string, unknown>,
  allowed: Set<string>
): boolean {
  const keys = Object.keys(value);
  return keys.length === allowed.size
    && keys.every(key => allowed.has(key));
}

function isSha256(value: unknown): value is string {
  return typeof value === 'string' && sha256Pattern.test(value);
}

function isDateTime(value: unknown): value is string {
  return typeof value === 'string'
    && value.length > 0
    && Number.isFinite(Date.parse(value));
}

function isUuid(value: unknown): value is string {
  return typeof value === 'string' && uuidPattern.test(value);
}

function isNonEmptyString(value: unknown): value is string {
  return typeof value === 'string' && value.trim().length > 0;
}

function isRecord(value: unknown): value is Record<string, unknown> {
  return typeof value === 'object' && value !== null && !Array.isArray(value);
}
