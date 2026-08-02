import { describe, expect, it, vi } from 'vitest';
import {
  createCodeGenerationRunsApi
} from '../js/core/code-generation-runs.js';

describe('Layui 代码生成运行 API', () => {
  it('使用稳定路径并严格校验预览包装与运行页', async () => {
    const runId = '0198f36e-f7a7-7c52-9cbb-774e67411212';
    const request = vi.fn()
      .mockResolvedValueOnce({
        runId,
        preview: {
          databaseTableName: 'acme_catalog_product',
          readPermission: 'catalog.products.read',
          writePermission: 'catalog.products.write',
          artifacts: []
        }
      })
      .mockResolvedValueOnce({
        items: [createRun(runId)],
        page: 1,
        pageSize: 20,
        total: 1
      })
      .mockResolvedValueOnce({
        runId: '0198f36e-f7a7-7c52-9cbb-774e67411213',
        previewRunId: runId,
        artifactCount: 8,
        changedArtifactCount: 3,
        manifestSha256: 'b'.repeat(64)
      })
      .mockResolvedValueOnce({
        runId: '0198f36e-f7a7-7c52-9cbb-774e67411215',
        applyRunId: runId,
        artifactCount: 0,
        changedArtifactCount: 2,
        manifestSha256: 'c'.repeat(64)
      })
      .mockResolvedValueOnce({
        rollbacks: [{
          runId: '0198f36e-f7a7-7c52-9cbb-774e67411220',
          applyRunId: '0198f36e-f7a7-7c52-9cbb-774e67411216',
          artifactCount: 1,
          changedArtifactCount: 1,
          manifestSha256: 'd'.repeat(64)
        }, {
          runId: '0198f36e-f7a7-7c52-9cbb-774e67411221',
          applyRunId: '0198f36e-f7a7-7c52-9cbb-774e67411215',
          artifactCount: 0,
          changedArtifactCount: 1,
          manifestSha256: 'e'.repeat(64)
        }]
      });
    const api = createCodeGenerationRunsApi(request);
    const runs = [
      {
        ...createRun('0198f36e-f7a7-7c52-9cbb-774e67411216'),
        operationKind: 'apply',
        startedAtUtc: '2026-08-02T08:00:02Z',
        finishedAtUtc: '2026-08-02T08:00:02Z'
      },
      {
        ...createRun('0198f36e-f7a7-7c52-9cbb-774e67411215'),
        operationKind: 'apply',
        startedAtUtc: '2026-08-02T08:00:01Z',
        finishedAtUtc: '2026-08-02T08:00:01Z'
      }
    ];

    await api.preview({
      templateId: '0198f36e-f7a7-7c52-9cbb-774e67411205',
      templateVersion: 3
    });
    await api.list('succeeded');
    await api.apply({ previewRunId: runId });
    await api.rollback({ applyRunId: runId });
    await api.rollbackApply(runs, '0198f36e-f7a7-7c52-9cbb-774e67411215');

    expect(request.mock.calls.map(([path, options]) => [
      path,
      options?.method ?? 'GET'
    ])).toEqual([
      ['/api/v1/code-generation/runs/preview', 'POST'],
      [
        '/api/v1/code-generation/runs?page=1&pageSize=20&status=succeeded',
        'GET'
      ],
      ['/api/v1/code-generation/runs/apply', 'POST'],
      ['/api/v1/code-generation/runs/rollback', 'POST'],
      ['/api/v1/code-generation/runs/rollback-chain', 'POST']
    ]);
  });

  it('拒绝历史响应夹带生成源码', async () => {
    const run = {
      ...createRun('0198f36e-f7a7-7c52-9cbb-774e67411212'),
      content: 'generated source'
    };
    const api = createCodeGenerationRunsApi(vi.fn().mockResolvedValue({
      items: [run],
      page: 1,
      pageSize: 20,
      total: 1
    }));

    await expect(api.list())
      .rejects.toThrow('client.invalid_code_generation_run_page');
  });
});

function createRun(id) {
  return {
    id,
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
}
