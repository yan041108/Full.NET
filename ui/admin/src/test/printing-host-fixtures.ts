import type { PrintingTemplate, PrintingTemplateVersion } from '@fullnet/client-contracts';
import { outputId, printTemplate } from './data-output-fixtures';
export const hostPrintingDraft: PrintingTemplate = { ...printTemplate, latestPublishedVersionNumber: 0 };
export function hostPrintingVersion(template: PrintingTemplate = hostPrintingDraft): PrintingTemplateVersion {
  return { id: '019bc2b1-2a40-7cc3-8992-a80de51bf294', templateId: template.id,
    versionNumber: template.latestPublishedVersionNumber + 1, layoutHtml: template.layoutHtml, changeNote: null,
    publishedByUserId: outputId, publishedAtUtc: template.createdAtUtc };
}
