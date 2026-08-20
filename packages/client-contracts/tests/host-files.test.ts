import { describe, expect, it } from 'vitest';
import {
  readHostFileResponse,
  readPagedResultOfHostFileResponse
} from '../src/index';
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

  it('生成守卫拒绝错误 primitive 与分页数组坏项', () => {
    expect(readHostFileResponse(sample)).toEqual(sample);
    expect(() => readHostFileResponse({ ...sample, sizeBytes: '12' }))
      .toThrow('client.invalid_host_file_response');
    expect(() => readPagedResultOfHostFileResponse({
      items: [{ ...sample, createdByUserId: 'bad' }],
      page: 1,
      pageSize: 20,
      total: 1
    })).toThrow('client.invalid_paged_result_of_host_file_response');
  });
});
