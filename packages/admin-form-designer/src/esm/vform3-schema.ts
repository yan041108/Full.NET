/** VForm3 设计态中 Full.NET 安全子集使用的字段节点。 */
export interface VForm3Widget {
  id: string;
  type: string;
  options: Record<string, unknown>;
  [key: string]: unknown;
}

/** VForm3 3.0.10 兼容的顶层设计态 JSON。 */
export interface VForm3DesignerJson {
  widgetList: VForm3Widget[];
  formConfig: Record<string, unknown>;
}

/**
 * 校验并深克隆设计态 JSON。
 * 只允许纯 JSON 值进入响应式状态，确保设计器永远不会携带或执行函数对象。
 */
export function cloneDesignerJson(value: unknown): VForm3DesignerJson {
  if (!isPlainRecord(value)
    || !Array.isArray(value.widgetList)
    || !isPlainRecord(value.formConfig)) {
    throw new Error('client.invalid_vform3_json');
  }

  return {
    widgetList: value.widgetList.map(cloneWidget),
    formConfig: cloneRecord(value.formConfig)
  };
}

/** 为用户从组件目录新增的字段创建稳定的 VForm3 兼容节点。 */
export function createVForm3Widget(
  type: string,
  idFactory: () => string = () => crypto.randomUUID().replaceAll('-', '').slice(0, 12),
  fieldTypeKey?: string
): VForm3Widget {
  const suffix = idFactory();
  // 服务端发布编译要求小数与金额显式声明精度；新字段采用常用的两位精度，避免生成无法发布的草稿。
  const publishableNumericDefaults = fieldTypeKey === 'decimal' || fieldTypeKey === 'money'
    ? { precision: 2 }
    : {};
  return {
    id: `fn-${suffix}`,
    type,
    options: {
      name: `${type}_${suffix}`,
      label: `${type}_${suffix}`,
      required: false,
      fullNetSectionKey: 'main',
      ...publishableNumericDefaults,
      ...(fieldTypeKey === undefined ? {} : { fullNetFieldType: fieldTypeKey })
    }
  };
}

/** 将指定字段移动一个位置；越界移动保持原顺序。 */
export function moveVForm3Widget(
  widgets: readonly VForm3Widget[],
  index: number,
  offset: -1 | 1
): VForm3Widget[] {
  const target = index + offset;
  const result = widgets.map(cloneWidget);
  if (index < 0 || index >= result.length || target < 0 || target >= result.length) return result;
  const [widget] = result.splice(index, 1);
  if (widget !== undefined) result.splice(target, 0, widget);
  return result;
}

/** 删除指定字段；越界索引保持原顺序。 */
export function removeVForm3Widget(
  widgets: readonly VForm3Widget[],
  index: number
): VForm3Widget[] {
  const result = widgets.map(cloneWidget);
  if (index >= 0 && index < result.length) result.splice(index, 1);
  return result;
}

/** 深克隆并校验单个字段节点。 */
function cloneWidget(value: unknown): VForm3Widget {
  if (!isPlainRecord(value)
    || typeof value.id !== 'string'
    || typeof value.type !== 'string'
    || !isPlainRecord(value.options)) {
    throw new Error('client.invalid_vform3_json');
  }
  return {
    ...cloneRecord(value),
    id: value.id,
    type: value.type,
    options: cloneRecord(value.options)
  };
}

/** 递归克隆普通 JSON 对象。 */
function cloneRecord(value: Record<string, unknown>): Record<string, unknown> {
  return Object.fromEntries(Object.entries(value).map(([key, child]) => [key, cloneJsonValue(child)]));
}

/** 拒绝 undefined、函数、类实例等非 JSON 数据。 */
function cloneJsonValue(value: unknown): unknown {
  if (value === null || typeof value === 'string' || typeof value === 'boolean') return value;
  if (typeof value === 'number' && Number.isFinite(value)) return value;
  if (Array.isArray(value)) return value.map(cloneJsonValue);
  if (isPlainRecord(value)) return cloneRecord(value);
  throw new Error('client.invalid_vform3_json');
}

/** 仅接受原型为 Object 或 null 的记录，阻断类实例穿透。 */
function isPlainRecord(value: unknown): value is Record<string, unknown> {
  if (typeof value !== 'object' || value === null || Array.isArray(value)) return false;
  const prototype = Object.getPrototypeOf(value);
  return prototype === Object.prototype || prototype === null;
}
