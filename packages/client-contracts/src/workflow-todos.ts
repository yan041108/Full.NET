import {
  readWorkflowTodoDetailResponse,
  type WorkflowTodoDetailResponse
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
  'switch'
] as const;

export type WorkflowFieldType = typeof WORKFLOW_FIELD_TYPES[number];
export type WorkflowFieldPolicy = 'hidden' | 'readOnly' | 'editable' | 'required';

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

export type WorkflowTodoDetail = Omit<
  WorkflowTodoDetailResponse,
  'fieldPolicies' | 'formSchema' | 'submission'
> & {
  readonly fieldPolicies: WorkflowFieldPolicies;
  readonly formSchema: WorkflowFormSchema;
  readonly submission: WorkflowSubmission;
};

/**
 * 将 OpenAPI 的 JsonElement 收紧为前端可安全渲染的静态表单协议。
 * 未知 adapter、字段控件或策略全部失败关闭。
 */
export function isWorkflowTodoDetail(value: unknown): value is WorkflowTodoDetail {
  let response: WorkflowTodoDetailResponse;
  try {
    response = readWorkflowTodoDetailResponse(value);
  } catch {
    return false;
  }

  if (!isWorkflowFormSchema(response.formSchema)
    || !isRecord(response.submission)
    || !isWorkflowFieldPolicies(response.fieldPolicies)) {
    return false;
  }

  const fieldKeys = new Set(response.formSchema.sections.flatMap(section =>
    section.fields.map(field => field.fieldKey)));
  return Object.keys(response.submission).every(key => fieldKeys.has(key))
    && Object.keys(response.fieldPolicies).every(key => fieldKeys.has(key));
}

export function isWorkflowFormSchema(value: unknown): value is WorkflowFormSchema {
  if (!isRecord(value)
    || value.schemaVersion !== 1
    || value.adapterVersion !== 1
    || !Array.isArray(value.sections)) {
    return false;
  }

  const fieldKeys = new Set<string>();
  return value.sections.every(section => {
    if (!isRecord(section)
      || typeof section.sectionKey !== 'string'
      || section.sectionKey.length === 0
      || !Array.isArray(section.fields)) {
      return false;
    }

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
  return isRecord(value)
    && typeof value.fieldKey === 'string'
    && value.fieldKey.length > 0
    && typeof value.fieldTypeKey === 'string'
    && WORKFLOW_FIELD_TYPES.some(type => type === value.fieldTypeKey)
    && typeof value.required === 'boolean'
    && isRecord(value.constraints);
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
