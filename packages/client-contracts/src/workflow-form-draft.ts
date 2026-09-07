import type { WorkflowFormComponentCatalogResponse } from './generated/index.generated.js';
import {
  createDefaultSubtableConstraints
} from './workflow-form-subtable.js';
import {
  WORKFLOW_FIELD_TYPES,
  type WorkflowFieldType,
  type WorkflowFormField,
  type WorkflowFormSchema,
  type WorkflowFormSection
} from './workflow-todos.js';

const INVALID_DRAFT_ERROR = 'client.invalid_workflow_form_draft';
const MAX_SECTIONS = 32;
const MAX_FIELDS_PER_SECTION = 64;
const MAX_FIELDS = 256;
const forbiddenKeys = new Set(['__proto__', 'prototype', 'constructor']);

export interface WorkflowFormFieldPatch {
  readonly fieldKey?: string;
  readonly required?: boolean;
  readonly constraints?: Readonly<Record<string, unknown>>;
}

/** 创建能够直接进入服务端发布编译器的最小表单草稿。 */
export function createWorkflowFormDraft(): WorkflowFormSchema {
  return {
    schemaVersion: 1,
    adapterVersion: 1,
    sections: [{
      sectionKey: 'main',
      fields: [{
        fieldKey: 'summary',
        fieldTypeKey: 'text',
        required: true,
        constraints: {}
      }]
    }]
  };
}

/** 添加带最小占位字段的 Section，避免产生服务端明确拒绝的空 Section。 */
export function addWorkflowFormSection(
  schema: WorkflowFormSchema,
  sectionKey: string
): WorkflowFormSchema {
  assertSchemaShape(schema);
  if (!isStableKey(sectionKey)
    || schema.sections.length >= MAX_SECTIONS
    || schema.sections.some(section => section.sectionKey === sectionKey)) {
    fail();
  }

  const fieldKey = `${sectionKey}_field`;
  if (!isStableKey(fieldKey) || containsField(schema, fieldKey)) {
    fail();
  }

  return {
    ...schema,
    sections: [...schema.sections, {
      sectionKey,
      fields: [{
        fieldKey,
        fieldTypeKey: 'text',
        required: false,
        constraints: {}
      }]
    }]
  };
}

/** 仅按服务端下发的闭合目录添加字段，并补齐类型要求的安全默认约束。 */
export function addWorkflowFormField(
  schema: WorkflowFormSchema,
  sectionKey: string,
  fieldKey: string,
  fieldTypeKey: WorkflowFieldType,
  catalog: WorkflowFormComponentCatalogResponse
): WorkflowFormSchema {
  assertCatalogMatches(schema, catalog);
  const component = findEnabledComponent(catalog, fieldTypeKey);
  const totalFields = schema.sections.reduce((count, section) => count + section.fields.length, 0);
  const section = schema.sections.find(item => item.sectionKey === sectionKey);
  if (component === undefined
    || section === undefined
    || !isStableKey(fieldKey)
    || containsField(schema, fieldKey)
    || section.fields.length >= MAX_FIELDS_PER_SECTION
    || totalFields >= MAX_FIELDS) {
    fail();
  }

  const field: WorkflowFormField = {
    fieldKey,
    fieldTypeKey,
    required: false,
    constraints: defaultConstraints(fieldTypeKey)
  };
  return replaceSection(schema, sectionKey, current => ({
    ...current,
    fields: [...current.fields, field]
  }));
}

/** 更新字段的稳定属性；目录外约束不会进入返回 Draft。 */
export function updateWorkflowFormField(
  schema: WorkflowFormSchema,
  fieldKey: string,
  patch: Readonly<WorkflowFormFieldPatch>,
  catalog: WorkflowFormComponentCatalogResponse
): WorkflowFormSchema {
  assertCatalogMatches(schema, catalog);
  const current = schema.sections.flatMap(section => section.fields)
    .find(field => field.fieldKey === fieldKey);
  if (current === undefined) {
    fail();
  }

  const component = findEnabledComponent(catalog, current.fieldTypeKey);
  const nextKey = patch.fieldKey ?? current.fieldKey;
  if (component === undefined
    || !isStableKey(nextKey)
    || (nextKey !== current.fieldKey && containsField(schema, nextKey))) {
    fail();
  }

  const constraints = patch.constraints === undefined
    ? current.constraints
    : sanitizeConstraints(patch.constraints, component.constraintKeys);
  return mapFields(schema, field => field.fieldKey === fieldKey
    ? {
        ...field,
        fieldKey: nextKey,
        required: patch.required ?? field.required,
        constraints
      }
    : field);
}

/** 删除字段；字段删除后为空的非最后 Section 一并删除，整个表单始终至少保留一个字段。 */
export function removeWorkflowFormField(
  schema: WorkflowFormSchema,
  fieldKey: string
): WorkflowFormSchema {
  assertSchemaShape(schema);
  const totalFields = schema.sections.reduce((count, section) => count + section.fields.length, 0);
  if (totalFields <= 1 || !containsField(schema, fieldKey)) {
    fail();
  }

  return {
    ...schema,
    sections: schema.sections
      .map(section => ({
        ...section,
        fields: section.fields.filter(field => field.fieldKey !== fieldKey)
      }))
      .filter(section => section.fields.length > 0)
  };
}

function assertCatalogMatches(
  schema: WorkflowFormSchema,
  catalog: WorkflowFormComponentCatalogResponse
): void {
  assertSchemaShape(schema);
  if (catalog.schemaVersion !== schema.schemaVersion
    || catalog.adapterVersion !== schema.adapterVersion) {
    fail();
  }
}

function assertSchemaShape(schema: WorkflowFormSchema): void {
  if (schema.schemaVersion !== 1
    || schema.adapterVersion !== 1
    || schema.sections.length === 0
    || schema.sections.length > MAX_SECTIONS) {
    fail();
  }
}

function findEnabledComponent(
  catalog: WorkflowFormComponentCatalogResponse,
  fieldTypeKey: string
) {
  if (!WORKFLOW_FIELD_TYPES.some(type => type === fieldTypeKey)) {
    return undefined;
  }
  return catalog.components.find(component => component.fieldTypeKey === fieldTypeKey
    && component.designable
    && component.publishable
    && component.executable);
}

function defaultConstraints(fieldTypeKey: WorkflowFieldType): Readonly<Record<string, unknown>> {
  if (fieldTypeKey === 'money' || fieldTypeKey === 'decimal') {
    return { scale: 2 };
  }
  if (fieldTypeKey === 'radio' || fieldTypeKey === 'checkbox' || fieldTypeKey === 'select') {
    return { options: ['option1'] };
  }
  if (fieldTypeKey === 'attachment') {
    return {
      maxCount: 3,
      maxSizeBytes: 10_485_760,
      allowedExtensions: ['pdf', 'png', 'jpg', 'jpeg', 'doc', 'docx', 'xls', 'xlsx']
    };
  }
  if (fieldTypeKey === 'subtable') {
    return { ...createDefaultSubtableConstraints() };
  }
  return {};
}

function sanitizeConstraints(
  constraints: Readonly<Record<string, unknown>>,
  allowedKeys: readonly string[]
): Readonly<Record<string, unknown>> {
  const result: Record<string, unknown> = {};
  for (const [key, value] of Object.entries(constraints)) {
    if (!allowedKeys.includes(key)) {
      continue;
    }
    if (!isSafeConstraintValue(value)) {
      fail();
    }
    result[key] = cloneConstraintValue(value);
  }
  return result;
}

function cloneConstraintValue(value: unknown): unknown {
  if (Array.isArray(value)) {
    return value.map(item => typeof item === 'object' && item !== null && !Array.isArray(item)
      ? { ...item }
      : item);
  }
  return value;
}

function isSafeConstraintValue(value: unknown): boolean {
  if (isSafeSubtableColumnsValue(value)) {
    return true;
  }

  return typeof value === 'string'
    || typeof value === 'number' && Number.isFinite(value)
    || Array.isArray(value) && value.length > 0
      && value.every(item => typeof item === 'string' && item.trim().length > 0)
      && new Set(value).size === value.length;
}

function isSafeSubtableColumnsValue(value: unknown): boolean {
  if (!Array.isArray(value) || value.length === 0 || value.length > 16) {
    return false;
  }

  const keys = new Set<string>();
  return value.every(column => {
    if (!isRecord(column)
      || typeof column.columnKey !== 'string'
      || !isStableKey(column.columnKey)
      || keys.has(column.columnKey)
      || typeof column.fieldTypeKey !== 'string'
      || !WORKFLOW_FIELD_TYPES.some(type => type === column.fieldTypeKey
        && type !== 'attachment'
        && type !== 'subtable')
      || typeof column.required !== 'boolean'
      || !isRecord(column.constraints)) {
      return false;
    }

    keys.add(column.columnKey);
    const allowed = WORKFLOW_FORM_CONSTRAINT_KEYS[column.fieldTypeKey as WorkflowFieldType];
    return Object.keys(column.constraints).every(key => allowed.has(key))
      && Object.values(column.constraints).every(item =>
        typeof item === 'string'
        || typeof item === 'number' && Number.isFinite(item)
        || Array.isArray(item) && item.length > 0
          && item.every(entry => typeof entry === 'string' && entry.trim().length > 0)
          && new Set(item).size === item.length);
  });
}

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

function isRecord(value: unknown): value is Record<string, unknown> {
  return typeof value === 'object' && value !== null && !Array.isArray(value);
}

function replaceSection(
  schema: WorkflowFormSchema,
  sectionKey: string,
  update: (section: WorkflowFormSection) => WorkflowFormSection
): WorkflowFormSchema {
  return {
    ...schema,
    sections: schema.sections.map(section =>
      section.sectionKey === sectionKey ? update(section) : section)
  };
}

function mapFields(
  schema: WorkflowFormSchema,
  update: (field: WorkflowFormField) => WorkflowFormField
): WorkflowFormSchema {
  return {
    ...schema,
    sections: schema.sections.map(section => ({
      ...section,
      fields: section.fields.map(update)
    }))
  };
}

function containsField(schema: WorkflowFormSchema, fieldKey: string): boolean {
  return schema.sections.some(section =>
    section.fields.some(field => field.fieldKey === fieldKey));
}

function isStableKey(value: string): boolean {
  return value.length <= 64
    && /^[A-Za-z][A-Za-z0-9_.-]*$/u.test(value)
    && !forbiddenKeys.has(value.toLowerCase());
}

function fail(): never {
  throw new Error(INVALID_DRAFT_ERROR);
}
