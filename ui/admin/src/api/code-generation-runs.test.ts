import { beforeEach, describe, expect, it, vi } from 'vitest';

import { http } from './http';

import {
  applyTrackedCodeGeneration,
  executeTrackedCodeGenerationRollback,
  listCodeGenerationRuns,
  previewTrackedCodeGeneration,
  rollbackTrackedCodeGeneration,
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

  sourceApplyRunId: null

};



describe('code-generation-runs api', () => {

  beforeEach(() => requestMock.mockReset());



  it('applies tracked preview with strict summary validation', async () => {

    const previewRunId = '0198f36e-f7a7-7c52-9cbb-774e67411212';

    requestMock.mockResolvedValueOnce({

      runId: '0198f36e-f7a7-7c52-9cbb-774e67411213',

      previewRunId,

      artifactCount: 8,

      changedArtifactCount: 3,

      manifestSha256: 'b'.repeat(64)

    });



    await applyTrackedCodeGeneration({ previewRunId });



    expect(requestMock).toHaveBeenCalledWith(

      '/api/v1/code-generation/runs/apply',

      expect.objectContaining({

        method: 'POST',

        body: JSON.stringify({ previewRunId })

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


