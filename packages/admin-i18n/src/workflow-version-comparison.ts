export const workflowVersionComparisonZh = {
  'workflowVersionCompare.open': '比较已发布版本', 'workflowVersionCompare.title': '固定版本对比',
  'workflowVersionCompare.before': '原版本', 'workflowVersionCompare.after': '目标版本',
  'workflowVersionCompare.run': '读取并比较', 'workflowVersionCompare.loading': '正在重读固定版本…',
  'workflowVersionCompare.permission': '比较需要流程定义与表单的读取权限。',
  'workflowVersionCompare.failed': '版本已失效、不可见或内容无法比较，请重新读取版本列表。',
  'workflowVersionCompare.help': '比较规范定义、字段约束、节点字段授权及固定调用绑定；不会运行条件、展开人员或发起流程。',
  'workflowVersionCompare.impact': '变更仅作用于使用目标版本的新实例。已有实例继续固定原版本；对比不保证回填兼容或执行成功。',
  'workflowVersionCompare.count': '共 {count} 处变化', 'workflowVersionCompare.empty': '所比较的结构与绑定没有变化。',
  'workflowVersionCompare.truncated': '仅展示前 512 处变化，请缩小比较范围或分别检查固定资产。',
  'workflowVersionCompare.area': '影响范围', 'workflowVersionCompare.path': '位置',
  'workflowVersionCompare.area.definition': '流程类型与协议', 'workflowVersionCompare.area.node': '节点与执行配置',
  'workflowVersionCompare.area.field': '字段与约束', 'workflowVersionCompare.area.authorization': '字段授权',
  'workflowVersionCompare.area.binding': '固定表单、子流程或协作绑定', 'workflowVersionCompare.area.presentation': '名称与展示快照',
  'workflowVersionCompare.kind.added': '新增', 'workflowVersionCompare.kind.removed': '移除', 'workflowVersionCompare.kind.changed': '修改'
} as const;
export const workflowVersionComparisonEn: Record<keyof typeof workflowVersionComparisonZh, string> = {
  'workflowVersionCompare.open': 'Compare published versions', 'workflowVersionCompare.title': 'Fixed version comparison',
  'workflowVersionCompare.before': 'Original version', 'workflowVersionCompare.after': 'Target version',
  'workflowVersionCompare.run': 'Read and compare', 'workflowVersionCompare.loading': 'Reading fixed versions…',
  'workflowVersionCompare.permission': 'Definition and form read permissions are required.',
  'workflowVersionCompare.failed': 'A version is unavailable or cannot be compared. Reload the version list.',
  'workflowVersionCompare.help': 'Compare canonical definitions, field constraints, field policies and fixed call bindings. No rules, members or workflows are executed.',
  'workflowVersionCompare.impact': 'Changes apply to new instances using the target version. Existing instances keep their fixed versions. Comparison does not guarantee mapping compatibility or execution success.',
  'workflowVersionCompare.count': '{count} changes', 'workflowVersionCompare.empty': 'The compared structure and bindings are unchanged.',
  'workflowVersionCompare.truncated': 'Only the first 512 changes are shown. Narrow the comparison or inspect each fixed asset.',
  'workflowVersionCompare.area': 'Affected area', 'workflowVersionCompare.path': 'Location',
  'workflowVersionCompare.area.definition': 'Workflow kind and protocol', 'workflowVersionCompare.area.node': 'Nodes and execution configuration',
  'workflowVersionCompare.area.field': 'Fields and constraints', 'workflowVersionCompare.area.authorization': 'Field policies',
  'workflowVersionCompare.area.binding': 'Fixed form, subprocess or collaboration binding', 'workflowVersionCompare.area.presentation': 'Names and render snapshot',
  'workflowVersionCompare.kind.added': 'Added', 'workflowVersionCompare.kind.removed': 'Removed', 'workflowVersionCompare.kind.changed': 'Changed'
};
