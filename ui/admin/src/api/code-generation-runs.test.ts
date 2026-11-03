import { beforeEach, describe, expect, it, vi } from 'vitest';

import { http } from './http';

import {
  applyTrackedCodeGeneration,
  prepareTrackedCodeGenerationApply,
  executeTrackedCodeGenerationRollback,
  listCodeGenerationRuns,
  previewTrackedCodeGeneration,
  rollbackTrackedCodeGeneration,
  retryCodeGenerationPublication,
  type CodeGenerationRunResponse
} from './code-generation-runs';



vi.mock('./http', () => ({

  http: {

    request: vi.fn(),

    requestBlob: vi.fn()

  }

}));

const requestMock = vi.mocked(http.request);



const runSummary = {

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

  sourceApplyRunId: null,
  publicationStatus: 'unavailable',
  commitSha: null

};



describe('code-generation-runs api', () => {

  beforeEach(() => requestMock.mockReset());



  it('retries publication through its own endpoint', async () => {
    const runId = '0198f36e-f7a7-7c52-9cbb-774e67411213';
    requestMock.mockResolvedValueOnce({ runId, publicationStatus: 'published', commitSha: 'a'.repeat(40) });
    expect((await retryCodeGenerationPublication(runId)).publicationStatus).toBe('published');
    expect(requestMock).toHaveBeenCalledWith(`/api/v1/code-generation/runs/${runId}/publication/retry`,
      expect.objectContaining({ method: 'POST' }), undefined);
  });

  it('审查计划使用独立接口并验证全文件摘要', async () => {
    const id = '0198f36e-f7a7-7c52-9cbb-774e67411212';
    const plan = { planId: id, previewRunId: id, workspaceId: id, workspaceRevision: 0, generatorVersion: '2026.10.24.1',
      planSha256: 'a'.repeat(64), expiresAtUtc: '2099-01-01T00:00:00Z', files: [
        { relativePath: '.fullnet/codegeneration-manifest.json', kind: 'manifest', beforeSha256: null, afterSha256: 'b'.repeat(64) }]
    };
    requestMock.mockResolvedValueOnce(plan);
    expect(await prepareTrackedCodeGenerationApply({ previewRunId: id })).toEqual(plan);
    expect(requestMock).toHaveBeenCalledWith('/api/v1/code-generation/runs/apply-plan',
      expect.objectContaining({ method: 'POST', body: JSON.stringify({ previewRunId: id }) }), undefined);
  });

  it('applies tracked preview with strict summary validation', async () => {

    const previewRunId = '0198f36e-f7a7-7c52-9cbb-774e67411212';

    requestMock.mockResolvedValueOnce({

      runId: '0198f36e-f7a7-7c52-9cbb-774e67411213',

      previewRunId,

      artifactCount: 8,

      changedArtifactCount: 3,

      publicationStatus: 'disabled',

      commitSha: null,

      manifestSha256: 'b'.repeat(64)

    });



    const request = { planId: previewRunId, planSha256: 'a'.repeat(64), idempotencyKey: previewRunId };
    await applyTrackedCodeGeneration(request);



    expect(requestMock).toHaveBeenCalledWith(

      '/api/v1/code-generation/runs/apply',

      expect.objectContaining({

        method: 'POST',

        body: JSON.stringify(request)

      }),

      undefined

    );

  });



  it('previews tracked generation and validates wrapped response', async () => {

    requestMock.mockResolvedValueOnce({

      runId: '0198f36e-f7a7-7c52-9cbb-774e67411212',

      preview: {

        databaseTableName: 'acme_catalog_product',

        readPermission: 'catalog.products.read',

        writePermission: 'catalog.products.write',

        artifacts: []

      }

    });



    await previewTrackedCodeGeneration({

      templateId: '0198f36e-f7a7-7c52-9cbb-774e67411205',

      templateVersion: 3

    });



    expect(requestMock).toHaveBeenCalledWith(

      '/api/v1/code-generation/runs/preview',

      expect.objectContaining({ method: 'POST' }),

      undefined

    );

  });



  it('encodes status filter and rejects run pages containing source content', async () => {

    requestMock.mockResolvedValueOnce({

      items: [{ ...runSummary, content: 'must not be accepted' }],

      page: 1,

      pageSize: 20,

      total: 1

    });



    await expect(listCodeGenerationRuns('failed'))

      .rejects.toThrow('client.invalid_code_generation_run_page');

    expect(requestMock).toHaveBeenCalledWith(

      '/api/v1/code-generation/runs?page=1&pageSize=20&status=failed',

      { method: 'GET' },

      undefined

    );

  });



  it('rolls back a single apply run with strict summary validation', async () => {

    const applyRunId = '0198f36e-f7a7-7c52-9cbb-774e67411213';

    requestMock.mockResolvedValueOnce({

      runId: '0198f36e-f7a7-7c52-9cbb-774e67411215',

      applyRunId,

      artifactCount: 0,

      changedArtifactCount: 2,

      publicationStatus: 'disabled',

      commitSha: null,

      manifestSha256: 'c'.repeat(64)

    });



    await rollbackTrackedCodeGeneration({ applyRunId });



    expect(requestMock).toHaveBeenCalledWith(

      '/api/v1/code-generation/runs/rollback',

      expect.objectContaining({

        method: 'POST',

        body: JSON.stringify({ applyRunId })

      }),

      undefined

    );

  });



  it('uses rollback-chain when multiple applies must be rolled back', async () => {

    const newest = '0198f36e-f7a7-7c52-9cbb-774e67411216';

    const target = '0198f36e-f7a7-7c52-9cbb-774e67411215';

    requestMock.mockResolvedValueOnce({

      rollbacks: [

        {

          runId: '0198f36e-f7a7-7c52-9cbb-774e67411220',

          applyRunId: newest,

          artifactCount: 1,

          changedArtifactCount: 1,

          publicationStatus: 'disabled',

          commitSha: null,

          manifestSha256: 'd'.repeat(64)

        },

        {

          runId: '0198f36e-f7a7-7c52-9cbb-774e67411221',

          applyRunId: target,

          artifactCount: 0,

          changedArtifactCount: 1,

          publicationStatus: 'disabled',

          commitSha: null,

          manifestSha256: 'c'.repeat(64)

        }

      ]

    });



    await executeTrackedCodeGenerationRollback([
      {
        ...runSummary,
        id: newest,
        operationKind: 'apply',
        startedAtUtc: '2026-08-02T08:00:02Z',
        finishedAtUtc: '2026-08-02T08:00:02Z'
      } as CodeGenerationRunResponse,
      {
        ...runSummary,
        id: target,
        operationKind: 'apply',
        startedAtUtc: '2026-08-02T08:00:01Z',
        finishedAtUtc: '2026-08-02T08:00:01Z'
      } as CodeGenerationRunResponse
    ], target);



    expect(requestMock).toHaveBeenCalledWith(

      '/api/v1/code-generation/runs/rollback-chain',

      expect.objectContaining({

        method: 'POST',

        body: JSON.stringify({ applyRunIds: [newest, target] })

      }),

      undefined

    );

  });

});


