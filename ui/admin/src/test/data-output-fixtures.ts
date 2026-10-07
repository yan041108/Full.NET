import { createPinia, setActivePinia } from 'pinia';
import type { PrintingTemplate, PrintingTemplatePreview, ReportingDefinition, ReportingExecutionPage, ReportingExportTaskDetail } from '@fullnet/client-contracts';
import { useSessionStore } from '../auth/session';

export const outputId = '019bc2b1-2a40-7cc3-8992-a80de51bf299';
export function createOutputSession(permissions: string[]) {
  const pinia = createPinia(); setActivePinia(pinia);
  const session = useSessionStore(); session.state = 'authenticated';
  session.currentUser = { id: outputId, username: 'reader', displayName: '查看者', tenantId: outputId,
    actorScope: 'tenant', scope: 'tenant', isSuperAdministrator: false, passwordChangeRequired: false,
    permissions, sessionId: outputId, preferredLocale: 'zh-CN', profileVersion: 1 };
  return { pinia, session };
}
export const printTemplate: PrintingTemplate = { id: outputId, templateKey: 'fixture', name: '打印夹具',
  formSchemaKey: 'printing.tenant_profile_card', layoutHtml: '<div>夹具</div>', latestPublishedVersionNumber: 1,
  isEnabled: true, createdAtUtc: '2026-10-07T00:00:00Z', updatedAtUtc: null, version: 1 };
export const printResult: PrintingTemplatePreview = { templateId: outputId, templateKey: 'fixture', templateName: '打印夹具',
  formSchemaKey: printTemplate.formSchemaKey, versionNumber: 1, html: '<div>旧租户敏感内容</div>', boundFields: {}, generatedAtUtc: printTemplate.createdAtUtc };
export const reportDefinition: ReportingDefinition = { id: outputId, groupId: outputId, dataSourceId: outputId,
  definitionKey: 'fixture', name: '报表夹具', description: null, queryPortKey: 'fixture', parameterSchema: [], layoutConfigJson: '{}',
  latestPublishedVersionNumber: 1, isEnabled: true, createdAtUtc: printTemplate.createdAtUtc, updatedAtUtc: null, version: 1 };
export const reportResult: ReportingExecutionPage = { definitionId: outputId, definitionKey: 'fixture', definitionName: '报表夹具',
  versionNumber: 1, queryPortKey: 'fixture', columns: [{ columnKey: 'secret', displayName: '敏感列' }], rows: [{ values: { secret: '旧租户敏感内容' } }],
  page: 1, pageSize: 50, hasMore: false, totalRows: 1, commandTimeoutSeconds: 30, executedAtUtc: printTemplate.createdAtUtc };
export const exportTask: ReportingExportTaskDetail = { id: outputId, definitionId: outputId, definitionKey: 'fixture', definitionName: '报表夹具',
  versionNumber: 1, formatKey: 'excel', statusKey: 'succeeded', rowCount: 1, outputFileName: 'fixture.xlsx', outputFileId: outputId,
  errorCode: null, errorMessage: null, requestedByUserId: outputId, createdAtUtc: printTemplate.createdAtUtc, completedAtUtc: printTemplate.createdAtUtc, parameters: [] };
export function deferred<T>() {
  let resolve!: (value: T) => void; let reject!: (reason?: unknown) => void;
  const promise = new Promise<T>((done, fail) => { resolve = done; reject = fail; });
  return { promise, resolve, reject };
}
