import type {
  WorkflowFormComponentCatalogResponse,
  WorkflowFormField,
  WorkflowFormSchema
} from '@fullnet/client-contracts';

export interface VForm3DesignerJson {
  readonly widgetList: VForm3Widget[];
  readonly formConfig: Record<string, unknown>;
}

interface VForm3Widget {
  readonly id?: string;
  readonly type?: string;
  readonly options?: Record<string, unknown>;
  readonly cols?: unknown;
  readonly widgetList?: unknown;
  readonly children?: unknown;
  readonly [key: string]: unknown;
}

const fieldToWidgetType: Readonly<Record<string, string>> = {
  text: 'input',
  textarea: 'textarea',
  integer: 'number',
  decimal: 'number',
  money: 'number',
  date: 'date',
  time: 'time',
  datetime: 'date',
  radio: 'radio',
  checkbox: 'checkbox',
  select: 'select',
  switch: 'switch'
};

const widgetToFieldType: Readonly<Record<string, string>> = {
  input: 'text',
  textarea: 'textarea',
  number: 'decimal',
  date: 'date',
  time: 'time',
  radio: 'radio',
  checkbox: 'checkbox',
  select: 'select',
  switch: 'switch'
};

const unsafePropertyPattern = /(?:script|function|javascript|remote|url|header|body|iframe|html|css|onform|onevent|customclass)/iu;
const stableKeyPattern = /^[A-Za-z][A-Za-z0-9_.-]{0,63}$/u;

/** 把服务端权威表单 Schema 投影为 VForm3 的设计态 JSON。 */
export function toVFormDesignerJson(schema: WorkflowFormSchema): VForm3DesignerJson {
  return {
    widgetList: schema.sections.flatMap(section => section.fields.map(field => ({
      id: `fn-${field.fieldKey}`,
      type: fieldToWidgetType[field.fieldTypeKey] ?? 'input',
      options: toWidgetOptions(section.sectionKey, field)
    }))),
    formConfig: {
      modelName: 'formData',
      refName: 'fullNetWorkflowForm',
      labelWidth: 120,
      labelPosition: 'left',
      fullNetSchemaVersion: schema.schemaVersion,
      fullNetAdapterVersion: schema.adapterVersion
    }
  };
}

/**
 * 把 VForm3 设计态 JSON 编译回闭合 Schema。
 * 未在服务端目录出现的控件和带脚本/远程配置的内容会失败关闭。
 */
export function fromVFormDesignerJson(
  raw: unknown,
  catalog: WorkflowFormComponentCatalogResponse
): WorkflowFormSchema {
  if (!isRecord(raw) || !Array.isArray(raw.widgetList) || !isRecord(raw.formConfig)) {
    throw new Error('client.invalid_workflow_form_draft');
  }
  assertSafeConfiguration(raw.formConfig);

  const allowed = new Map(catalog.components
    .filter(component => component.designable && component.publishable && component.executable)
    .map(component => [component.fieldTypeKey, component]));
  const sections = new Map<string, WorkflowFormField[]>();
  const fieldKeys = new Set<string>();
  for (const widget of collectWidgets(raw.widgetList)) {
    const type = String(widget.type ?? '').trim().toLowerCase();
    const options = isRecord(widget.options) ? widget.options : {};
    assertSafeConfiguration(options);
    const metadataType = typeof options.fullNetFieldType === 'string'
      ? options.fullNetFieldType
      : undefined;
    const fieldTypeKey = metadataType ?? widgetToFieldType[type];
    if (fieldTypeKey === undefined || !allowed.has(fieldTypeKey)) {
      throw new Error('client.unsupported_vform_component');
    }

    const fieldKey = typeof options.name === 'string' ? options.name.trim() : '';
    const sectionKey = typeof options.fullNetSectionKey === 'string'
      ? options.fullNetSectionKey.trim()
      : 'main';
    if (!stableKeyPattern.test(fieldKey) || !stableKeyPattern.test(sectionKey) || fieldKeys.has(fieldKey)) {
      throw new Error('client.invalid_workflow_form_draft');
    }
    fieldKeys.add(fieldKey);

    const component = allowed.get(fieldTypeKey)!;
    const field: WorkflowFormField = {
      fieldKey,
      fieldTypeKey: fieldTypeKey as WorkflowFormField['fieldTypeKey'],
      required: options.required === true,
      constraints: readConstraints(fieldTypeKey, options, component.constraintKeys)
    };
    const fields = sections.get(sectionKey) ?? [];
    fields.push(field);
    sections.set(sectionKey, fields);
  }

  if (sections.size === 0) {
    throw new Error('client.invalid_workflow_form_draft');
  }

  return {
    schemaVersion: 1,
    adapterVersion: 1,
    sections: Array.from(sections, ([sectionKey, fields]) => ({ sectionKey, fields }))
  };
}

function toWidgetOptions(sectionKey: string, field: WorkflowFormField): Record<string, unknown> {
  const options: Record<string, unknown> = {
    name: field.fieldKey,
    label: field.fieldKey,
    required: field.required,
    fullNetSectionKey: sectionKey,
    fullNetFieldType: field.fieldTypeKey
  };
  const constraints = field.constraints;
  if (typeof constraints.minLength === 'number') options.minLength = constraints.minLength;
  if (typeof constraints.maxLength === 'number') options.maxLength = constraints.maxLength;
  if (typeof constraints.minimum === 'number') options.min = constraints.minimum;
  if (typeof constraints.maximum === 'number') options.max = constraints.maximum;
  if (typeof constraints.scale === 'number') options.precision = constraints.scale;
  if (Array.isArray(constraints.options)) {
    options.optionItems = constraints.options.map(value => ({ label: String(value), value: String(value) }));
  }
  if (field.fieldTypeKey === 'datetime') options.type = 'datetime';
  return options;
}

function readConstraints(
  fieldTypeKey: string,
  options: Record<string, unknown>,
  supported: readonly string[]
): Readonly<Record<string, unknown>> {
  const result: Record<string, unknown> = {};
  if (supported.includes('minLength') && isFiniteNumber(options.minLength)) result.minLength = options.minLength;
  if (supported.includes('maxLength') && isFiniteNumber(options.maxLength)) result.maxLength = options.maxLength;
  if (supported.includes('minimum') && isFiniteNumber(options.min)) result.minimum = options.min;
  if (supported.includes('maximum') && isFiniteNumber(options.max)) result.maximum = options.max;
  if (supported.includes('scale') && isFiniteNumber(options.precision)) result.scale = options.precision;
  if (supported.includes('options')) {
    const items = Array.isArray(options.optionItems) ? options.optionItems : [];
    result.options = items.map(item => isRecord(item) ? item.value ?? item.label : item)
      .filter(value => typeof value === 'string' || typeof value === 'number')
      .map(String);
  }
  if (fieldTypeKey === 'integer') delete result.scale;
  return result;
}

function collectWidgets(items: unknown[]): VForm3Widget[] {
  const result: VForm3Widget[] = [];
  const visit = (value: unknown): void => {
    if (Array.isArray(value)) {
      value.forEach(visit);
      return;
    }
    if (!isRecord(value)) return;
    if (typeof value.type === 'string' && isRecord(value.options)) {
      result.push(value as VForm3Widget);
      return;
    }
    Object.values(value).forEach(visit);
  };
  visit(items);
  return result;
}

function assertSafeConfiguration(value: unknown): void {
  if (Array.isArray(value)) {
    value.forEach(assertSafeConfiguration);
    return;
  }
  if (!isRecord(value)) return;
  for (const [key, child] of Object.entries(value)) {
    if (unsafePropertyPattern.test(key) && hasConfiguredValue(child)) {
      throw new Error('client.unsafe_vform_configuration');
    }
    if (typeof child === 'string' && /(?:javascript:|https?:\/\/|<script|<iframe)/iu.test(child)) {
      throw new Error('client.unsafe_vform_configuration');
    }
    assertSafeConfiguration(child);
  }
}

function hasConfiguredValue(value: unknown): boolean {
  if (value === undefined || value === null || value === false || value === '') return false;
  if (Array.isArray(value)) return value.length > 0;
  if (isRecord(value)) return Object.keys(value).length > 0;
  return true;
}

function isFiniteNumber(value: unknown): value is number {
  return typeof value === 'number' && Number.isFinite(value);
}

function isRecord(value: unknown): value is Record<string, unknown> {
  return typeof value === 'object' && value !== null && !Array.isArray(value);
}
