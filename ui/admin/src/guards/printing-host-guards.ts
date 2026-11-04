import { isGuid, isPrintingTemplate, type PrintingTemplate, type PrintingTemplateVersion } from '@fullnet/client-contracts';
const validId = (value: unknown): value is string => isGuid(value) && value !== '00000000-0000-0000-0000-000000000000';
export function validHostPrintingTemplate(value: unknown): value is PrintingTemplate {
  return isPrintingTemplate(value) && validId(value.id) && Number.isSafeInteger(value.version) && value.version > 0
    && Number.isSafeInteger(value.latestPublishedVersionNumber) && value.latestPublishedVersionNumber >= 0
    // 服务端净化可能清空草稿布局；空布局仍是已创建资源，发布校验另行负责。
    && !!value.templateKey.trim() && !!value.formSchemaKey.trim();
}
export function validHostPrintingVersion(value: unknown): value is PrintingTemplateVersion {
  if (value === null || typeof value !== 'object' || Array.isArray(value)) return false;
  const row = value as Record<string, unknown>;
  return validId(row.id) && validId(row.templateId) && Number.isSafeInteger(row.versionNumber) && Number(row.versionNumber) > 0
    && typeof row.layoutHtml === 'string' && !!row.layoutHtml.trim() && (row.changeNote === null || typeof row.changeNote === 'string')
    && validId(row.publishedByUserId) && typeof row.publishedAtUtc === 'string';
}
