import { describe, expect, it } from 'vitest';
import type { PrintingPublishedTemplateResponse } from '@fullnet/client-contracts';
import { resolvePrintingRecordLink } from './record-print-link';

const id = '01980000-0000-7000-8000-000000000002';
const template: PrintingPublishedTemplateResponse = {
  templateId: id, templateKey: 'request', templateName: 'Request',
  formSchemaKey: 'enterprise_request.request_summary', versionNumber: 1, requiresRecordId: true
};
describe('业务打印链接只定位已获授的记录模板', () => {
  it('普通目录访问保持手工选择', () => {
    expect(resolvePrintingRecordLink({}, [template])).toEqual({ kind: 'none' });
  });
  it.each([
    { formSchemaKey: template.formSchemaKey },
    { recordId: id },
    { formSchemaKey: [template.formSchemaKey], recordId: id },
    { formSchemaKey: template.formSchemaKey, recordId: [id] },
    { formSchemaKey: template.formSchemaKey, recordId: null },
    { formSchemaKey: template.formSchemaKey, recordId: 'not-a-uuid' },
    { formSchemaKey: template.formSchemaKey, recordId: '00000000-0000-0000-0000-000000000000' },
    { formSchemaKey: 'unknown.schema', recordId: id }
  ])('缺失、畸形或未获授链接不能回退到其他模板：%j', query => {
    expect(resolvePrintingRecordLink(query, [template])).toEqual({ kind: 'invalid' });
  });
  it('仅保留同表单的已授记录模板及各自精确版本', () => {
    const values = [template, { ...template, versionNumber: 2 }, { ...template, requiresRecordId: false },
      { ...template, formSchemaKey: 'other.schema' }];
    expect(resolvePrintingRecordLink({ formSchemaKey: template.formSchemaKey, recordId: id }, values))
      .toEqual({ kind: 'bound', recordId: id, templates: values.slice(0, 2) });
  });
  it('合法 UUID 规范化，不使用 URL 提供的模板 ID 读取未授模板', () => {
    expect(resolvePrintingRecordLink({ formSchemaKey: template.formSchemaKey, recordId: id.toUpperCase(), templateId: 'foreign' }, [template]))
      .toEqual({ kind: 'bound', recordId: id, templates: [template] });
  });
});
