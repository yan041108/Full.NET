import { http } from './http';
import { workflowGetStarterSelections, workflowListStarterSelectionCandidates, type WorkflowRecipientCandidatePageResponse,
  type WorkflowStarterSelectionDescriptorResponse, type WorkflowStarterSelectionsResponse } from '@fullnet/client-contracts';

export interface WorkflowStarterSelectionDescriptor extends Omit<WorkflowStarterSelectionDescriptorResponse, 'modeKey' | 'rangeKey'> {
  readonly modeKey: 'single' | 'multiple'; readonly maximumUsers: number; readonly rangeKey: 'all' | 'specified_users' | 'role_members';
}
export interface WorkflowStarterSelectionMetadata extends Omit<WorkflowStarterSelectionsResponse, 'items'> { readonly items: readonly WorkflowStarterSelectionDescriptor[] }
export type WorkflowAssigneeSelections = Readonly<Record<string, readonly string[]>>;
const key = /^[A-Za-z][A-Za-z0-9_.-]{0,127}$/u;
const uuid = /^(?!00000000-0000-0000-0000-000000000000$)[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/iu;
const object = (value: unknown): value is Record<string, unknown> => typeof value === 'object' && value !== null && !Array.isArray(value);
const invalid = (): never => { throw new TypeError('workflow.invalid_starter_selection'); };

/** 启动请求只接受稳定选择键及有序成员，禁止在选择值中传入范围、租户或发起人。 */
export function readWorkflowAssigneeSelections(value: unknown): WorkflowAssigneeSelections {
  if (!object(value) || Object.keys(value).length > 64) return invalid();
  let total = 0;
  const entries = Object.entries(value).sort(([first], [second]) => first < second ? -1 : first > second ? 1 : 0);
  const normalized: Array<[string, string[]]> = [];
  for (const [name, users] of entries) {
    if (!key.test(name) || !Array.isArray(users) || users.length < 1 || users.length > 20 || (total += users.length) > 160 ||
      users.some(user => typeof user !== 'string' || !uuid.test(user))) return invalid();
    const ids = users.map(user => String(user).toLowerCase());
    if (new Set(ids).size !== ids.length) return invalid();
    normalized.push([name, ids]);
  }
  return Object.fromEntries(normalized);
}

export async function getWorkflowStarterSelections(versionId: string, signal?: AbortSignal): Promise<WorkflowStarterSelectionMetadata> {
  if (!uuid.test(versionId)) return invalid();
  const value = await workflowGetStarterSelections(http, { versionId }, signal);
  if (!object(value) || value.definitionVersionId !== versionId || !Array.isArray(value.items) || value.items.length > 64) return invalid();
  const seen = new Set<string>();
  for (const item of value.items) {
    if (!object(item) || typeof item.selectionKey !== 'string' || !key.test(item.selectionKey) || seen.has(item.selectionKey) ||
      typeof item.nodeKey !== 'string' || !key.test(item.nodeKey) || typeof item.nodeName !== 'string' || item.nodeName.length > 512 ||
      !['single', 'multiple'].includes(String(item.modeKey)) || !Number.isSafeInteger(item.maximumUsers) || Number(item.maximumUsers) < 1 || Number(item.maximumUsers) > 20 ||
      item.modeKey === 'single' && item.maximumUsers !== 1 || !['all', 'specified_users', 'role_members'].includes(String(item.rangeKey))) return invalid();
    seen.add(item.selectionKey);
  }
  return value as unknown as WorkflowStarterSelectionMetadata;
}

export async function listWorkflowStarterSelectionCandidates(versionId: string, selectionKey: string, page: number, signal?: AbortSignal): Promise<WorkflowRecipientCandidatePageResponse> {
  if (!uuid.test(versionId) || !key.test(selectionKey) || !Number.isSafeInteger(page) || page < 1) return invalid();
  const value = await workflowListStarterSelectionCandidates(http, { versionId, selectionKey, page, pageSize: 20 }, signal);
  if (value.page !== page || value.pageSize !== 20 || !Number.isSafeInteger(value.total) || value.total < 0 || value.items.length > 20 ||
    value.items.some(user => !uuid.test(user.id)) || new Set(value.items.map(user => user.id.toLowerCase())).size !== value.items.length) return invalid();
  return value;
}
