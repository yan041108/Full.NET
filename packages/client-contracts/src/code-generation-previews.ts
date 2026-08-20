export type CodeGenerationDataScope =
  | 'TenantRequired'
  | 'HostOnly'
  | 'Global'
  | 'tenant.required'
  | 'host.only'
  | 'global';

export type CodeGenerationScalarType =
  | 'Uuid'
  | 'String'
  | 'Int32'
  | 'Int64'
  | 'Boolean'
  | 'DateTimeUtc'
  | 'Decimal'
  | 'uuid'
  | 'string'
  | 'int32'
  | 'int64'
  | 'boolean'
  | 'date.time.utc'
  | 'decimal';

export type CodeGenerationDeleteMode =
  | 'hard.delete'
  | 'soft.delete'
  | 'immutable'
  | 'HardDelete'
  | 'SoftDelete'
  | 'Immutable';

export type CodeGenerationOwnershipMode =
  | 'none'
  | 'organization.unit'
  | 'None'
  | 'OrganizationUnit';

export type CodeGenerationScene =
  | 'single'
  | 'tree'
  | 'master.detail'
  | 'many.to.many'
  | 'Single'
  | 'Tree'
  | 'MasterDetail'
  | 'ManyToMany';

export type CodeGenerationArtifactKind =
  | 'backend'
  | 'vue_client'
  | 'vue_view'
  | 'layui_client'
  | 'report'
  | 'migration_template'
  | 'integration_test_template'
  | 'openapi_contract';

export type CodeGenerationColumnControlKind =
  | 'text'
  | 'textarea'
  | 'number'
  | 'switch'
  | 'datetime'
  | 'uuid'
  | 'Text'
  | 'Textarea'
  | 'Number'
  | 'Switch'
  | 'DateTime'
  | 'Uuid';

export type CodeGenerationColumnQueryKind =
  | 'none'
  | 'equals'
  | 'contains'
  | 'range'
  | 'None'
  | 'Equals'
  | 'Contains'
  | 'Range';

export interface CodeGenerationPreviewColumnUiRequest {
  controlKind: CodeGenerationColumnControlKind;
  showInList: boolean;
  includeInCreate: boolean;
  includeInUpdate: boolean;
  required: boolean;
  sortable: boolean;
  queryable: boolean;
  queryKind: CodeGenerationColumnQueryKind;
  unique: boolean;
  includeInImportExport: boolean;
}

export interface CodeGenerationPreviewColumnRequest {
  databaseName: string;
  clrPropertyName: string;
  jsonPropertyName: string;
  scalarType: CodeGenerationScalarType;
  isNullable: boolean;
  maxLength: number | null;
  numericPrecision: number | null;
  numericScale: number | null;
  ui?: CodeGenerationPreviewColumnUiRequest;
}

export interface CodeGenerationSchemaBase {
  ownerKey: string;
  moduleKey: string;
  entityKey: string;
  databaseTableName: string;
  rootNamespace: string;
  clrTypeName: string;
  apiResourceName: string;
  permissionResourceName: string;
  dataScope: CodeGenerationDataScope;
  columns: CodeGenerationPreviewColumnRequest[];
}

export interface CodeGenerationEntityCapabilitiesRequest {
  deleteMode: CodeGenerationDeleteMode;
  hasCreatedAudit: boolean;
  hasUpdatedAudit: boolean;
  hasDeletedAudit: boolean;
  hasVersion: boolean;
  ownershipMode: CodeGenerationOwnershipMode;
}

export interface CodeGenerationRelationshipRequest {
  principalEntityKey: string;
  principalColumnName: string;
  principalDataScope: CodeGenerationDataScope;
  dependentEntityKey: string;
  dependentColumnName: string;
  dependentDataScope: CodeGenerationDataScope;
  compositeKeyColumnNames?: string[];
  cascadeDelete?: boolean;
}

export type CodeGenerationPreviewRequest =
  CodeGenerationSchemaBase & (
    | {
        hasVersion: boolean;
        entityCapabilities?: never;
        scene?: never;
        relationships?: never;
      }
    | {
        hasVersion?: never;
        entityCapabilities: CodeGenerationEntityCapabilitiesRequest;
        scene: CodeGenerationScene;
        relationships: CodeGenerationRelationshipRequest[];
      }
  );

export interface CodeGenerationPreviewArtifact {
  path: string;
  kind: CodeGenerationArtifactKind;
  sha256: string;
  content: string;
}

export interface CodeGenerationPreviewResponse {
  databaseTableName: string;
  readPermission: string;
  writePermission: string;
  artifacts: CodeGenerationPreviewArtifact[];
  createPermission?: string;
  updatePermission?: string;
  disablePermission?: string;
}

const dataScopes = new Set<CodeGenerationDataScope>([
  'TenantRequired',
  'HostOnly',
  'Global',
  'tenant.required',
  'host.only',
  'global'
]);
const scalarTypes = new Set<CodeGenerationScalarType>([
  'Uuid',
  'String',
  'Int32',
  'Int64',
  'Boolean',
  'DateTimeUtc',
  'Decimal',
  'uuid',
  'string',
  'int32',
  'int64',
  'boolean',
  'date.time.utc',
  'decimal'
]);
const deleteModes = new Set<CodeGenerationDeleteMode>([
  'hard.delete',
  'soft.delete',
  'immutable',
  'HardDelete',
  'SoftDelete',
  'Immutable'
]);
const ownershipModes = new Set<CodeGenerationOwnershipMode>([
  'none',
  'organization.unit',
  'None',
  'OrganizationUnit'
]);
const scenes = new Set<CodeGenerationScene>([
  'single',
  'tree',
  'master.detail',
  'many.to.many',
  'Single',
  'Tree',
  'MasterDetail',
  'ManyToMany'
]);
const controlKinds = new Set<CodeGenerationColumnControlKind>([
  'text',
  'textarea',
  'number',
  'switch',
  'datetime',
  'uuid',
  'Text',
  'Textarea',
  'Number',
  'Switch',
  'DateTime',
  'Uuid'
]);
const queryKinds = new Set<CodeGenerationColumnQueryKind>([
  'none',
  'equals',
  'contains',
  'range',
  'None',
  'Equals',
  'Contains',
  'Range'
]);
const artifactKinds = new Set<CodeGenerationArtifactKind>([
  'backend',
  'vue_client',
  'vue_view',
  'layui_client',
  'report',
  'migration_template',
  'integration_test_template',
  'openapi_contract'
]);
const sha256Pattern = /^[0-9a-f]{64}$/;

export function isCodeGenerationPreviewRequest(
  value: unknown
): value is CodeGenerationPreviewRequest {
  return isRecord(value)
    && isNonEmptyString(value.ownerKey)
    && isNonEmptyString(value.moduleKey)
    && isNonEmptyString(value.entityKey)
    && isNonEmptyString(value.databaseTableName)
    && isNonEmptyString(value.rootNamespace)
    && isNonEmptyString(value.clrTypeName)
    && isNonEmptyString(value.apiResourceName)
    && isNonEmptyString(value.permissionResourceName)
    && dataScopes.has(value.dataScope as CodeGenerationDataScope)
    && Array.isArray(value.columns)
    && value.columns.length > 0
    && value.columns.length <= 128
    && value.columns.every(isCodeGenerationPreviewColumnRequest)
    && hasValidLifecycleShape(value);
}

export function isCodeGenerationPreviewResponse(
  value: unknown
): value is CodeGenerationPreviewResponse {
  return isRecord(value)
    && isNonEmptyString(value.databaseTableName)
    && isNonEmptyString(value.readPermission)
    && isNonEmptyString(value.writePermission)
    && Array.isArray(value.artifacts)
    && value.artifacts.every(isPreviewArtifact);
}

export function isCodeGenerationPreviewColumnRequest(
  value: unknown
): value is CodeGenerationPreviewColumnRequest {
  return isRecord(value)
    && isNonEmptyString(value.databaseName)
    && isNonEmptyString(value.clrPropertyName)
    && isNonEmptyString(value.jsonPropertyName)
    && scalarTypes.has(value.scalarType as CodeGenerationScalarType)
    && typeof value.isNullable === 'boolean'
    && isNullableInteger(value.maxLength)
    && isNullableInteger(value.numericPrecision)
    && isNullableInteger(value.numericScale)
    && (value.ui === undefined || isColumnUi(value.ui));
}

function isColumnUi(
  value: unknown
): value is CodeGenerationPreviewColumnUiRequest {
  return isRecord(value)
    && controlKinds.has(value.controlKind as CodeGenerationColumnControlKind)
    && typeof value.showInList === 'boolean'
    && typeof value.includeInCreate === 'boolean'
    && typeof value.includeInUpdate === 'boolean'
    && typeof value.required === 'boolean'
    && typeof value.sortable === 'boolean'
    && typeof value.queryable === 'boolean'
    && queryKinds.has(value.queryKind as CodeGenerationColumnQueryKind)
    && typeof value.unique === 'boolean'
    && typeof value.includeInImportExport === 'boolean';
}

function isPreviewArtifact(
  value: unknown
): value is CodeGenerationPreviewArtifact {
  return isRecord(value)
    && isNonEmptyString(value.path)
    && artifactKinds.has(value.kind as CodeGenerationArtifactKind)
    && typeof value.sha256 === 'string'
    && sha256Pattern.test(value.sha256)
    && typeof value.content === 'string';
}

function hasValidLifecycleShape(value: Record<string, unknown>): boolean {
  const hasLegacyShape = typeof value.hasVersion === 'boolean';
  const hasExplicitShape = isEntityCapabilities(value.entityCapabilities);
  if (hasLegacyShape === hasExplicitShape) {
    return false;
  }

  if (hasLegacyShape) {
    return isNullish(value.entityCapabilities)
      && isNullish(value.scene)
      && isNullish(value.relationships);
  }

  return isNullish(value.hasVersion)
    && scenes.has(value.scene as CodeGenerationScene)
    && Array.isArray(value.relationships)
    && value.relationships.every(isRelationship);
}

function isEntityCapabilities(
  value: unknown
): value is CodeGenerationEntityCapabilitiesRequest {
  return isRecord(value)
    && deleteModes.has(value.deleteMode as CodeGenerationDeleteMode)
    && typeof value.hasCreatedAudit === 'boolean'
    && typeof value.hasUpdatedAudit === 'boolean'
    && typeof value.hasDeletedAudit === 'boolean'
    && typeof value.hasVersion === 'boolean'
    && ownershipModes.has(value.ownershipMode as CodeGenerationOwnershipMode);
}

function isRelationship(
  value: unknown
): value is CodeGenerationRelationshipRequest {
  return isRecord(value)
    && isNonEmptyString(value.principalEntityKey)
    && isNonEmptyString(value.principalColumnName)
    && dataScopes.has(value.principalDataScope as CodeGenerationDataScope)
    && isNonEmptyString(value.dependentEntityKey)
    && isNonEmptyString(value.dependentColumnName)
    && dataScopes.has(value.dependentDataScope as CodeGenerationDataScope);
}

function isNullish(value: unknown): value is null | undefined {
  return value === null || value === undefined;
}

function isNullableInteger(value: unknown): value is number | null {
  return value === null || Number.isInteger(value);
}

function isNonEmptyString(value: unknown): value is string {
  return typeof value === 'string' && value.trim().length > 0;
}

function isRecord(value: unknown): value is Record<string, unknown> {
  return typeof value === 'object' && value !== null && !Array.isArray(value);
}
