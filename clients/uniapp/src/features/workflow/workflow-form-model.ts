import {
  isWorkflowFormSchema,
  type WorkflowFieldPolicies,
  type WorkflowFieldPolicy,
  type WorkflowFormField,
  type WorkflowFormSchema,
  type WorkflowSubmission
} from '@fullnet/client-contracts';

export interface WorkflowRenderedField extends WorkflowFormField {
  readonly policy: WorkflowFieldPolicy;
  readonly readOnly: boolean;
  readonly required: boolean;
  readonly value: unknown;
}

export interface WorkflowRenderedSection {
  readonly sectionKey: string;
  readonly fields: readonly WorkflowRenderedField[];
}

export interface WorkflowFormState {
  readonly sections: readonly WorkflowRenderedSection[];
  readonly fieldsByKey: Readonly<Record<string, WorkflowRenderedField>>;
  readonly values: WorkflowSubmission;
  readonly patch: WorkflowSubmission;
}

export interface WorkflowFormModel {
  readonly state: WorkflowFormState;
  updateValue(fieldKey: string, value: unknown): boolean;
  validate(): Readonly<Record<string, 'required'>>;
}

const WORKFLOW_POLICIES = new Set<WorkflowFieldPolicy>([
  'hidden',
  'readOnly',
  'editable',
  'required'
]);

/**
 * 创建移动端静态表单模型。协议、策略和提交值均在边界失败关闭，避免服务端新增控件后被旧客户端误渲染。
 */
export function createWorkflowFormModel(
  schemaValue: unknown,
  submissionValue: unknown,
  policiesValue: unknown = {}
): WorkflowFormModel {
  if (!isWorkflowFormSchema(schemaValue)) {
    throw new Error('workflow.form.unsupported-schema');
  }

  const schema = schemaValue as WorkflowFormSchema;
  const fieldMap = new Map<string, WorkflowFormField>();
  for (const section of schema.sections) {
    for (const field of section.fields) {
      fieldMap.set(field.fieldKey, field);
    }
  }

  const submission = readRecord(submissionValue, 'workflow.form.invalid-submission');
  const policies = readPolicies(policiesValue, fieldMap);
  assertKnownKeys(submission, fieldMap, 'workflow.form.invalid-submission');

  const initialValues: Record<string, unknown> = {};
  const currentValues: Record<string, unknown> = {};
  const patch: Record<string, unknown> = {};

  for (const [fieldKey, value] of Object.entries(submission)) {
    const field = fieldMap.get(fieldKey)!;
    const normalized = normalizeValue(field, value);
    if (!normalized.accepted) {
      throw new Error('workflow.form.invalid-submission');
    }
    initialValues[fieldKey] = cloneValue(normalized.value);
    currentValues[fieldKey] = cloneValue(normalized.value);
  }

  const toState = (): WorkflowFormState => {
    const fieldsByKey: Record<string, WorkflowRenderedField> = {};
    const sections = schema.sections.map(section => ({
      sectionKey: section.sectionKey,
      fields: section.fields
        .filter(field => resolvePolicy(field, policies) !== 'hidden')
        .map(field => {
          const policy = resolvePolicy(field, policies);
          const renderedField: WorkflowRenderedField = {
            ...field,
            constraints: { ...field.constraints },
            policy,
            readOnly: policy === 'readOnly',
            required: field.required || policy === 'required',
            value: cloneValue(currentValues[field.fieldKey])
          };
          fieldsByKey[field.fieldKey] = renderedField;
          return renderedField;
        })
    }));

    return {
      sections,
      fieldsByKey,
      values: cloneRecord(currentValues),
      patch: cloneRecord(patch)
    };
  };

  return {
    get state() {
      return toState();
    },
    updateValue(fieldKey, value) {
      const field = fieldMap.get(fieldKey);
      if (!field) {
        return false;
      }

      const policy = resolvePolicy(field, policies);
      if (policy === 'hidden' || policy === 'readOnly') {
        return false;
      }

      const normalized = normalizeValue(field, value);
      if (!normalized.accepted) {
        return false;
      }

      currentValues[fieldKey] = cloneValue(normalized.value);
      if (areEqual(initialValues[fieldKey], normalized.value)) {
        delete patch[fieldKey];
      } else {
        patch[fieldKey] = cloneValue(normalized.value);
      }
      return true;
    },
    validate() {
      const errors: Record<string, 'required'> = {};
      for (const section of schema.sections) {
        for (const field of section.fields) {
          const policy = resolvePolicy(field, policies);
          if (policy !== 'hidden'
            && (field.required || policy === 'required')
            && isEmpty(currentValues[field.fieldKey])) {
            errors[field.fieldKey] = 'required';
          }
        }
      }
      return errors;
    }
  };
}

interface NormalizedValue {
  readonly accepted: boolean;
  readonly value?: unknown;
}

function normalizeValue(field: WorkflowFormField, value: unknown): NormalizedValue {
  if (value === null || value === undefined || value === '') {
    return { accepted: true, value: value === '' ? '' : null };
  }

  switch (field.fieldTypeKey) {
    case 'integer': {
      const integer = typeof value === 'number' ? value : Number(value);
      return Number.isSafeInteger(integer)
        ? { accepted: true, value: integer }
        : { accepted: false };
    }
    case 'decimal':
    case 'money':
      return typeof value === 'string'
        ? { accepted: true, value: value.trim() }
        : { accepted: false };
    case 'checkbox': {
      if (!Array.isArray(value)) {
        return { accepted: false };
      }
      const options = readOptions(field);
      const selected = value.filter((item): item is string =>
        typeof item === 'string' && options.includes(item));
      return { accepted: true, value: [...new Set(selected)] };
    }
    case 'radio':
    case 'select': {
      const options = readOptions(field);
      return typeof value === 'string' && options.includes(value)
        ? { accepted: true, value }
        : { accepted: false };
    }
    case 'switch':
      return typeof value === 'boolean'
        ? { accepted: true, value }
        : { accepted: false };
    default:
      return typeof value === 'string'
        ? { accepted: true, value }
        : { accepted: false };
  }
}

function readPolicies(
  value: unknown,
  fields: ReadonlyMap<string, WorkflowFormField>
): WorkflowFieldPolicies {
  const record = readRecord(value, 'workflow.form.invalid-policies');
  assertKnownKeys(record, fields, 'workflow.form.invalid-policies');
  for (const policy of Object.values(record)) {
    if (typeof policy !== 'string' || !WORKFLOW_POLICIES.has(policy as WorkflowFieldPolicy)) {
      throw new Error('workflow.form.invalid-policies');
    }
  }
  return record as WorkflowFieldPolicies;
}

function readRecord(value: unknown, errorCode: string): Record<string, unknown> {
  if (typeof value !== 'object' || value === null || Array.isArray(value)) {
    throw new Error(errorCode);
  }
  return value as Record<string, unknown>;
}

function assertKnownKeys(
  record: Readonly<Record<string, unknown>>,
  fields: ReadonlyMap<string, WorkflowFormField>,
  errorCode: string
): void {
  if (Object.keys(record).some(key => !fields.has(key))) {
    throw new Error(errorCode);
  }
}

function resolvePolicy(
  field: WorkflowFormField,
  policies: WorkflowFieldPolicies
): WorkflowFieldPolicy {
  return policies[field.fieldKey] ?? (field.required ? 'required' : 'editable');
}

function readOptions(field: WorkflowFormField): readonly string[] {
  const options = field.constraints.options;
  return Array.isArray(options)
    ? options.filter((option): option is string => typeof option === 'string')
    : [];
}

function isEmpty(value: unknown): boolean {
  return value === null
    || value === undefined
    || (typeof value === 'string' && value.trim().length === 0)
    || (Array.isArray(value) && value.length === 0);
}

function areEqual(left: unknown, right: unknown): boolean {
  return JSON.stringify(left) === JSON.stringify(right);
}

function cloneRecord(record: Readonly<Record<string, unknown>>): WorkflowSubmission {
  return Object.fromEntries(Object.entries(record).map(([key, value]) => [key, cloneValue(value)]));
}

function cloneValue(value: unknown): unknown {
  return Array.isArray(value) ? [...value] : value;
}
