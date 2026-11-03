import type { WorkflowDefinitionVersionResponse, WorkflowFormSchema } from '@fullnet/client-contracts';
import type { WorkflowVersionForm } from './workflow-version-comparison';

export const comparisonIds = { definition: '018f0000-0000-7000-8000-000000000001',
  before: '018f0000-0000-7000-8000-000000000011', after: '018f0000-0000-7000-8000-000000000012',
  form: '018f0000-0000-7000-8000-000000000021', nextForm: '018f0000-0000-7000-8000-000000000022' };
export interface ComparisonNode { nodeKey: string; nodeTypeKey: string; nodeSchemaVersion: number; config: Record<string, unknown>; awaitActivityNodeKeys?: string[] }
export function comparisonDraft(): { schemaVersion: number; workflowKindKey: string; nodes: ComparisonNode[] } {
  return { schemaVersion: 1, workflowKindKey: 'approval', nodes: [
    { nodeKey: 'start', nodeTypeKey: 'start', nodeSchemaVersion: 1, config: { nextNodeKeys: ['review'] } },
    { nodeKey: 'review', nodeTypeKey: 'human.approval', nodeSchemaVersion: 1,
      config: { nextNodeKeys: ['end'], nodeName: 'Review', fieldPolicies: { title: 'editable' } } },
    { nodeKey: 'end', nodeTypeKey: 'end', nodeSchemaVersion: 1, config: {} }
  ] };
}
export function comparisonVersion(id: string, canonicalJson = JSON.stringify(comparisonDraft())): WorkflowDefinitionVersionResponse {
  return { id, definitionId: comparisonIds.definition, formVersionId: comparisonIds.form, canonicalJson, contentHash: 'a'.repeat(64),
    schemaVersion: 1, businessTitleTemplate: null, versionNumber: id === comparisonIds.before ? 1 : 2,
    publishedById: comparisonIds.definition, publishedAtUtc: '2026-10-29T00:00:00Z' };
}
export function comparisonForm(id = comparisonIds.form, maximum = 128): WorkflowVersionForm {
  const schema: WorkflowFormSchema = { schemaVersion: 1, adapterVersion: 1, sections: [{ sectionKey: 'main', fields: [
    { fieldKey: 'title', fieldTypeKey: 'text', required: false, constraints: { minLength: 0, maxLength: maximum } }
  ] }] };
  return { schema, version: { id, formDefinitionId: comparisonIds.definition, formSchemaJson: JSON.stringify(schema), webRenderSchemaJson: '{}',
    contentHash: 'b'.repeat(64), versionNumber: 1, schemaVersion: 1, adapterVersion: 1, componentCatalogVersion: 1,
    publishedById: comparisonIds.definition, publishedAtUtc: '2026-10-29T00:00:00Z' } };
}
