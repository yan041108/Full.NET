import {
  WORKFLOW_FIELD_TYPES,
  type WorkflowFieldType,
  type WorkflowFormField
} from './workflow-todos.js';

/** 明细子表列允许使用的标量字段类型，禁止嵌套子表与附件。 */
export const WORKFLOW_SUBTABLE_COLUMN_FIELD_TYPES = WORKFLOW_FIELD_TYPES.filter(type =>
  type !== 'attachment' && type !== 'subtable');

export type WorkflowSubtableColumnFieldType = typeof WORKFLOW_SUBTABLE_COLUMN_FIELD_TYPES[number];

export interface WorkflowSubtableColumnDefinition {
  readonly columnKey: string;
  readonly fieldTypeKey: WorkflowSubtableColumnFieldType;
  readonly required: boolean;
  readonly constraints: Readonly<Record<string, unknown>>;
}

export interface WorkflowSubtableConstraints {
  readonly maxRows: number;
  readonly columns: readonly WorkflowSubtableColumnDefinition[];
}

const MAX_SUBTABLE_ROWS = 20;
const MAX_SUBTABLE_COLUMNS = 16;

/** 从字段约束中读取明细子表配置；结构非法时返回 undefined。 */
export function readWorkflowSubtableConstraints(
  field: WorkflowFormField
): WorkflowSubtableConstraints | undefined {
  if (field.fieldTypeKey !== 'subtable') {
    return undefined;
  }

  const maxRows = field.constraints.maxRows;
  const columns = field.constraints.columns;
  if (!Number.isSafeInteger(maxRows)
    || Number(maxRows) < 1
    || Number(maxRows) > MAX_SUBTABLE_ROWS
    || !isSubtableColumnsValue(columns)) {
    return undefined;
  }

  return {
    maxRows: Number(maxRows),
    columns
  };
}

/** 创建带单列文本的安全默认子表约束。 */
export function createDefaultSubtableConstraints(): WorkflowSubtableConstraints {
  return {
    maxRows: 5,
    columns: [{
      columnKey: 'itemName',
      fieldTypeKey: 'text',
      required: true,
      constraints: { minLength: 1, maxLength: 128 }
    }]
  };
}

export function isSubtableRowValue(value: unknown): value is Readonly<Record<string, unknown>> {
  return typeof value === 'object' && value !== null && !Array.isArray(value);
}

export function readSubtableRows(fieldKey: string, values: Readonly<Record<string, unknown>>): readonly Readonly<Record<string, unknown>>[] {
  const value = values[fieldKey];
  if (!Array.isArray(value)) {
    return [];
  }

  return value.filter(isSubtableRowValue);
}

function isSubtableColumnsValue(value: unknown): value is readonly WorkflowSubtableColumnDefinition[] {
  if (!Array.isArray(value) || value.length === 0 || value.length > MAX_SUBTABLE_COLUMNS) {
    return false;
  }

  const keys = new Set<string>();
  return value.every(column => {
    if (!isRecord(column)
      || typeof column.columnKey !== 'string'
      || !isStableColumnKey(column.columnKey)
      || keys.has(column.columnKey)
      || typeof column.fieldTypeKey !== 'string'
      || !WORKFLOW_SUBTABLE_COLUMN_FIELD_TYPES.some(type => type === column.fieldTypeKey)
      || typeof column.required !== 'boolean'
      || !isRecord(column.constraints)) {
      return false;
    }

    keys.add(column.columnKey);
    const allowed = WORKFLOW_FORM_COLUMN_CONSTRAINT_KEYS[column.fieldTypeKey as WorkflowSubtableColumnFieldType];
    return Object.keys(column.constraints).every(key => allowed.has(key))
      && Object.values(column.constraints).every(isSafeScalarConstraintValue);
  });
}

const WORKFLOW_FORM_COLUMN_CONSTRAINT_KEYS: Readonly<Record<WorkflowSubtableColumnFieldType, ReadonlySet<string>>> = {
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

function isStableColumnKey(value: string): boolean {
  return value.length > 0
    && value.length <= 64
    && /^[A-Za-z][A-Za-z0-9_.-]*$/u.test(value)
    && !['__proto__', 'prototype', 'constructor'].includes(value.toLowerCase());
}

function isSafeScalarConstraintValue(value: unknown): boolean {
  return typeof value === 'string'
    || typeof value === 'number' && Number.isFinite(value)
    || Array.isArray(value) && value.length > 0
      && value.every(item => typeof item === 'string' && item.trim().length > 0)
      && new Set(value).size === value.length;
}

function isRecord(value: unknown): value is Record<string, unknown> {
  return typeof value === 'object' && value !== null && !Array.isArray(value);
}
