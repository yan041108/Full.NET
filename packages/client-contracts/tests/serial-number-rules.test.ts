import { describe, expect, it } from 'vitest';
import {
  isPreviewSerialNumberRequest,
  isSerialNumberPreviewResponse,
  isSerialNumberRulePage,
  isSerialNumberRuleResponse
} from '../src/serial-number-rules';

describe('serial-number-rules contracts', () => {
  const rule = {
    id: '0198f36e-f7a7-7c52-9cbb-774e67411205',
    ruleKey: 'invoice.host',
    displayName: 'Invoice serial',
    description: null,
    scope: 1 as const,
    resetInterval: 1 as const,
    pattern: 'INV-{utc:yyyy}-{tenant}-{sequence:5}',
    minimumValue: 1,
    maximumValue: 99999,
    displayOrder: 10,
    isEnabled: true,
    createdAtUtc: '2026-07-30T08:00:00Z',
    createdByUserId: '0198f36e-f7a7-7c52-9cbb-774e67411204',
    updatedAtUtc: null,
    updatedByUserId: null,
    version: 1
  };

  it('accepts valid serial number rule payloads', () => {
    expect(isSerialNumberRuleResponse(rule)).toBe(true);
    expect(isSerialNumberRulePage({
      items: [rule],
      page: 1,
      pageSize: 20,
      total: 1
    })).toBe(true);
    expect(isSerialNumberPreviewResponse({
      value: 'INV-2026-acme-00042',
      resetBucket: '20260730',
      sequenceValue: 42
    })).toBe(true);
    expect(isPreviewSerialNumberRequest({
      scope: 1,
      pattern: rule.pattern,
      tenantIdentifier: 'acme',
      sequenceValue: 42,
      atUtc: '2026-07-30T00:00:00Z',
      resetInterval: 1
    })).toBe(true);
  });

  it('rejects invalid payloads', () => {
    expect(isSerialNumberRuleResponse({ ...rule, scope: 2 })).toBe(false);
    expect(isSerialNumberPreviewResponse({ value: '' })).toBe(false);
    expect(isSerialNumberPreviewResponse({
      value: 'INV-2026-acme-00042',
      resetBucket: '',
      sequenceValue: 42
    })).toBe(false);
  });
});