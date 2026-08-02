import { afterEach, describe, expect, it, vi } from 'vitest';
import {
  applyTrackedCodeGeneration,
  listCodeGenerationRuns,
  previewTrackedCodeGeneration,
  rollbackTrackedCodeGeneration,
  executeTrackedCodeGenerationRollback
} from './code-generation-runs';

afterEach(() => {
  vi.unstubAllGlobals();
});

describe('Vue 代码生成运行 API', () => {
  it('只提交预览运行标识并严格校验 Apply 摘要', async () => {
    const previewRunId = '0198f36e-f7a7-7c52-9cbb-774e67411212';
    const fetchMock = vi.fn().mockResolvedValue(new Response(JSON.stringify({
      runId: '0198f36e-f7a7-7c52-9cbb-774e67411213',
      previewRunId,
      artifactCount: 8,
      changedArtifactCount: 3,
      manifestSha256: 'b'.repeat(64)
    }), {
      status: 200,
      headers: { 'content-type': 'application/json' }
    }));
    vi.stubGlobal('fetch', fetchMock);

    await applyTrackedCodeGeneration({ previewRunId });

    expect(fetchMock).toHaveBeenCalledWith(
      expect.stringContaining('/api/v1/code-generation/runs/apply'),
      expect.objectContaining({
        method: 'POST',
        body: JSON.stringify({ previewRunId })
      })
    );
  });

  it('提交严格受跟踪预览并校验包装响应', async () => {
    const fetchMock = vi.fn().mockResolvedValue(new Response(JSON.stringify({
      runId: '0198f36e-f7a7-7c52-9cbb-774e67411212',
      preview: {
        databaseTableName: 'acme_catalog_product',
        readPermission: 'catalog.products.read',
        writePermission: 'catalog.products.write',
        artifacts: []
      }
    }), {
      status: 200,
      headers: { 'content-type': 'application/json' }
    }));
    vi.stubGlobal('fetch', fetchMock);

    await previewTrackedCodeGeneration({
      templateId: '0198f36e-f7a7-7c52-9cbb-774e67411205',
      templateVersion: 3
    });

    expect(fetchMock).toHaveBeenCalledWith(
      expect.stringContaining('/api/v1/code-generation/runs/preview'),
      expect.objectContaining({ method: 'POST' })
    );
  });

  it('对状态筛选编码并拒绝包含源码的运行页', async () => {
    const fetchMock = vi.fn().mockResolvedValue(new Response(JSON.stringify({
      items: [{
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
        content: 'must not be accepted'
      }],
      page: 1,
      pageSize: 20,
      total: 1
    }), {
      status: 200,
      headers: { 'content-type': 'application/json' }
    }));
    vi.stubGlobal('fetch', fetchMock);

    await expect(listCodeGenerationRuns('failed'))
      .rejects.toThrow('client.invalid_code_generation_run_page');
    expect(fetchMock.mock.calls[0][0]).toContain('status=failed');
  });

  it('只提交 Apply 运行标识并严格校验 Rollback 摘要', async () => {
    const applyRunId = '0198f36e-f7a7-7c52-9cbb-774e67411213';
    const fetchMock = vi.fn().mockResolvedValue(new Response(JSON.stringify({
      runId: '0198f36e-f7a7-7c52-9cbb-774e67411215',
      applyRunId,
      artifactCount: 0,
      changedArtifactCount: 2,
      manifestSha256: 'c'.repeat(64)
    }), {
      status: 200,
      headers: { 'content-type': 'application/json' }
    }));
    vi.stubGlobal('fetch', fetchMock);

    await rollbackTrackedCodeGeneration({ applyRunId });

    expect(fetchMock).toHaveBeenCalledWith(
      expect.stringContaining('/api/v1/code-generation/runs/rollback'),
      expect.objectContaining({
        method: 'POST',
        body: JSON.stringify({ applyRunId })
      })
    );
  });

  it('uses rollback-chain when multiple applies must be rolled back', async () => {
    const newest = '0198f36e-f7a7-7c52-9cbb-774e67411216';
    const target = '0198f36e-f7a7-7c52-9cbb-774e67411215';
    const fetchMock = vi.fn().mockResolvedValue(new Response(JSON.stringify({
      rollbacks: [
        {
          runId: '0198f36e-f7a7-7c52-9cbb-774e67411220',
          applyRunId: newest,
          artifactCount: 1,
          changedArtifactCount: 1,
          manifestSha256: 'd'.repeat(64)
        },
        {
          runId: '0198f36e-f7a7-7c52-9cbb-774e67411221',
          applyRunId: target,
          artifactCount: 0,
          changedArtifactCount: 1,
          manifestSha256: 'c'.repeat(64)
        }
      ]
    }), {
      status: 200,
      headers: { 'content-type': 'application/json' }
    }));
    vi.stubGlobal('fetch', fetchMock);

    await executeTrackedCodeGenerationRollback([
      {
        id: newest,
        templateId: null,
        templateVersion: null,
        operationKind: 'apply',
        status: 'succeeded',
        moduleKey: 'catalog',
        entityKey: 'product',
        schemaSha256: 'a'.repeat(64),
        artifactCount: 1,
        manifestSha256: 'b'.repeat(64),
        errorCode: null,
        requestedByUserId: '0198f36e-f7a7-7c52-9cbb-774e67411211',
        startedAtUtc: '2026-08-02T08:00:02Z',
        finishedAtUtc: '2026-08-02T08:00:02Z',
        sourceApplyRunId: null
      },
      {
        id: target,
        templateId: null,
        templateVersion: null,
        operationKind: 'apply',
        status: 'succeeded',
        moduleKey: 'catalog',
        entityKey: 'product',
        schemaSha256: 'a'.repeat(64),
        artifactCount: 1,
        manifestSha256: 'b'.repeat(64),
        errorCode: null,
        requestedByUserId: '0198f36e-f7a7-7c52-9cbb-774e67411211',
        startedAtUtc: '2026-08-02T08:00:01Z',
        finishedAtUtc: '2026-08-02T08:00:01Z',
        sourceApplyRunId: null
      }
    ], target);

    expect(fetchMock).toHaveBeenCalledWith(
      expect.stringContaining('/api/v1/code-generation/runs/rollback-chain'),
      expect.objectContaining({
        method: 'POST',
        body: JSON.stringify({ applyRunIds: [newest, target] })
      })
    );
  });
});
