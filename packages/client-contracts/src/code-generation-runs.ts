import {
  isCodeGenerationPreviewRequest,
  isCodeGenerationPreviewResponse,
  type CodeGenerationPreviewRequest,
  type CodeGenerationPreviewResponse
} from './code-generation-previews.js';

export type CodeGenerationRunOperationKind = 'preview' | 'apply' | 'rollback';

export type CodeGenerationRunStatus = 'running' | 'succeeded' | 'failed';

export type CodeGenerationPublicationStatus = 'disabled' | 'local_only' | 'pending' | 'published' | 'unavailable';

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

export interface CodeGenerationRunApplyPlanRequest {
  previewRunId: string;
  integrationTarget?: CodeGenerationIntegrationTarget;
}

export interface CodeGenerationRunApplyRequest {
  planId: string;
  planSha256: string;
  idempotencyKey: string;
}

export interface CodeGenerationApplyPlanFileResponse {
  relativePath: string;
  kind: 'create' | 'update' | 'unchanged' | 'delete' | 'manifest';
  beforeSha256: string | null;
  afterSha256: string | null;
}

export interface CodeGenerationRunApplyPlanResponse {
  planId: string;
  previewRunId: string;
  workspaceId: string;
  workspaceRevision: number;
  generatorVersion: string;
  planSha256: string;
  expiresAtUtc: string;
  files: CodeGenerationApplyPlanFileResponse[];
}

export type CodeGenerationWorkspacePhase = 'idle' | 'prepared' | 'committing' | 'locally_applied' | 'recovery_required' | 'recovered' | 'completed';

export interface CodeGenerationWorkspaceStateResponse {
  workspaceId: string;
  revision: number;
  tipOperationId: string | null;
  lastOperationId: string | null;
  pendingOperationId: string | null;
  pendingKind: 'apply' | 'rollback' | null;
  pendingClient: 'host' | 'cli' | null;
  phase: CodeGenerationWorkspacePhase;
}

export interface CodeGenerationWorkspaceRecoveryRequest { operationId: string }

export function isCodeGenerationWorkspaceStateResponse(value: unknown): value is CodeGenerationWorkspaceStateResponse {
  if (!isRecord(value) || !hasOnlyKeys(value, new Set(['workspaceId', 'revision', 'tipOperationId', 'lastOperationId',
    'pendingOperationId', 'pendingKind', 'pendingClient', 'phase'])) || !isUuid(value.workspaceId)
    || !Number.isSafeInteger(value.revision) || (value.revision as number) < 0
    || ![value.tipOperationId, value.lastOperationId, value.pendingOperationId].every(id => id === null || isUuid(id))) return false;
  if (value.revision === 0 && (value.tipOperationId !== null || value.lastOperationId !== null)) return false;
  if ((value.revision as number) > 0 && value.lastOperationId === null) return false;
  if (value.pendingOperationId === null) return value.phase === 'idle' && value.pendingKind === null && value.pendingClient === null;
  return ['prepared', 'committing', 'locally_applied', 'recovery_required', 'recovered', 'completed'].includes(value.phase as string)
    && (value.pendingKind === 'apply' || value.pendingKind === 'rollback') && (value.pendingClient === 'host' || value.pendingClient === 'cli');
}

export interface CodeGenerationIntegrationTarget {
  moduleName: string;
  moduleProjectPath: string;
  moduleEntryPointPath: string;
  compositionProjectPath: string;
  compositionCatalogPath: string;
  vueRouterPath: string;
  layuiRouterPath?: string;
  clientRoute?: CodeGenerationClientRouteTarget;
  authorizationContributorPath?: string;
}

export interface CodeGenerationClientRouteTarget {
  routePath: string;
  vueRouteName: string;
  vueComponentPath: string;
  layuiControllerPath?: string;
  layuiControllerExport?: string;
}

export interface CodeGenerationRunApplyResponse {
  runId: string;
  previewRunId: string;
  artifactCount: number;
  changedArtifactCount: number;
  manifestSha256: string;
  publicationStatus: CodeGenerationPublicationStatus;
  commitSha: string | null;
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
  publicationStatus: CodeGenerationPublicationStatus;
  commitSha: string | null;
}

export interface CodeGenerationRunRollbackChainRequest {
  applyRunIds: string[];
}

export interface CodeGenerationRunRollbackChainResponse {
  rollbacks: CodeGenerationRunRollbackResponse[];
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
  publicationStatus: CodeGenerationPublicationStatus;
  commitSha: string | null;
}

export interface CodeGenerationRunPublicationResponse {
  runId: string;
  publicationStatus: CodeGenerationPublicationStatus;
  commitSha: string | null;
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
  'sourceApplyRunId',
  'publicationStatus',
  'commitSha'
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
    && hasOnlyKeys(value, new Set(['planId', 'planSha256', 'idempotencyKey']))
    && isUuid(value.planId) && isSha256(value.planSha256) && isUuid(value.idempotencyKey);
}

export function isCodeGenerationRunApplyPlanResponse(value: unknown): value is CodeGenerationRunApplyPlanResponse {
  if (!isRecord(value) || !hasOnlyKeys(value, new Set(['planId', 'previewRunId', 'workspaceId', 'workspaceRevision',
    'generatorVersion', 'planSha256', 'expiresAtUtc', 'files'])) || !isUuid(value.planId) || !isUuid(value.previewRunId)
    || !isUuid(value.workspaceId) || !Number.isSafeInteger(value.workspaceRevision) || (value.workspaceRevision as number) < 0
    || !isNonEmptyString(value.generatorVersion) || !isSha256(value.planSha256) || !isDateTime(value.expiresAtUtc)
    || !Array.isArray(value.files) || value.files.length === 0 || value.files.length > 2049) return false;
  const paths = new Set<string>();
  let manifests = 0;
  for (const file of value.files) {
    if (!isRecord(file) || !hasOnlyKeys(file, new Set(['relativePath', 'kind', 'beforeSha256', 'afterSha256']))
      || !isNonEmptyString(file.relativePath) || file.relativePath.startsWith('/') || /[\\:\u0000-\u001f]/u.test(file.relativePath)
      || file.relativePath.split('/').some(segment => !segment || segment === '.' || segment === '..')
      || paths.has(file.relativePath.toLowerCase())) return false;
    paths.add(file.relativePath.toLowerCase());
    const before = file.beforeSha256;
    const after = file.afterSha256;
    if (file.kind === 'manifest') {
      if (file.relativePath !== '.fullnet/codegeneration-manifest.json' || (before !== null && !isSha256(before)) || !isSha256(after)) return false;
      manifests++;
    } else {
      if (file.relativePath.split('/').some(segment => ['.git', '.fullnet'].includes(segment.toLowerCase()))) return false;
      if (file.kind === 'create') { if (before !== null || !isSha256(after)) return false; }
      else if (file.kind === 'update') { if (!isSha256(before) || !isSha256(after)) return false; }
      else if (file.kind === 'delete') { if (!isSha256(before) || after !== null) return false; }
      else if (file.kind === 'unchanged') { if (!isSha256(before) || before !== after) return false; }
      else return false;
    }
  }
  return manifests === 1;
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
      'publicationStatus',
      'commitSha',
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
    && isSha256(value.manifestSha256)
    && isPublicationState(value);
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
      'publicationStatus',
      'commitSha',
      'manifestSha256'
    ]))
    && isUuid(value.runId)
    && isUuid(value.applyRunId)
    && Number.isSafeInteger(value.artifactCount)
    && (value.artifactCount as number) >= 0
    && Number.isSafeInteger(value.changedArtifactCount)
    && (value.changedArtifactCount as number) >= 0
    && isSha256(value.manifestSha256)
    && isPublicationState(value);
}

export function isCodeGenerationRunRollbackChainRequest(
  value: unknown
): value is CodeGenerationRunRollbackChainRequest {
  return isRecord(value)
    && hasOnlyKeys(value, new Set(['applyRunIds']))
    && Array.isArray(value.applyRunIds)
    && value.applyRunIds.length >= 2
    && value.applyRunIds.every(isUuid);
}

export function isCodeGenerationRunRollbackChainResponse(
  value: unknown
): value is CodeGenerationRunRollbackChainResponse {
  return isRecord(value)
    && hasOnlyKeys(value, new Set(['rollbacks']))
    && Array.isArray(value.rollbacks)
    && value.rollbacks.length >= 2
    && value.rollbacks.every(isCodeGenerationRunRollbackResponse);
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
    || !hasSourceApplyPair(value.operationKind, value.sourceApplyRunId)
    || !isPublicationState(value)) {
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

export function isCodeGenerationRunPublicationResponse(value: unknown): value is CodeGenerationRunPublicationResponse {
  return isRecord(value) && hasOnlyKeys(value, new Set(['runId', 'publicationStatus', 'commitSha']))
    && isUuid(value.runId) && isPublicationState(value);
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

function isPublicationState(value: Record<string, unknown>): boolean {
  const status = value.publicationStatus;
  if (status !== 'disabled' && status !== 'local_only' && status !== 'pending'
    && status !== 'published' && status !== 'unavailable') return false;
  const commit = value.commitSha;
  if (commit !== null && (typeof commit !== 'string' || !/^(?:[a-f0-9]{40}|[a-f0-9]{64})$/.test(commit))) return false;
  return status === 'published' || status === 'local_only' ? commit !== null : true;
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
