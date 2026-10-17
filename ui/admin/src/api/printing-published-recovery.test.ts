import { beforeEach, describe, expect, it, vi } from 'vitest';
import { http } from './http';
import { listPrintingPublishedTemplates, previewPrintingPublishedTemplate } from './printing-templates';
import { deferred, outputId, printResult } from '../test/data-output-fixtures';
vi.mock('./http', () => ({ http: { request: vi.fn() }, request: vi.fn() }));
const template = { templateId: outputId, templateKey: 'fixture', templateName: 'Fixture', formSchemaKey: 'printing.tenant_profile_card', versionNumber: 1 };
beforeEach(() => vi.resetAllMocks());
describe('已授权打印目录和预览输入边界', () => {
  it.each([{ templateId: '00000000-0000-0000-0000-000000000000' }, { versionNumber: 0 }, { versionNumber: 1.5 }, { formSchemaKey: ' ' }, { templateKey: '' }])('拒绝无效目录身份或机器键 %j', async change => {
    vi.mocked(http.request).mockResolvedValue([{ ...template, ...change }]); await expect(listPrintingPublishedTemplates()).rejects.toThrow(/^client.invalid_(?:printing_published_template_list|printing_list_published_templates_response)$/u);
  });
  it('拒绝UUID大小写等价的重复模板版本，允许同模板多个版本', async () => {
    vi.mocked(http.request).mockResolvedValue([template, { ...template, templateId: outputId.toUpperCase() }]); await expect(listPrintingPublishedTemplates()).rejects.toThrow(/^client.invalid_printing_published_template_list$/u);
    vi.mocked(http.request).mockResolvedValue([template, { ...template, versionNumber: 2 }]); await expect(listPrintingPublishedTemplates()).resolves.toHaveLength(2);
  });
  it.each([{ versionNumber: 0 }, { versionNumber: 1.5 }, { recordId: 'bad' }, { recordId: '00000000-0000-0000-0000-000000000000' }])('非法预览目标在HTTP前拒绝 %j', async body => {
    await expect(previewPrintingPublishedTemplate(outputId, body)).rejects.toThrow(/^client.invalid_printing_template_preview_request$/u); expect(http.request).not.toHaveBeenCalled();
  });
  it('空模板身份在HTTP前拒绝', async () => {
    await expect(previewPrintingPublishedTemplate('00000000-0000-0000-0000-000000000000')).rejects.toThrow(/^client.invalid_printing_template_preview_request$/u); expect(http.request).not.toHaveBeenCalled();
  });
  it('记录和版本由发送副本持有，调用方在途改变不影响身份', async () => {
    const pending = deferred<unknown>(); vi.mocked(http.request).mockReturnValueOnce(pending.promise); const payload = { versionNumber: 1, recordId: outputId }, reading = previewPrintingPublishedTemplate(outputId, payload);
    payload.versionNumber = 2; payload.recordId = '01980000-0000-7000-8000-000000000003'; pending.resolve(printResult); await expect(reading).resolves.toEqual(printResult); expect(JSON.parse(vi.mocked(http.request).mock.calls[0]![1]!.body as string)).toEqual({ versionNumber: 1, recordId: outputId });
  });
  it('未指定版本仍可解析最高授权版本，但不接受零版本输出', async () => {
    vi.mocked(http.request).mockResolvedValue(printResult); await expect(previewPrintingPublishedTemplate(outputId)).resolves.toEqual(printResult); vi.mocked(http.request).mockResolvedValue({ ...printResult, versionNumber: 0 }); await expect(previewPrintingPublishedTemplate(outputId)).rejects.toThrow(/^client.invalid_printing_template_preview$/u);
  });
});
