import { beforeEach, describe, expect, it, vi } from 'vitest';
import { http, request } from './http';
import { previewPrintingPublishedTemplate, previewPrintingTemplate } from './printing-templates';
import { deferred, outputId, printResult } from '../test/data-output-fixtures';
vi.mock('./http', () => ({ http: { request: vi.fn() }, request: vi.fn() }));
beforeEach(() => vi.resetAllMocks());
describe('打印预览响应身份', () => {
  it.each(['host', 'tenant'])('%s 预览拒绝其他模板与错误版本', async kind => {
    const mock = kind === 'host' ? vi.mocked(request) : vi.mocked(http.request), preview = kind === 'host' ? previewPrintingTemplate : previewPrintingPublishedTemplate;
    mock.mockResolvedValueOnce({ ...printResult, templateId: '01980000-0000-7000-8000-000000000003' }); await expect(preview(outputId, { versionNumber: 1 })).rejects.toThrow(/^client.invalid_printing_template_preview$/u);
    mock.mockResolvedValueOnce({ ...printResult, versionNumber: 2 }); await expect(preview(outputId, { versionNumber: 1 })).rejects.toThrow(/^client.invalid_printing_template_preview$/u);
  });
  it.each(['host', 'tenant'])('%s 请求版本被冻结并接受UUID大小写等价', async kind => {
    const mock = kind === 'host' ? vi.mocked(request) : vi.mocked(http.request), preview = kind === 'host' ? previewPrintingTemplate : previewPrintingPublishedTemplate;
    const pending = deferred<typeof printResult>(); mock.mockReturnValueOnce(pending.promise); const body = { versionNumber: 1 }, signal = new AbortController().signal;
    const action = preview(outputId, body, signal); body.versionNumber = 2; pending.resolve({ ...printResult, templateId: outputId.toUpperCase() }); await expect(action).resolves.toMatchObject({ versionNumber: 1 }); expect(mock.mock.calls[0]?.[2]).toBe(signal);
  });
});
