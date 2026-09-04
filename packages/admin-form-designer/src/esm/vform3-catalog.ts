/** Full.NET 当前允许进入 VForm3 ESM 设计器的字段目录项。 */
export interface VForm3CatalogItem {
  key: string;
  type: string;
  fieldTypeKey: string;
  label: string;
}

/**
 * 设计器目录与 Workflow Adapter 的闭合映射保持一致。
 * 不在此目录中的富文本、文件、图片、HTML 和扩展组件不会进入生产依赖图。
 */
export const vform3SafeCatalog: readonly VForm3CatalogItem[] = [
  { key: 'input', type: 'input', fieldTypeKey: 'text', label: '单行文本' },
  { key: 'textarea', type: 'textarea', fieldTypeKey: 'textarea', label: '多行文本' },
  { key: 'integer', type: 'number', fieldTypeKey: 'integer', label: '整数' },
  { key: 'number', type: 'number', fieldTypeKey: 'decimal', label: '小数' },
  { key: 'money', type: 'number', fieldTypeKey: 'money', label: '金额' },
  { key: 'date', type: 'date', fieldTypeKey: 'date', label: '日期' },
  { key: 'datetime', type: 'date', fieldTypeKey: 'datetime', label: '日期时间' },
  { key: 'time', type: 'time', fieldTypeKey: 'time', label: '时间' },
  { key: 'radio', type: 'radio', fieldTypeKey: 'radio', label: '单选' },
  { key: 'checkbox', type: 'checkbox', fieldTypeKey: 'checkbox', label: '多选' },
  { key: 'select', type: 'select', fieldTypeKey: 'select', label: '下拉选择' },
  { key: 'switch', type: 'switch', fieldTypeKey: 'switch', label: '开关' }
];

/** 返回安全目录中的中文显示名，未知类型明确标记而不动态加载组件。 */
export function getVForm3WidgetLabel(type: string, fieldTypeKey?: string): string {
  return vform3SafeCatalog.find(item => item.type === type
    && (fieldTypeKey === undefined || item.fieldTypeKey === fieldTypeKey))?.label
    ?? vform3SafeCatalog.find(item => item.type === type)?.label
    ?? `未知控件（${type}）`;
}
