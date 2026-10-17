import { beforeEach, describe, expect, it, vi } from 'vitest';
import { http } from './http';
import { getDocumentVersionRetentionSettings, updateDocumentVersionRetentionSettings } from './document-version-retention';
import { deferred } from '../test/data-output-fixtures';
vi.mock('./http', () => ({ http: { request: vi.fn() } }));
const settings = { minimumRetainedVersionsPerItem: 1, maximumRetainedHistoryVersions: 0, pollSeconds: 300, batchSize: 50, version: 0 };
beforeEach(() => vi.resetAllMocks());
describe('文档保留策略适配边界', () => {
  it('保存携带读取版本，成功响应必须递增一次', async () => {
    const input = { ...settings, version: 7 };
    const response = { ...input, version: 8 };
    vi.mocked(http.request).mockResolvedValue(response);
    await expect(updateDocumentVersionRetentionSettings(input)).resolves.toEqual(response);
    expect(JSON.parse(vi.mocked(http.request).mock.calls[0]?.[1]?.body as string)).toEqual(input);
  });
  it.each([undefined, -1, 1.5, NaN, Number.MAX_SAFE_INTEGER])('无效版本%s不能写入', async version => {
    const input = { ...settings, version };
    await expect(updateDocumentVersionRetentionSettings(input as never)).rejects.toThrow(/^client.invalid_document_version_retention_input$/);
    expect(http.request).not.toHaveBeenCalled();
  });
  it('读取缺失版本不能成为可保存的快照', async () => {
    const { version: _version, ...withoutVersion } = settings;
    vi.mocked(http.request).mockResolvedValue(withoutVersion);
    await expect(getDocumentVersionRetentionSettings()).rejects.toThrow(/^client.invalid_(?:document_version_retention_settings|host_document_version_retention_settings_response)$/);
  });
  it('保存返回原版本不能认证成功', async () => {
    const input = { ...settings, version: 7 };
    vi.mocked(http.request).mockResolvedValue(input);
    await expect(updateDocumentVersionRetentionSettings(input)).rejects.toThrow(/^client.invalid_document_version_retention_settings$/);
  });
  it('合法上下限与取消信号可传递', async () => { const signal = new AbortController().signal; for (const value of [settings, { minimumRetainedVersionsPerItem: 1000, maximumRetainedHistoryVersions: 10000, pollSeconds: 86400, batchSize: 1000, version: Number.MAX_SAFE_INTEGER - 1 }, { ...settings, pollSeconds: 60, batchSize: 1 }]) { const response = { ...value, version: value.version + 1 }; vi.mocked(http.request).mockResolvedValue(response); await expect(updateDocumentVersionRetentionSettings(value, signal)).resolves.toEqual(response); expect(vi.mocked(http.request).mock.calls.at(-1)?.[2]).toBe(signal); } });
  it.each([{ field: 'minimumRetainedVersionsPerItem', value: 0 }, { field: 'minimumRetainedVersionsPerItem', value: 1001 }, { field: 'maximumRetainedHistoryVersions', value: -1 }, { field: 'maximumRetainedHistoryVersions', value: 10001 }, { field: 'pollSeconds', value: 59 }, { field: 'pollSeconds', value: 86401 }, { field: 'batchSize', value: 0 }, { field: 'batchSize', value: 1001 }, { field: 'batchSize', value: NaN }, { field: 'batchSize', value: 1.5 }, { field: 'batchSize', value: undefined }])('无效%j在发送前拒绝', async ({ field, value }) => { vi.mocked(http.request).mockResolvedValue(settings); await expect(updateDocumentVersionRetentionSettings({ ...settings, [field]: value })).rejects.toThrow(/^client.invalid_document_version_retention_input$/); expect(http.request).not.toHaveBeenCalled(); });
  it('读取拒绝超出服务端范围的设置', async () => { vi.mocked(http.request).mockResolvedValue({ ...settings, pollSeconds: 1 }); await expect(getDocumentVersionRetentionSettings()).rejects.toThrow(/^client.invalid_document_version_retention_settings$/); });
  it.each(Object.keys(settings).filter(key => key !== 'version'))('保存响应%s不一致不能认证成功', async field => { vi.mocked(http.request).mockResolvedValue({ ...settings, version: 1, [field]: settings[field as keyof typeof settings] + 1 }); await expect(updateDocumentVersionRetentionSettings(settings)).rejects.toThrow(/^client.invalid_document_version_retention_settings$/); });
  it('发送使用冻结副本，调用方迟改不影响合法回包', async () => { const pending = deferred<unknown>(); vi.mocked(http.request).mockReturnValue(pending.promise); const mutable = { ...settings }; const result = updateDocumentVersionRetentionSettings(mutable); mutable.batchSize = 99; expect(JSON.parse(vi.mocked(http.request).mock.calls[0]?.[1]?.body as string)).toEqual(settings); const response = { ...settings, version: 1 }; pending.resolve(response); await expect(result).resolves.toEqual(response); });
  it('超出安全整数的读取不能成为编辑凭据', async () => { vi.mocked(http.request).mockResolvedValue({ ...settings, version: Number.MAX_SAFE_INTEGER + 1 }); await expect(getDocumentVersionRetentionSettings()).rejects.toThrow(/^client.invalid_(?:document_version_retention_settings|host_document_version_retention_settings_response)$/); });
  it('调用方迟改不能把不同回包迁移为成功', async () => { const pending = deferred<unknown>(); vi.mocked(http.request).mockReturnValue(pending.promise); const mutable = { ...settings }; const result = updateDocumentVersionRetentionSettings(mutable); mutable.batchSize = 99; pending.resolve(mutable); await expect(result).rejects.toThrow(/^client.invalid_document_version_retention_settings$/); });
});
