import { beforeEach, expect, it, vi } from 'vitest';
import { http } from './http';
import { getPrintingTemplate, getPrintingTemplateVersion, listPrintingTemplateVersions, updatePrintingTemplate, publishPrintingTemplate, getPrintingFormSchema } from './printing-templates';
vi.mock('./http', () => ({ http: { request: vi.fn() } }));
const id = '019bc2b1-2a40-7cc3-8992-a80de51bf299', other = '019bc2b1-2a40-7cc3-8992-a80de51bf300';
const draft = () => ({ id, templateKey: 'layout-one', name: 'Layout', formSchemaKey: 'printing.tenant_profile_card', layoutHtml: '<h1>{{tenantName}}</h1>',
  latestPublishedVersionNumber: 2, isEnabled: true, createdAtUtc: '2026-11-03T00:00:00Z', updatedAtUtc: null, version: 8 });
const published = () => ({ id: other, templateId: id, versionNumber: 2, layoutHtml: '<h1>Published</h1>', changeNote: null,
  publishedByUserId: other, publishedAtUtc: '2026-11-03T00:00:00Z' });
beforeEach(() => { vi.mocked(http.request).mockReset(); });
it('草稿读取和更新使用生成协议及当前读取版本', async () => {
  const signal = new AbortController().signal; vi.mocked(http.request).mockResolvedValue(draft());
  expect((await getPrintingTemplate(id, signal)).layoutHtml).toContain('{{tenantName}}');
  await updatePrintingTemplate(id, { name: 'Updated', layoutHtml: '<p>New</p>', isEnabled: false, version: 7 }, signal);
  const call = vi.mocked(http.request).mock.lastCall!; expect(call[0]).toBe(`/api/v1/printing/templates/${id}`); expect(call[1]?.method).toBe('PUT');
  expect(JSON.parse(String(call[1]?.body))).toMatchObject({ version: 7, isEnabled: false }); expect(call[2]).toBe(signal);
});
it('发布返回不可变版本，选版式读取绑定同一模板和指定发布号', async () => {
  vi.mocked(http.request).mockResolvedValue(published()); const signal = new AbortController().signal;
  expect((await publishPrintingTemplate(id, { version: 8, changeNote: 'Reviewed' }, signal)).versionNumber).toBe(2);
  const call = vi.mocked(http.request).mock.lastCall!; expect(call[0]).toBe(`/api/v1/printing/templates/${id}/publish`); expect(JSON.parse(String(call[1]?.body))).toEqual({ version: 8, changeNote: 'Reviewed' });
  expect((await getPrintingTemplateVersion(id, 2, signal)).layoutHtml).toBe('<h1>Published</h1>'); expect(vi.mocked(http.request).mock.lastCall?.[0]).toBe(`/api/v1/printing/templates/${id}/versions/2`);
});
it('历史按二十条摘要分页，不把目录版本当作版式正文', async () => {
  const { layoutHtml: _, ...summary } = published(); vi.mocked(http.request).mockResolvedValue({ items: [summary], page: 2, pageSize: 20, total: 21 });
  const result = await listPrintingTemplateVersions(id, 2); expect(result.items[0]).not.toHaveProperty('layoutHtml');
  const url = new URL(String(vi.mocked(http.request).mock.lastCall![0]), 'https://example.test'); expect(url.searchParams.get('page')).toBe('2'); expect(url.searchParams.get('pageSize')).toBe('20');
});
it('错误模板或发布版本的响应不能替换当前选择', async () => {
  vi.mocked(http.request).mockResolvedValue({ ...draft(), id: other }); await expect(getPrintingTemplate(id)).rejects.toThrow();
  vi.mocked(http.request).mockResolvedValue({ ...published(), templateId: other }); await expect(publishPrintingTemplate(id, { version: 8 })).rejects.toThrow();
  vi.mocked(http.request).mockResolvedValue({ ...published(), versionNumber: 1 }); await expect(getPrintingTemplateVersion(id, 2)).rejects.toThrow();
  vi.mocked(http.request).mockResolvedValue({ items: [{ ...published(), templateId: other }], page: 1, pageSize: 20, total: 1 }); await expect(listPrintingTemplateVersions(id)).rejects.toThrow();
});
it('发布版本冲突保留原错误，不在适配器自动重放', async () => {
  const error = { status: 409, code: 'printing.template.concurrency_conflict' }; vi.mocked(http.request).mockRejectedValue(error);
  await expect(publishPrintingTemplate(id, { version: 8 })).rejects.toEqual(error); expect(http.request).toHaveBeenCalledTimes(1);
});
it('字段目录通过生成操作读取并绑定请求的 Schema 和取消信号', async () => {
  const signal = new AbortController().signal, schema = { formSchemaKey: 'printing.tenant_profile_card', displayName: '目录', description: '', fields: [{ fieldKey: 'tenantName', displayName: '租户名称' }] };
  vi.mocked(http.request).mockResolvedValue(schema); expect(await getPrintingFormSchema(schema.formSchemaKey, signal)).toEqual(schema);
  expect(vi.mocked(http.request).mock.lastCall?.[0]).toBe('/api/v1/printing/form-schemas/printing.tenant_profile_card'); expect(vi.mocked(http.request).mock.lastCall?.[2]).toBe(signal);
});
it('拒绝错误 Schema、重复或动态语法字段以及无界目录', async () => {
  const schema = { formSchemaKey: 'printing.tenant_profile_card', displayName: '目录', description: '', fields: [{ fieldKey: 'tenantName', displayName: '租户名称' }] };
  for (const value of [{ ...schema, formSchemaKey: 'printing.other' }, { ...schema, fields: [...schema.fields, ...schema.fields] },
    { ...schema, fields: [{ fieldKey: 'tenant.name<script>', displayName: '' }] }, { ...schema, fields: Array.from({ length: 65 }, (_, index) => ({ fieldKey: `field${index}`, displayName: '' })) }]) {
    vi.mocked(http.request).mockResolvedValue(value); await expect(getPrintingFormSchema(schema.formSchemaKey)).rejects.toThrow('client.invalid_printing_form_schema');
  }
});
