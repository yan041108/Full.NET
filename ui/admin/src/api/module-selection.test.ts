import { beforeEach, describe, expect, it, vi } from 'vitest';
import { http } from './http';
import {
  getModuleSelectionRuntime,
  validateModuleSelection
} from './module-selection';

vi.mock('./http', () => ({
  http: {
    request: vi.fn(),
    requestBlob: vi.fn()
  }
}));

const requestMock = vi.mocked(http.request);

const analysis = {
  isValid: true,
  sourceKind: 'preset',
  preset: 'Full',
  enabledModuleKeys: ['Identity'],
  officialModuleKeys: ['Identity', 'Document'],
  issues: [],
  modules: [{
    moduleKey: 'Identity',
    isEnabled: true,
    dependencies: [],
    missingDependencies: []
  }],
  deploymentNotice: 'restart required'
};

describe('module-selection api', () => {
  beforeEach(() => requestMock.mockReset());

  it('reads runtime and validates candidate configuration', async () => {
    requestMock
      .mockResolvedValueOnce(analysis)
      .mockResolvedValueOnce({ ...analysis, isValid: false, issues: [{
        code: 'modules.missing_dependency',
        message: 'missing Files',
        moduleKey: 'Document',
        relatedModuleKey: 'Files'
      }] });

    await getModuleSelectionRuntime();
    await validateModuleSelection({ preset: 'Minimal' });

    expect(requestMock).toHaveBeenNthCalledWith(
      1,
      '/api/v1/identity/modules/selection/runtime',
      { method: 'GET' },
      undefined
    );
    expect(requestMock).toHaveBeenNthCalledWith(
      2,
      '/api/v1/identity/modules/selection/validate',
      expect.objectContaining({
        method: 'POST',
        body: JSON.stringify({ enabled: null, preset: 'Minimal' })
      }),
      undefined
    );
  });
});
