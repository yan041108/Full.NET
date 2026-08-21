import { beforeEach, describe, expect, it, vi } from 'vitest';
import { http } from './http';
import { getIdentityModule, listIdentityModules } from './module-catalog';

vi.mock('./http', () => ({
  http: {
    request: vi.fn(),
    requestBlob: vi.fn()
  }
}));
const requestMock = vi.mocked(http.request);

const sampleModule = {
  moduleKey: 'identity',
  displayName: 'Identity',
  version: '1.0.0',
  dependencies: ['tenancy'],
  hostProfiles: ['Api'],
  sourceClassification: 'official',
  healthCapability: 'ready'
};

describe('Vue Host 模块清单 API', () => {
  beforeEach(() => requestMock.mockReset());

  it('校验模块清单列表响应', async () => {
    requestMock.mockResolvedValueOnce([sampleModule]);

    await expect(listIdentityModules()).resolves.toEqual([sampleModule]);
    expect(requestMock).toHaveBeenCalledWith(
      '/api/v1/identity/modules',
      { method: 'GET' },
      undefined
    );
  });

  it('校验模块详情响应', async () => {
    requestMock.mockResolvedValueOnce(sampleModule);

    await expect(getIdentityModule('identity')).resolves.toEqual(sampleModule);
    expect(requestMock).toHaveBeenCalledWith(
      '/api/v1/identity/modules/identity',
      { method: 'GET' },
      undefined
    );
  });

  it('拒绝无效 moduleKey 的列表项', async () => {
    requestMock.mockResolvedValueOnce([
      { ...sampleModule, moduleKey: 'bad/key' }
    ]);

    await expect(listIdentityModules()).rejects.toThrow(
      'Invalid identity module catalog response.'
    );
  });
});
