import { workflowListDefinitions, workflowListDefinitionVersions, workflowGetDefinitionVersion, workflowGetFormVersion,
  isWorkflowFormSchema, type WorkflowFormField } from '@fullnet/client-contracts';
import { http } from './http';

export const listSubprocessDefinitions = async (signal?: AbortSignal) =>
  (await workflowListDefinitions(http, {}, signal)).filter(item => item.statusKey === 'active' && item.latestPublishedVersionId);
export const listSubprocessVersions = async (definitionId: string, signal?: AbortSignal) =>
  workflowListDefinitionVersions(http, { definitionId }, signal);
export interface SubprocessAsset { definitionId: string; versionId: string; fields: readonly WorkflowFormField[] }
/** 读取真实固定发布资产；客户端过滤仅改善选择，最终准入仍由服务端发布绑定裁定。 */
export async function getSubprocessAsset(versionId: string, signal?: AbortSignal): Promise<SubprocessAsset> {
  const version = await workflowGetDefinitionVersion(http, { versionId }, signal);
  const form = await workflowGetFormVersion(http, { versionId: version.formVersionId }, signal);
  const schema: unknown = JSON.parse(form.formSchemaJson); const draft: unknown = JSON.parse(version.canonicalJson);
  if (!isWorkflowFormSchema(schema) || !draft || typeof draft !== 'object' || !('nodes' in draft) || !Array.isArray(draft.nodes) ||
      draft.nodes.some(node => !node || typeof node !== 'object' || node.nodeTypeKey === 'business.command')) throw new Error('client.invalid_workflow_subprocess_asset');
  return { definitionId: version.definitionId, versionId: version.id, fields: schema.sections.flatMap(section => section.fields) };
}
