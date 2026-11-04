import { describe, expect, it, vi } from 'vitest';
import { documentHostGetVersionRetentionSettings, documentHostUpdateVersionRetentionSettings, isHostDocumentVersionRetentionSettingsResponse, isUpdateHostDocumentVersionRetentionRequest } from '../src/index';
import { readHostDocumentVersionRetentionSettingsResponse } from '../src/generated/guards.generated';

describe('document retention revision credentials', () => {
  const settings = { minimumRetainedVersionsPerItem: 1, maximumRetainedHistoryVersions: 0, pollSeconds: 300, batchSize: 50, version: 7 };
  it.each([0, 1, Number.MAX_SAFE_INTEGER - 1])('accepts writable version %s', version => {
    expect(isUpdateHostDocumentVersionRetentionRequest({ ...settings, version })).toBe(true);
  });
  it.each([undefined, -1, 1.5, NaN, Infinity, Number.MAX_SAFE_INTEGER + 1])('rejects invalid revision %s', version => {
    expect(isHostDocumentVersionRetentionSettingsResponse({ ...settings, version })).toBe(false);
    expect(isUpdateHostDocumentVersionRetentionRequest({ ...settings, version })).toBe(false);
  });
  it('can read the largest safe revision but cannot increment it in a request', () => {
    const exhausted = { ...settings, version: Number.MAX_SAFE_INTEGER };
    expect(isHostDocumentVersionRetentionSettingsResponse(exhausted)).toBe(true);
    expect(isUpdateHostDocumentVersionRetentionRequest(exhausted)).toBe(false);
  });
  it.each([undefined, Number.MAX_SAFE_INTEGER + 1])('generated response guard rejects unusable revision %s', version => {
    expect(() => readHostDocumentVersionRetentionSettingsResponse({ ...settings, version })).toThrow('client.invalid_host_document_version_retention_settings_response');
  });
  it('generated reader normalizes a safe numeric revision', () => {
    expect(readHostDocumentVersionRetentionSettingsResponse({ ...settings, version: '7' }).version).toBe(7);
  });
  it('public generated operations expose and transmit the revision', async () => {
    const request = vi.fn().mockResolvedValueOnce(settings).mockResolvedValueOnce({ ...settings, version: 8 });
    const http = { request } as unknown as Parameters<typeof documentHostGetVersionRetentionSettings>[0];
    const result = await documentHostGetVersionRetentionSettings(http, {});
    expect(result.version).toBe(7);
    expect((await documentHostUpdateVersionRetentionSettings(http, { body: result })).version).toBe(8);
    expect(JSON.parse(request.mock.calls[1]?.[1]?.body)).toEqual(settings);
  });
});
