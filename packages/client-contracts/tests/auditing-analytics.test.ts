import { describe, expect, it } from 'vitest';
import {
  isAuditingDomainChangeDiffQueryResult,
  isAuditingLogTrend
} from '../src/auditing-analytics.js';

describe('auditing analytics contracts', () => {
  it('validates audit log trend response', () => {
    expect(isAuditingLogTrend({
      fromUtc: '2026-09-06T00:00:00.000Z',
      toUtc: '2026-09-06T23:59:59.000Z',
      bucketSizeMinutes: 60,
      buckets: [
        {
          bucketStartUtc: '2026-09-06T00:00:00.000Z',
          eventCount: 12,
          errorCount: 1
        }
      ],
      totalCount: 12,
      bucketLimitReached: false
    })).toBe(true);
  });

  it('validates domain change diff response', () => {
    expect(isAuditingDomainChangeDiffQueryResult({
      traceId: '00-abc',
      entries: [
        {
          auditId: '018f3f2a-1111-7111-8111-111111111111',
          moduleKey: 'settings',
          actionKey: 'settings.diagnostic_policy.update',
          occurredAtUtc: '2026-09-06T10:00:00.000Z',
          availability: 'available',
          fields: [
            {
              fieldKey: 'pressure',
              beforeValue: 'low',
              afterValue: 'high'
            }
          ]
        }
      ]
    })).toBe(true);
  });
});
