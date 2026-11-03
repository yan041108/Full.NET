import { readWorkflowDefinitionDraft, type WorkflowDefinitionVersionResponse, type WorkflowFormVersionResponse, type WorkflowFormSchema } from '@fullnet/client-contracts';

export type WorkflowVersionChangeArea = 'definition' | 'node' | 'field' | 'authorization' | 'binding' | 'presentation';
export interface WorkflowVersionChange {
  readonly area: WorkflowVersionChangeArea;
  readonly kind: 'added' | 'removed' | 'changed';
  readonly path: string;
  readonly before: unknown;
  readonly after: unknown;
}
export interface WorkflowVersionComparison {
  readonly changes: readonly WorkflowVersionChange[];
  readonly totalChanges: number;
  readonly truncated: boolean;
}
export interface WorkflowVersionForm { readonly version: WorkflowFormVersionResponse; readonly schema: WorkflowFormSchema }
const maximumJsonLength = 4 * 1024 * 1024;
const pointer = (key: string) => key.replaceAll('~', '~0').replaceAll('/', '~1');
const record = (value: unknown): value is Record<string, unknown> => value !== null && typeof value === 'object' && !Array.isArray(value);

/** 只比较固定资产的结构，不求值条件、不重新展开成员，也不预测实例能否执行成功。 */
export function compareWorkflowVersions(before: WorkflowDefinitionVersionResponse, after: WorkflowDefinitionVersionResponse,
  beforeForm: WorkflowVersionForm, afterForm: WorkflowVersionForm): WorkflowVersionComparison {
  if (before.definitionId !== after.definitionId || beforeForm.version.id !== before.formVersionId || afterForm.version.id !== after.formVersionId) {
    throw new TypeError('workflow.version_comparison.binding_mismatch');
  }
  function definition(version: WorkflowDefinitionVersionResponse) {
    if (version.canonicalJson.length > maximumJsonLength) throw new TypeError('workflow.version_comparison.too_large');
    const draft = readWorkflowDefinitionDraft(JSON.parse(version.canonicalJson));
    if (draft.nodes.length > 1024 || new Set(draft.nodes.map(node => node.nodeKey)).size !== draft.nodes.length ||
      draft.nodes.some(node => !record(node.config))) throw new TypeError('workflow.version_comparison.invalid_definition');
    return draft;
  }
  const left = definition(before), right = definition(after);
  const changes: WorkflowVersionChange[] = []; let totalChanges = 0;
  function add(area: WorkflowVersionChangeArea, path: string, old: unknown, next: unknown): void {
    totalChanges++;
    if (changes.length < 512) changes.push({ area, path, before: old, after: next,
      kind: old === undefined ? 'added' : next === undefined ? 'removed' : 'changed' });
  }
  function diff(area: WorkflowVersionChangeArea, path: string, old: unknown, next: unknown): void {
    if (old === next) return;
    if (record(old) && record(next)) {
      for (const key of [...new Set([...Object.keys(old), ...Object.keys(next)])].sort()) diff(area, `${path}/${pointer(key)}`, old[key], next[key]);
    } else if (Array.isArray(old) && Array.isArray(next)) {
      // 序列顺序会影响分支优先级、办理顺序与字段展示，不能统一当成集合排序。
      if (JSON.stringify(canonical(old)) !== JSON.stringify(canonical(next))) add(area, path, old, next);
    } else if (JSON.stringify(canonical(old)) !== JSON.stringify(canonical(next))) add(area, path, old, next);
  }
  diff('definition', '/workflowKindKey', left.workflowKindKey, right.workflowKindKey);
  diff('definition', '/schemaVersion', left.schemaVersion, right.schemaVersion);
  diff('binding', '/formVersionId', before.formVersionId, after.formVersionId);
  diff('presentation', '/businessTitleTemplate', before.businessTitleTemplate, after.businessTitleTemplate);
  const oldNodes = new Map(left.nodes.map(node => [node.nodeKey, node])); const newNodes = new Map(right.nodes.map(node => [node.nodeKey, node]));
  for (const key of [...new Set([...oldNodes.keys(), ...newNodes.keys()])].sort()) {
    const old = oldNodes.get(key), next = newNodes.get(key); const path = `/nodes/${pointer(key)}`;
    if (!old || !next) { add('node', path, old, next); continue; }
    diff('node', `${path}/nodeTypeKey`, old.nodeTypeKey, next.nodeTypeKey);
    diff('node', `${path}/nodeSchemaVersion`, old.nodeSchemaVersion, next.nodeSchemaVersion);
    diff('node', `${path}/awaitActivityNodeKeys`, old.awaitActivityNodeKeys, next.awaitActivityNodeKeys);
    const oldConfig = old.config as Record<string, unknown>, newConfig = next.config as Record<string, unknown>;
    for (const property of [...new Set([...Object.keys(oldConfig), ...Object.keys(newConfig)])].sort()) {
      const area: WorkflowVersionChangeArea = property === 'fieldPolicies' ? 'authorization'
        : property === 'collaborationPolicy' || property === 'callPolicy' || property === 'subprocessPolicy' ? 'binding'
        : property === 'nodeName' ? 'presentation' : 'node';
      diff(area, `${path}/config/${pointer(property)}`, oldConfig[property], newConfig[property]);
    }
  }
  function fields(schema: WorkflowFormSchema) {
    const rows = schema.sections.flatMap(section => section.fields);
    if (rows.length > 1024 || new Set(rows.map(field => field.fieldKey)).size !== rows.length) throw new TypeError('workflow.version_comparison.invalid_form');
    return Object.fromEntries(rows.map(field => [field.fieldKey, field]));
  }
  diff('field', '/fields', fields(beforeForm.schema), fields(afterForm.schema));
  diff('field', '/form/schemaVersion', beforeForm.schema.schemaVersion, afterForm.schema.schemaVersion);
  diff('binding', '/form/adapterVersion', beforeForm.version.adapterVersion, afterForm.version.adapterVersion);
  diff('binding', '/form/componentCatalogVersion', beforeForm.version.componentCatalogVersion, afterForm.version.componentCatalogVersion);
  diff('presentation', '/form/sections', beforeForm.schema.sections.map(section => ({ sectionKey: section.sectionKey, fields: section.fields.map(field => field.fieldKey) })),
    afterForm.schema.sections.map(section => ({ sectionKey: section.sectionKey, fields: section.fields.map(field => field.fieldKey) })));
  if (beforeForm.version.webRenderSchemaJson !== afterForm.version.webRenderSchemaJson) add('presentation', '/form/webRenderSnapshot', beforeForm.version.id, afterForm.version.id);
  return { changes, totalChanges, truncated: changes.length < totalChanges };
}

function canonical(value: unknown): unknown {
  if (Array.isArray(value)) return value.map(canonical);
  if (record(value)) return Object.fromEntries(Object.keys(value).sort().map(key => [key, canonical(value[key])]));
  return value;
}

/** 限制单元格体积，模板使用纯文本；版本中的脚本或HTML绝不执行。 */
export function formatWorkflowVersionValue(value: unknown): string {
  const text = value === undefined ? '—' : JSON.stringify(canonical(value), null, 2) ?? '—';
  return text.length > 2000 ? text.slice(0, 2000) + '…' : text;
}
