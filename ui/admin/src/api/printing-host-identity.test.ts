import { beforeEach, describe, expect, it, vi } from 'vitest';
import { request } from './http';
import { createPrintingTemplate, listPrintingTemplates, previewPrintingTemplate, publishPrintingTemplate } from './printing-templates';
import { deferred, printResult, printTemplate } from '../test/data-output-fixtures';
import { hostPrintingDraft as draft, hostPrintingVersion } from '../test/printing-host-fixtures';
vi.mock('./http', () => ({ http: {}, request: vi.fn() }));
const other = '019bc2b1-2a40-7cc3-8992-a80de51bf293';
const body = () => ({ templateKey: draft.templateKey, name: draft.name, formSchemaKey: draft.formSchemaKey, layoutHtml: '<div onclick="bad()">safe</div>', isEnabled: true });
beforeEach(() => vi.resetAllMocks());
describe('Host 打印目录、创建、发布与预览归属', () => {
  it.each([{ rows: [printTemplate, printTemplate] }, { rows: [{ ...printTemplate, version: 0 }] }, { rows: [{ ...printTemplate, latestPublishedVersionNumber: 0.5 }] }, { rows: [{ ...printTemplate, id: '00000000-0000-0000-0000-000000000000' }] }, { rows: [{ ...printTemplate, formSchemaKey: '' }] }])('拒绝歧义身份和非法修订目录 %j', async ({ rows }) => {
    vi.mocked(request).mockResolvedValue(rows); await expect(listPrintingTemplates()).rejects.toThrow();
  });
  it('创建接受服务端净化后的布局，核对稳定元数据与初始修订', async () => {
    vi.mocked(request).mockResolvedValue({ ...draft, layoutHtml: '<div>safe</div>' }); await expect(createPrintingTemplate(body())).resolves.toMatchObject({ version: 1, latestPublishedVersionNumber: 0 });
  });
  it.each(['', '   '])('净化为空或空白的草稿布局 %j 仍识别创建与目录成功', async layoutHtml => {
    const created = { ...draft, layoutHtml }; vi.mocked(request).mockResolvedValue(created);
    await expect(createPrintingTemplate({ ...body(), layoutHtml: '<script>bad()</script>' })).resolves.toEqual(created);
    vi.mocked(request).mockResolvedValue([printTemplate, { ...created, id: other }]);
    await expect(listPrintingTemplates()).resolves.toHaveLength(2);
  });
  it.each([{ templateKey: 'other' }, { formSchemaKey: 'other' }, { version: 2 }, { latestPublishedVersionNumber: 1 }, { isEnabled: false }])('创建拒绝错误配置或非初始草稿 %j', async change => {
    vi.mocked(request).mockResolvedValue({ ...draft, ...change }); await expect(createPrintingTemplate(body())).rejects.toThrow();
  });
  it('创建冻结实际发送值，调用方后改不会影响回显校验', async () => {
    const pending = deferred<unknown>(); vi.mocked(request).mockReturnValueOnce(pending.promise); const input = body(), action = createPrintingTemplate(input); input.name = 'changed';
    pending.resolve(draft); await expect(action).resolves.toMatchObject({ name: draft.name }); expect(JSON.parse(vi.mocked(request).mock.calls[0]![1]!.body as string).name).toBe(draft.name);
  });
  it.each([{ templateId: other }, { id: '00000000-0000-0000-0000-000000000000' }, { versionNumber: 0 }, { changeNote: 'wrong' }])('发布不再丢弃响应，拒绝错误身份、版本和备注 %j', async change => {
    vi.mocked(request).mockResolvedValue({ ...hostPrintingVersion(), ...change }); await expect(publishPrintingTemplate(draft.id, { version: 1 })).rejects.toThrow();
  });
  it('发布返回服务端的精确发布版本', async () => {
    vi.mocked(request).mockResolvedValue(hostPrintingVersion()); await expect(publishPrintingTemplate(draft.id, { version: 1 })).resolves.toEqual(hostPrintingVersion());
  });
  it.each([0, 0.5, Number.NaN])('无效草稿修订 %s 在发布请求前拒绝', async version => {
    await expect(publishPrintingTemplate(draft.id, { version })).rejects.toThrow(/^client.invalid_printing_template_publish_request$/u); expect(request).not.toHaveBeenCalled();
  });
  it.each([{ versionNumber: 0 }, { boundFields: { field: 3 } }, { templateId: '00000000-0000-0000-0000-000000000000' }])('预览拒绝无效版本、绑定值与空身份 %j', async change => {
    vi.mocked(request).mockResolvedValue({ ...printResult, ...change }); await expect(previewPrintingTemplate(printTemplate.id)).rejects.toThrow();
  });
});
