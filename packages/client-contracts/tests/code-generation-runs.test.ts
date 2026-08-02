import { describe, expect, it } from 'vitest';
import {
  isCodeGenerationRunApplyRequest,
  isCodeGenerationRunApplyResponse,
  isCodeGenerationRunPage,
  isCodeGenerationRunPreviewRequest,
  isCodeGenerationRunPreviewResponse,
  isCodeGenerationRunResponse,
  isCodeGenerationRunRollbackRequest,
  isCodeGenerationRunRollbackResponse,
  isCodeGenerationRunRollbackChainRequest,
  isCodeGenerationRunRollbackChainResponse
} from '../src/code-generation-runs';

describe('code-generation run contracts', () => {
  const schema = {
    ownerKey: 'acme',
    moduleKey: 'catalog',
    entityKey: 'product',
    databaseTableName: 'acme_catalog_product',
    rootNamespace: 'Acme.Modules.Catalog',
    clrTypeName: 'Product',
    apiResourceName: 'products',
    permissionResourceName: 'products',
    dataScope: 'host.only',
    hasVersion: true,
    columns: [{
      databaseName: 'Id',
      clrPropertyName: 'Id',
      jsonPropertyName: 'id',
      scalarType: 'uuid',
      isNullable: false,
      maxLength: null,
      numericPrecision: null,
      numericScale: null
    }]
  };
  const run = {
    id: '0198f36e-f7a7-7c52-9cbb-774e67411212',
    templateId: null,
    templateVersion: null,
    operationKind: 'preview',
    status: 'succeeded',
    moduleKey: 'catalog',
    entityKey: 'product',
    schemaSha256: 'a'.repeat(64),
    artifactCount: 8,
    manifestSha256: 'b'.repeat(64),
    errorCode: null,
    requestedByUserId: '0198f36e-f7a7-7c52-9cbb-774e67411211',
    startedAtUtc: '2026-07-31T05:00:00Z',
    finishedAtUtc: '2026-07-31T05:00:01Z',
    sourceApplyRunId: null
  };

  it('accepts exactly one inline or template source', () => {
    expect(isCodeGenerationRunPreviewRequest({
      schema
    })).toBe(true);
    expect(isCodeGenerationRunPreviewRequest({
      templateId: '0198f36e-f7a7-7c52-9cbb-774e67411205',
      templateVersion: 3
    })).toBe(true);
    expect(isCodeGenerationRunPreviewRequest({
      schema,
      templateId: '0198f36e-f7a7-7c52-9cbb-774e67411205',
      templateVersion: 3
    })).toBe(false);
    expect(isCodeGenerationRunPreviewRequest({
      templateId: '0198f36e-f7a7-7c52-9cbb-774e67411205'
    })).toBe(false);
  });

  it('accepts consistent success, preview wrapper, and page', () => {
    expect(isCodeGenerationRunResponse(run)).toBe(true);
    expect(isCodeGenerationRunPreviewResponse({
      runId: run.id,
      preview: {
        databaseTableName: 'acme_catalog_product',
        readPermission: 'catalog.products.read',
        writePermission: 'catalog.products.write',
        artifacts: []
      }
    })).toBe(true);
    expect(isCodeGenerationRunPage({
      items: [run],
      page: 1,
      pageSize: 20,
      total: 1
    })).toBe(true);
  });

  it('accepts strict apply input and output summaries', () => {
    expect(isCodeGenerationRunApplyRequest({
      previewRunId: run.id
    })).toBe(true);
    expect(isCodeGenerationRunApplyRequest({
      previewRunId: run.id,
      workspaceRoot: 'C:/source'
    })).toBe(false);
    expect(isCodeGenerationRunApplyResponse({
      runId: '0198f36e-f7a7-7c52-9cbb-774e67411213',
      previewRunId: run.id,
      artifactCount: 8,
      changedArtifactCount: 3,
      manifestSha256: 'b'.repeat(64)
    })).toBe(true);
    expect(isCodeGenerationRunApplyResponse({
      runId: '0198f36e-f7a7-7c52-9cbb-774e67411213',
      previewRunId: run.id,
      artifactCount: 8,
      changedArtifactCount: 9,
      manifestSha256: 'b'.repeat(64)
    })).toBe(false);
    expect(isCodeGenerationRunResponse({
      ...run,
      templateId: '0198f36e-f7a7-7c52-9cbb-774e67411205',
      templateVersion: 3,
      operationKind: 'apply'
    })).toBe(true);
  });

  it('rejects unknown states, malformed outcomes, and source content', () => {
    expect(isCodeGenerationRunResponse({
      ...run,
      status: 'queued'
    })).toBe(false);
    expect(isCodeGenerationRunResponse({
      ...run,
      templateId: '0198f36e-f7a7-7c52-9cbb-774e67411205',
      templateVersion: 3,
      operationKind: 'apply',
      status: 'running'
    })).toBe(true);
    expect(isCodeGenerationRunResponse({
      ...run,
      status: 'failed',
      artifactCount: 0,
      manifestSha256: null,
      errorCode: 'codegen.preview.invalid_schema'
    })).toBe(false);
    expect(isCodeGenerationRunResponse({
      ...run,
      content: 'generated source'
    })).toBe(false);
    expect(isCodeGenerationRunPage({
      items: [run],
      page: 0,
      pageSize: 101,
      total: -1
    })).toBe(false);
  });

  it('accepts rollback summaries with zero artifacts', () => {
    expect(isCodeGenerationRunRollbackRequest({
      applyRunId: run.id
    })).toBe(true);
    expect(isCodeGenerationRunRollbackRequest({
      applyRunId: run.id,
      workspaceRoot: 'C:/source'
    })).toBe(false);
    expect(isCodeGenerationRunRollbackResponse({
      runId: '0198f36e-f7a7-7c52-9cbb-774e67411215',
      applyRunId: run.id,
      artifactCount: 0,
      changedArtifactCount: 2,
      manifestSha256: 'c'.repeat(64)
    })).toBe(true);
    expect(isCodeGenerationRunResponse({
      ...run,
      operationKind: 'rollback',
      artifactCount: 0,
      sourceApplyRunId: run.id,
      templateId: null,
      templateVersion: null
    })).toBe(true);
    expect(isCodeGenerationRunResponse({
      ...run,
      operationKind: 'rollback',
      artifactCount: 0,
      sourceApplyRunId: null
    })).toBe(false);
  });

  it('accepts rollback chain summaries', () => {
    const applyRunId = '0198f36e-f7a7-7c52-9cbb-774e67411214';
    const secondApplyRunId = '0198f36e-f7a7-7c52-9cbb-774e67411216';
    expect(isCodeGenerationRunRollbackChainRequest({
      applyRunIds: [applyRunId, secondApplyRunId]
    })).toBe(true);
    expect(isCodeGenerationRunRollbackChainRequest({
      applyRunIds: [applyRunId]
    })).toBe(false);
    expect(isCodeGenerationRunRollbackChainResponse({
      rollbacks: [
        {
          runId: '0198f36e-f7a7-7c52-9cbb-774e67411215',
          applyRunId: secondApplyRunId,
          artifactCount: 1,
          changedArtifactCount: 1,
          manifestSha256: 'b'.repeat(64)
        },
        {
          runId: '0198f36e-f7a7-7c52-9cbb-774e67411217',
          applyRunId,
          artifactCount: 0,
          changedArtifactCount: 1,
          manifestSha256: 'c'.repeat(64)
        }
      ]
    })).toBe(true);
  });
});
