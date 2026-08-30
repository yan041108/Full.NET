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
  'switch'
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
  switch: new Set()
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

export type WorkflowTodoDetail = Omit<
  WorkflowTodoRuntimeResponse,
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
  let response: WorkflowTodoRuntimeResponse;
  try {
    response = readWorkflowTodoRuntimeResponse(value);
  } catch {
    return false;
  }

  if (!isWorkflowFormSchema(response.formSchema)
    || !isRecord(response.submission)
    || !isWorkflowFieldPolicies(response.fieldPolicies)
    || !/^[0-9a-f]{64}$/.test(response.formSchemaHash)) {
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
