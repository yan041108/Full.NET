import { describe, expect, it } from 'vitest';
import { isHostFile, isHostFilePage } from '../src/host-files';

describe('host-files contracts', () => {
  const sample = {
    id: '01912345-6789-7abc-8def-0123456789ab',
    originalFileName: 'parity.txt',
    contentType: 'text/plain',
    sizeBytes: 12,
    contentHash: 'a'.repeat(64),
    createdAtUtc: '2026-07-26T00:00:00Z',
    createdByUserId: '01912345-6789-7abc-8def-0123456789ac'
  };

  it('accepts valid host file payloads', () => {
    expect(isHostFile(sample)).toBe(true);
    expect(isHostFilePage({
      items: [sample],
      page: 1,
      pageSize: 20,
      total: 1
    })).toBe(true);
  });

  it('rejects invalid ids', () => {
    expect(isHostFile({ ...sample, id: 'not-a-guid' })).toBe(false);
  });
});
