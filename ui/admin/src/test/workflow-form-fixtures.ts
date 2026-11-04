import type { WorkflowFormSchema } from '@fullnet/client-contracts';
import type { WorkflowFormResponse, WorkflowFormVersionResponse } from '../api/workflow-forms';
import { outputId } from './data-output-fixtures';
export const workflowFormId = '019bc2b1-2a40-7cc3-8992-a80de51bf295';
export const workflowFormVersionId = '019bc2b1-2a40-7cc3-8992-a80de51bf296';
export const workflowFormDraft = { schemaVersion: 1, adapterVersion: 1, sections: [{ sectionKey: 'main', fields: [{ fieldKey: 'summary', fieldTypeKey: 'text', required: true, constraints: {} }] }] } satisfies WorkflowFormSchema;
export const workflowForm = { id: workflowFormId, formKey: 'purchase.request', draft: workflowFormDraft, draftRevision: 2, latestPublishedVersionId: null, statusKey: 'active', version: 1, createdAtUtc: '2026-10-16T00:00:00Z', updatedAtUtc: null } satisfies WorkflowFormResponse;
export const workflowFormCatalog = { catalogVersion: 1, schemaVersion: 1, adapterVersion: 1, components: [{ fieldTypeKey: 'text', designable: true, publishable: true, executable: true, constraintKeys: ['minLength', 'maxLength'] }] };
export const workflowFormVersion = (form: WorkflowFormResponse = workflowForm): WorkflowFormVersionResponse => ({ id: workflowFormVersionId, formDefinitionId: form.id, versionNumber: 1, schemaVersion: 1, adapterVersion: 1, componentCatalogVersion: 1, formSchemaJson: JSON.stringify(form.draft), webRenderSchemaJson: JSON.stringify(form.draft), contentHash: 'a'.repeat(64), publishedById: outputId, publishedAtUtc: '2026-10-16T00:00:00Z' });
