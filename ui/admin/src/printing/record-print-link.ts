import type { PrintingPublishedTemplateResponse } from '@fullnet/client-contracts';

/** 链接与手工输入共用 UUID 边界，空 UUID 不能指向业务记录。 */
export function isPrintingRecordId(value: string): boolean {
  return /^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/i.test(value)
    && value !== '00000000-0000-0000-0000-000000000000';
}

type RecordPrintLink = { kind: 'none' } | { kind: 'invalid' }
  | { kind: 'bound'; recordId: string; templates: PrintingPublishedTemplateResponse[] };

/** URL 只定位目录中已经获授的记录表单；不能提供模板授权或自动触发读取。 */
export function resolvePrintingRecordLink(query: Record<string, unknown>, templates: readonly PrintingPublishedTemplateResponse[]): RecordPrintLink {
  const schema = query.formSchemaKey;
  const record = query.recordId;
  if (schema === undefined && record === undefined) return { kind: 'none' };
  if (typeof schema !== 'string' || typeof record !== 'string' || !isPrintingRecordId(record)) return { kind: 'invalid' };
  const matching = templates.filter(template => template.formSchemaKey === schema && template.requiresRecordId === true);
  return matching.length ? { kind: 'bound', templates: matching, recordId: record.toLowerCase() } : { kind: 'invalid' };
}
