import type { WorkflowFormComponentCatalogResponse } from './generated/index.generated.js';
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
    result[key] = Array.isArray(value) ? [...value] : value;
  }
  return result;
}

function isSafeConstraintValue(value: unknown): boolean {
  return typeof value === 'string'
    || typeof value === 'number' && Number.isFinite(value)
    || Array.isArray(value) && value.length > 0
      && value.every(item => typeof item === 'string' && item.trim().length > 0)
      && new Set(value).size === value.length;
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
