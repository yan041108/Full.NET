import { workflowGetBusinessActionCatalog, type WorkflowBusinessActionCatalogItemResponse } from '@fullnet/client-contracts';
import { http } from './http';
export type WorkflowBusinessAction = WorkflowBusinessActionCatalogItemResponse;
/** 固定目录仅用于当前可信作用域；未知元数据不能被提升为任意业务操作。 */
export async function listWorkflowBusinessActions(signal?: AbortSignal): Promise<WorkflowBusinessAction[]> {
  const value: unknown = await workflowGetBusinessActionCatalog(http, {}, signal);
  const invalid = (): never => { throw new TypeError('client.invalid_workflow_business_action_catalog'); };
  if (typeof value !== 'object' || value === null || !('items' in value) || !Array.isArray(value.items) || value.items.length > 64) return invalid();
  const seen = new Set<string>();
  for (const entry of value.items) {
    if (typeof entry !== 'object' || entry === null || typeof entry.ownerKey !== 'string' || !/^[a-z][a-z0-9_.-]{0,63}$/u.test(entry.ownerKey) ||
      typeof entry.actionKey !== 'string' || !/^[a-z][a-z0-9_.-]{0,63}$/u.test(entry.actionKey) || !Number.isSafeInteger(entry.actionVersion) ||
      entry.actionVersion < 1 || entry.actionVersion > 65535 || typeof entry.businessType !== 'string' || entry.businessType.length < 1 || entry.businessType.length > 64 ||
      entry.placementKey !== 'root_finalizer' || typeof entry.actionHash !== 'string' || !/^[a-f0-9]{64}$/u.test(entry.actionHash)) return invalid();
    const key = `${entry.actionKey}@${entry.actionVersion}`; if (seen.has(key)) return invalid(); seen.add(key);
  }
  return value.items as WorkflowBusinessAction[];
}
