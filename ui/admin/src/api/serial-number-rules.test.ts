import { beforeEach, describe, expect, it, vi } from 'vitest';
import { http } from './http';
import {
  createSerialNumberRule,
  disableSerialNumberRule,
  enableSerialNumberRule,
  listSerialNumberRules,
  previewSerialNumber,
  updateSerialNumberRule
} from './serial-number-rules';

vi.mock('./http', () => ({
  http: {
    request: vi.fn(),
    requestBlob: vi.fn()
  }
}));
const requestMock = vi.mocked(http.request);

const rule = {
  id: '0198f36e-f7a7-7c52-9cbb-774e67411205',
  ruleKey: 'order-no',
  displayName: 'Order Number',
  description: null,
  scope: 1 as const,
  resetInterval: 1 as const,
  pattern: '{date:yyyyMMdd}-{seq:4}',
  minimumValue: 1,
  maximumValue: 9999,
  displayOrder: 0,
  isEnabled: true,
  createdAtUtc: '2026-07-30T08:00:00Z',
  createdByUserId: '0198f36e-f7a7-7c52-9cbb-774e67411204',
  updatedAtUtc: null,
  updatedByUserId: null,
  version: 1
};

describe('serial-number-rules api', () => {
  beforeEach(() => requestMock.mockReset());

  it('lists, creates, updates, toggles status and previews serial numbers', async () => {
    requestMock
      .mockResolvedValueOnce({
        items: [rule],
        page: 1,
        pageSize: 20,
        total: 1
      })
      .mockResolvedValueOnce(rule)
      .mockResolvedValueOnce({ ...rule, version: 2 })
      .mockResolvedValueOnce({ ...rule, isEnabled: false, version: 3 })
      .mockResolvedValueOnce({ ...rule, isEnabled: true, version: 4 })
      .mockResolvedValueOnce({ value: '20260730-0001' });

    await listSerialNumberRules();
    await createSerialNumberRule({
      ruleKey: rule.ruleKey,
      displayName: rule.displayName,
      description: null,
      scope: rule.scope,
      resetInterval: rule.resetInterval,
      pattern: rule.pattern,
      minimumValue: rule.minimumValue,
      maximumValue: rule.maximumValue,
      displayOrder: rule.displayOrder,
      isEnabled: rule.isEnabled
    });
    await updateSerialNumberRule(rule.id, {
      displayName: rule.displayName,
      description: null,
      scope: rule.scope,
      resetInterval: rule.resetInterval,
      pattern: rule.pattern,
      minimumValue: rule.minimumValue,
      maximumValue: rule.maximumValue,
      displayOrder: rule.displayOrder,
      isEnabled: rule.isEnabled,
      version: 1
    });
    await disableSerialNumberRule(rule.id, { version: 2 });
    await enableSerialNumberRule(rule.id, { version: 3 });
    await previewSerialNumber({
      scope: 1,
      pattern: rule.pattern,
      tenantIdentifier: 'tenant-a',
      sequenceValue: 1,
      atUtc: '2026-07-30T08:00:00Z'
    });

    expect(requestMock).toHaveBeenNthCalledWith(
      1,
      '/api/v1/serial-numbers/rules?page=1&pageSize=20',
      { method: 'GET' },
      undefined
    );
    expect(requestMock).toHaveBeenNthCalledWith(
      2,
      '/api/v1/serial-numbers/rules',
      expect.objectContaining({ method: 'POST' }),
      undefined
    );
    expect(requestMock).toHaveBeenNthCalledWith(
      3,
      `/api/v1/serial-numbers/rules/${rule.id}`,
      expect.objectContaining({ method: 'PUT' }),
      undefined
    );
    expect(requestMock).toHaveBeenNthCalledWith(
      4,
      `/api/v1/serial-numbers/rules/${rule.id}/disable`,
      expect.objectContaining({
        method: 'POST',
        body: JSON.stringify({ version: 2 })
      }),
      undefined
    );
    expect(requestMock).toHaveBeenNthCalledWith(
      5,
      `/api/v1/serial-numbers/rules/${rule.id}/enable`,
      expect.objectContaining({
        method: 'POST',
        body: JSON.stringify({ version: 3 })
      }),
      undefined
    );
    expect(requestMock).toHaveBeenNthCalledWith(
      6,
      '/api/v1/serial-numbers/rules/preview',
      expect.objectContaining({ method: 'POST' }),
      undefined
    );
  });
});
