import { isWorkflowClaimProgress, type WorkflowClaimProgress } from './workflow-claims.js';
import {
  readWorkflowTodoRuntimeResponse,
  type WorkflowTodoRuntimeResponse
} from './generated/index.generated.js';

export const WORKFLOW_FIELD_TYPES = [
  'text',
  'textarea',
  'money',
  'decimal',
  'date',
  'time',
  'datetime',
  'radio',
  'select',
  'integer',
  'checkbox',
  'switch',
  'attachment',
  'subtable'
] as const;

export type WorkflowFieldType = typeof WORKFLOW_FIELD_TYPES[number];
export type WorkflowFieldPolicy = 'hidden' | 'readOnly' | 'editable' | 'required';

const MAX_WORKFLOW_FORM_SECTIONS = 32;
const MAX_WORKFLOW_FORM_FIELDS_PER_SECTION = 64;
const MAX_WORKFLOW_FORM_FIELDS = 256;
const MAX_WORKFLOW_FORM_STABLE_KEY_LENGTH = 64;
const FORBIDDEN_WORKFLOW_FORM_STABLE_KEYS = new Set([
  '__proto__',
  'prototype',
  'constructor'
]);
const WORKFLOW_FORM_CONSTRAINT_KEYS: Readonly<Record<WorkflowFieldType, ReadonlySet<string>>> = {
  text: new Set(['minLength', 'maxLength']),
  textarea: new Set(['minLength', 'maxLength']),
  integer: new Set(['minimum', 'maximum']),
  decimal: new Set(['scale', 'minimum', 'maximum']),
  money: new Set(['scale', 'minimum', 'maximum']),
  date: new Set(['minimum', 'maximum']),
  time: new Set(['minimum', 'maximum']),
  datetime: new Set(['minimum', 'maximum']),
  radio: new Set(['options']),
  checkbox: new Set(['options']),
  select: new Set(['options']),
  switch: new Set(),
  attachment: new Set(['maxCount', 'maxSizeBytes', 'allowedExtensions']),
  subtable: new Set(['maxRows', 'columns'])
};

export interface WorkflowFormField {
  readonly fieldKey: string;
  readonly fieldTypeKey: WorkflowFieldType;
  readonly required: boolean;
  readonly constraints: Readonly<Record<string, unknown>>;
}

export interface WorkflowFormSection {
  readonly sectionKey: string;
  readonly fields: readonly WorkflowFormField[];
}

export interface WorkflowFormSchema {
  readonly schemaVersion: 1;
  readonly adapterVersion: 1;
  readonly sections: readonly WorkflowFormSection[];
}

export type WorkflowSubmission = Readonly<Record<string, unknown>>;
export type WorkflowFieldPolicies = Readonly<Record<string, WorkflowFieldPolicy>>;

export type WorkflowSignatureUpload = { readonly fileId: string; readonly sizeBytes: number; readonly contentHash: string | null };

/** 采集响应不得把 URL 或不完整文件对象当成可提交的签名身份。 */
export function readWorkflowSignatureUpload(value: unknown): WorkflowSignatureUpload {
  if (!isRecord(value) || typeof value.fileId !== 'string' || !/^[0-9a-f]{8}-[0-9a-f]{4}-[1-8][0-9a-f]{3}-[89ab][0-9a-f]{3}-[0-9a-f]{12}$/i.test(value.fileId) ||
    typeof value.sizeBytes !== 'number' || !Number.isSafeInteger(value.sizeBytes) || value.sizeBytes < 8 || value.sizeBytes > 1_048_576 ||
    (value.contentHash !== null && (typeof value.contentHash !== 'string' || !/^[0-9a-f]{64}$/i.test(value.contentHash)))) {
    throw new TypeError('workflow.todo.invalid-signature-upload');
  }
  return value as WorkflowSignatureUpload;
}

/** 组进度与根门槛分别展示，成员数不得作为组数使用。 */
export interface WorkflowApprovalGroupProgress {
  readonly id: string; readonly groupKey: string; readonly groupName: string | null;
  readonly sequenceNo: number; readonly approvalModeKey: string; readonly requiredApprovalCount: number;
  readonly memberCount: number; readonly approvedCount: number; readonly rejectedCount: number;
  readonly pendingCount: number; readonly cancelledCount: number; readonly statusKey: string; readonly isCurrentGroup: boolean;
}

/** 办理的完成进度独立于审批票数。 */
export interface WorkflowTaskProgress {
  readonly completionModeKey: 'all' | 'any' | 'sequential'; readonly memberCount: number;
  readonly requiredCompletionCount: number; readonly completedCount: number; readonly pendingCount: number; readonly cancelledCount: number;
}

/** 本人独立选票表单，修订、值和渲染器都不借用共享主表单。 */
export interface WorkflowVoteForm {
  readonly formVersionId: string; readonly formSchemaHash: string; readonly formSchema: WorkflowFormSchema;
  readonly values: WorkflowSubmission; readonly fieldPolicies: WorkflowFieldPolicies; readonly revision: number;
  readonly choiceKey: 'yes' | 'no' | 'abstain' | null; readonly readOnly: boolean; readonly allowAbstain: boolean;
  readonly webRenderJson?: Record<string, unknown> | null;
}
export interface WorkflowVoteProgress {
  readonly memberCount: number; readonly requiredParticipationCount: number; readonly submittedCount: number;
  readonly pendingCount: number; readonly cancelledCount: number; readonly statusKey: 'active' | 'approved' | 'rejected' | 'suspended' | 'cancelled';
  readonly closesAtUtc: string | null; readonly yesCount: number | null; readonly noCount: number | null;
  readonly abstainCount: number | null; readonly unsubmittedCount: number | null;
}

/** 投票发送个人修订与完整值，不允许夹带共享表单修订或租户。 */
export interface WorkflowVoteRequest {
  readonly expectedRevision: number; readonly expectedBallotRevision: number; readonly choiceKey: 'yes' | 'no' | 'abstain';
  readonly values: WorkflowSubmission; readonly comment?: string | null; readonly idempotencyKey: string;
  readonly signatureFileId?: string | null;
}
export function readWorkflowVoteRequest(value: unknown): WorkflowVoteRequest {
  if (!isRecord(value) || Object.keys(value).some(key => !['expectedRevision', 'expectedBallotRevision', 'choiceKey', 'values', 'comment', 'idempotencyKey', 'signatureFileId'].includes(key)) ||
    !Number.isSafeInteger(value.expectedRevision) || Number(value.expectedRevision) < 1 || !Number.isSafeInteger(value.expectedBallotRevision) || Number(value.expectedBallotRevision) < 1 ||
    !['yes', 'no', 'abstain'].includes(String(value.choiceKey)) || !isRecord(value.values) || typeof value.idempotencyKey !== 'string' ||
    value.idempotencyKey.trim().length < 1 || value.idempotencyKey.length > 128 || value.comment != null && (typeof value.comment !== 'string' || value.comment.length > 512) ||
    value.signatureFileId != null && (typeof value.signatureFileId !== 'string' || !/^(?!00000000-0000-0000-0000-000000000000$)[0-9a-f]{8}(?:-[0-9a-f]{4}){3}-[0-9a-f]{12}$/iu.test(value.signatureFileId)))
    throw new TypeError('workflow.todo.invalid-vote');
  return value as unknown as WorkflowVoteRequest;
}

export type WorkflowTodoDetail = Omit<
  WorkflowTodoRuntimeResponse,
  'fieldPolicies' | 'formSchema' | 'submission' |
  'approvalModeKey' | 'requiredApprovalCount' | 'approvedCount' |
  'rejectedCount' | 'pendingCount' | 'approvalGroups' | 'activityKindKey' | 'taskProgress' | 'voteForm' | 'voteProgress' | 'claimProgress' | 'webRenderJson' | 'allowedActions' | 'signRequired' | 'commentRequired'
> & {
  readonly fieldPolicies: WorkflowFieldPolicies;
  readonly formSchema: WorkflowFormSchema;
  readonly submission: WorkflowSubmission;
  readonly activityKindKey?: 'human.approval' | 'human.group_approval' | 'human.task' | 'human.vote' | 'human.claim';
  readonly taskProgress?: WorkflowTaskProgress | null;
  readonly voteForm?: WorkflowVoteForm | null;
  readonly voteProgress?: WorkflowVoteProgress | null;
  readonly claimProgress?: WorkflowClaimProgress | null;
  readonly approvalGroups?: readonly WorkflowApprovalGroupProgress[];
  readonly approvalModeKey: string;
  readonly requiredApprovalCount: number;
  readonly approvedCount: number;
  readonly rejectedCount: number;
  readonly pendingCount: number;
  readonly webRenderJson?: Record<string, unknown> | null;
  readonly allowedActions: readonly string[];
  readonly signRequired: boolean;
  readonly commentRequired: boolean;
};

/**
 * 将 OpenAPI 的 JsonElement 收紧为前端可安全渲染的静态表单协议。
 * 未知 adapter、字段控件或策略全部失败关闭。
 */
export function isWorkflowTodoDetail(value: unknown): value is WorkflowTodoDetail {
  let raw: WorkflowTodoRuntimeResponse;
  try {
    raw = readWorkflowTodoRuntimeResponse(value);
  } catch {
    return false;
  }

  if (raw.approvalModeKey === undefined || raw.requiredApprovalCount === undefined ||
    raw.approvedCount === undefined || raw.rejectedCount === undefined ||
    raw.pendingCount === undefined) {
    return false;
  }
  const response = raw as WorkflowTodoDetail;
  const kind = response.activityKindKey ?? (response.approvalModeKey === 'group' ? 'human.group_approval' : 'human.approval');
  const isTask = kind === 'human.task';
  const isVote = kind === 'human.vote';
  const isClaim = kind === 'human.claim';
  const isApproval = !isTask && !isVote && !isClaim;
  const knownActions = isClaim ? ['complete', 'release', 'transfer', 'delegate', 'remind'] : isTask ? ['complete', 'return', 'transfer', 'delegate', 'remind'] : isVote ? ['vote', 'transfer', 'delegate', 'remind'] : ['approve', 'reject', 'return', 'countersign', 'transfer', 'delegate', 'remind'];
  if (!['human.approval', 'human.group_approval', 'human.task', 'human.vote', 'human.claim'].includes(kind) ||
    isTask && (response.approvalModeKey !== 'none' || !isWorkflowTaskProgress(response.taskProgress) ||
      response.requiredApprovalCount !== 0 || response.approvedCount !== 0 || response.rejectedCount !== 0 || response.pendingCount !== 0) ||
    !isTask && response.taskProgress != null || isApproval && response.approvalModeKey === 'none' ||
    isVote && (response.approvalModeKey !== 'none' || response.requiredApprovalCount !== 0 || response.approvedCount !== 0 ||
      response.rejectedCount !== 0 || response.pendingCount !== 0 || !isWorkflowVoteForm(response.voteForm) || !isWorkflowVoteProgress(response.voteProgress)) ||
    !isVote && (response.voteForm != null || response.voteProgress != null) ||
    isClaim && (response.approvalModeKey !== 'none' || response.requiredApprovalCount !== 0 || response.approvedCount !== 0 ||
      response.rejectedCount !== 0 || response.pendingCount !== 0 || !isWorkflowClaimProgress(response.claimProgress)) ||
    !isClaim && response.claimProgress != null) return false;

  if (!isWorkflowFormSchema(response.formSchema)
    || !Array.isArray(response.allowedActions)
    || response.allowedActions.some(action => !knownActions.includes(action))
    || new Set(response.allowedActions).size !== response.allowedActions.length
    || typeof response.signRequired !== 'boolean' || typeof response.commentRequired !== 'boolean'
    || (response.webRenderJson != null && !isRecord(response.webRenderJson))
    || !isRecord(response.submission)
    || !isWorkflowFieldPolicies(response.fieldPolicies)
    || !/^[0-9a-f]{64}$/.test(response.formSchemaHash)
    || !['single', 'all', 'any', 'nOfM', 'percent', 'sequential', 'group', 'none'].includes(response.approvalModeKey)
    || isApproval && response.requiredApprovalCount < 1
    || response.approvedCount < 0
    || response.rejectedCount < 0
    || response.pendingCount < 0
    || isApproval && response.approvalModeKey !== 'group' && response.requiredApprovalCount >
      response.approvedCount + response.rejectedCount + response.pendingCount) {
    return false;
  }

  if (response.approvalModeKey === 'group') {
    const groups = response.approvalGroups;
    if (!Array.isArray(groups) || groups.length < 2 || groups.length > 8 ||
      groups.some(group => !isWorkflowApprovalGroupProgress(group)) ||
      new Set(groups.map(group => group.groupKey)).size !== groups.length ||
      new Set(groups.map(group => group.id)).size !== groups.length ||
      groups.some((group, index) => group.sequenceNo !== index + 1) ||
      groups.reduce((sum, group) => sum + group.memberCount, 0) > 20 ||
      // 历史只读项的原席可能已恢复或转出；活动办理仍须定位唯一所属组，不能接受重复归属。
      groups.filter(group => group.isCurrentGroup).length > 1 ||
      ['pending', 'active', 'before_countersign'].includes(response.statusKey) && groups.filter(group => group.isCurrentGroup).length !== 1 ||
      response.requiredApprovalCount > groups.length ||
      response.approvedCount !== groups.filter(group => group.statusKey === 'approved').length ||
      response.rejectedCount !== groups.filter(group => group.statusKey === 'rejected').length ||
      response.pendingCount !== groups.filter(group => group.statusKey === 'active').length) return false;
  } else if (response.approvalGroups !== undefined && (!Array.isArray(response.approvalGroups) || response.approvalGroups.length !== 0)) return false;

  const fieldKeys = new Set(response.formSchema.sections.flatMap(section =>
    section.fields.map(field => field.fieldKey)));
  if (isVote && Object.values(response.fieldPolicies).some(policy => policy !== 'readOnly')) return false;
  return Object.keys(response.submission).every(key => fieldKeys.has(key))
    && Object.keys(response.fieldPolicies).every(key => fieldKeys.has(key));
}

/** 活动中不接受选择汇总，终态固定人数与未投人数必须守恒。 */
export function isWorkflowVoteProgress(value: unknown): value is WorkflowVoteProgress {
  if (!isRecord(value)) return false;
  const count = (key: string, maximum = 20): boolean => Number.isSafeInteger(value[key]) && Number(value[key]) >= 0 && Number(value[key]) <= maximum;
  if (!['memberCount', 'requiredParticipationCount', 'submittedCount', 'pendingCount', 'cancelledCount'].every(key => count(key)) ||
    Number(value.memberCount) < 1 || Number(value.requiredParticipationCount) < 1 || Number(value.requiredParticipationCount) > Number(value.memberCount) ||
    Number(value.submittedCount) + Number(value.pendingCount) + Number(value.cancelledCount) !== Number(value.memberCount) ||
    !['active', 'approved', 'rejected', 'suspended', 'cancelled'].includes(String(value.statusKey)) ||
    value.closesAtUtc !== null && (typeof value.closesAtUtc !== 'string' || !Number.isFinite(Date.parse(value.closesAtUtc)))) return false;
  const finalKeys = ['yesCount', 'noCount', 'abstainCount', 'unsubmittedCount'];
  if (value.statusKey === 'active') return finalKeys.every(key => value[key] === null) && value.cancelledCount === 0;
  return finalKeys.every(key => count(key)) && Number(value.yesCount) + Number(value.noCount) + Number(value.abstainCount) === Number(value.submittedCount) &&
    Number(value.unsubmittedCount) === Number(value.pendingCount) + Number(value.cancelledCount);
}

export function isWorkflowVoteForm(value: unknown): value is WorkflowVoteForm {
  if (!isRecord(value) || typeof value.formVersionId !== 'string' || !/^(?!00000000-0000-0000-0000-000000000000$)[0-9a-f]{8}(?:-[0-9a-f]{4}){3}-[0-9a-f]{12}$/iu.test(value.formVersionId) ||
    typeof value.formSchemaHash !== 'string' || !/^[0-9a-f]{64}$/u.test(value.formSchemaHash) || !isWorkflowFormSchema(value.formSchema) ||
    !isRecord(value.values) || !isWorkflowFieldPolicies(value.fieldPolicies) || !Number.isSafeInteger(value.revision) || Number(value.revision) < 1 ||
    typeof value.readOnly !== 'boolean' || typeof value.allowAbstain !== 'boolean' ||
    value.choiceKey !== null && !['yes', 'no', 'abstain'].includes(String(value.choiceKey)) ||
    value.choiceKey === 'abstain' && !value.allowAbstain || value.choiceKey !== null && !value.readOnly ||
    value.webRenderJson != null && !isRecord(value.webRenderJson)) return false;
  const keys = new Set(value.formSchema.sections.flatMap(section => section.fields.map(field => field.fieldKey)));
  return Object.keys(value.values).every(key => keys.has(key)) && Object.keys(value.fieldPolicies).length === keys.size &&
    Object.entries(value.fieldPolicies).every(([key, policy]) => keys.has(key) &&
      (value.readOnly ? policy === 'readOnly' : policy === 'editable' || policy === 'required'));
}

export function isWorkflowFormSchema(value: unknown): value is WorkflowFormSchema {
  if (!isRecord(value)
    || value.schemaVersion !== 1
    || value.adapterVersion !== 1
    || !Array.isArray(value.sections)
    || value.sections.length === 0
    || value.sections.length > MAX_WORKFLOW_FORM_SECTIONS) {
    return false;
  }

  const fieldKeys = new Set<string>();
  const sectionKeys = new Set<string>();
  let totalFields = 0;
  return value.sections.every(section => {
    if (!isRecord(section)
      || !isStableWorkflowFormKey(section.sectionKey)
      || sectionKeys.has(section.sectionKey)
      || !Array.isArray(section.fields)
      || section.fields.length === 0
      || section.fields.length > MAX_WORKFLOW_FORM_FIELDS_PER_SECTION
      || totalFields > MAX_WORKFLOW_FORM_FIELDS - section.fields.length) {
      return false;
    }

    sectionKeys.add(section.sectionKey);
    totalFields += section.fields.length;
    return section.fields.every(field => {
      if (!isWorkflowFormField(field) || fieldKeys.has(field.fieldKey)) {
        return false;
      }
      fieldKeys.add(field.fieldKey);
      return true;
    });
  });
}

function isWorkflowFormField(value: unknown): value is WorkflowFormField {
  if (!isRecord(value)) {
    return false;
  }

  if (!isStableWorkflowFormKey(value.fieldKey)
    || typeof value.fieldTypeKey !== 'string'
    || !WORKFLOW_FIELD_TYPES.some(type => type === value.fieldTypeKey)
    || typeof value.required !== 'boolean'
    || !isRecord(value.constraints)) {
    return false;
  }

  const allowedKeys = WORKFLOW_FORM_CONSTRAINT_KEYS[value.fieldTypeKey as WorkflowFieldType];
  return Object.keys(value.constraints).every(key => allowedKeys.has(key));
}

function isStableWorkflowFormKey(value: unknown): value is string {
  if (typeof value !== 'string'
    || value.length === 0
    || value.length > MAX_WORKFLOW_FORM_STABLE_KEY_LENGTH
    || FORBIDDEN_WORKFLOW_FORM_STABLE_KEYS.has(value.toLowerCase())
    || !isAsciiLetter(value.charCodeAt(0))) {
    return false;
  }

  for (let index = 1; index < value.length; index += 1) {
    const characterCode = value.charCodeAt(index);
    const character = value[index];
    if (!isAsciiLetter(characterCode)
      && !(characterCode >= 48 && characterCode <= 57)
      && character !== '_'
      && character !== '-'
      && character !== '.') {
      return false;
    }
  }

  return true;
}

function isAsciiLetter(characterCode: number): boolean {
  return (characterCode >= 65 && characterCode <= 90)
    || (characterCode >= 97 && characterCode <= 122);
}

function isWorkflowFieldPolicies(value: unknown): value is WorkflowFieldPolicies {
  return isRecord(value) && Object.values(value).every(policy =>
    policy === 'hidden'
    || policy === 'readOnly'
    || policy === 'editable'
    || policy === 'required');
}

function isRecord(value: unknown): value is Record<string, unknown> {
  return typeof value === 'object' && value !== null && !Array.isArray(value);
}

/** 所有计数均来自服务端真实席位；未知状态、负数与重复当前组全部失败关闭。 */
function isWorkflowApprovalGroupProgress(value: unknown): value is WorkflowApprovalGroupProgress {
  if (!isRecord(value) || typeof value.id !== 'string' || !/^[0-9a-f-]{36}$/iu.test(value.id) ||
    typeof value.groupKey !== 'string' || !/^[a-z][a-z0-9_]{0,63}$/u.test(value.groupKey) ||
    value.groupName !== null && typeof value.groupName !== 'string' || typeof value.isCurrentGroup !== 'boolean' ||
    !['single', 'all', 'any', 'nOfM', 'percent', 'sequential', 'system'].includes(String(value.approvalModeKey)) ||
    !['active', 'approved', 'rejected', 'cancelled'].includes(String(value.statusKey))) return false;
  const counts = ['sequenceNo', 'requiredApprovalCount', 'memberCount', 'approvedCount', 'rejectedCount', 'pendingCount', 'cancelledCount'];
  if (counts.some(key => !Number.isSafeInteger(value[key]) || Number(value[key]) < 0)) return false;
  const group = value as unknown as WorkflowApprovalGroupProgress;
  if (group.approvalModeKey === 'system') return group.sequenceNo >= 1 && group.sequenceNo <= 8 && group.memberCount === 0 &&
    group.requiredApprovalCount === 0 && group.statusKey === 'approved' && !group.isCurrentGroup &&
    group.approvedCount === 0 && group.rejectedCount === 0 && group.pendingCount === 0 && group.cancelledCount === 0;
  return group.sequenceNo >= 1 && group.sequenceNo <= 8 && group.memberCount >= 1 && group.memberCount <= 20 &&
    group.requiredApprovalCount >= 1 && group.requiredApprovalCount <= group.memberCount &&
    group.approvedCount + group.rejectedCount + group.pendingCount + group.cancelledCount === group.memberCount;
}

/** 固定人数、门槛及三类结果必须闭合，不能借审批字段补出办理进度。 */
function isWorkflowTaskProgress(value: unknown): value is WorkflowTaskProgress {
  if (!isRecord(value) || !['all', 'any', 'sequential'].includes(String(value.completionModeKey))) return false;
  if (['memberCount', 'requiredCompletionCount', 'completedCount', 'pendingCount', 'cancelledCount']
    .some(key => !Number.isSafeInteger(value[key]) || Number(value[key]) < 0 || Number(value[key]) > 20)) return false;
  const progress = value as unknown as WorkflowTaskProgress;
  return progress.memberCount >= 1 && progress.requiredCompletionCount === (progress.completionModeKey === 'any' ? 1 : progress.memberCount) &&
    progress.completedCount + progress.pendingCount + progress.cancelledCount === progress.memberCount;
}
