/**
 * Host 管理端模块展示顺序，须与侧栏 navigationGroupRules 保持一致。
 * @see ../framework/art-design/adapters/fullNetShellAdapter.ts
 */
export const ADMIN_MODULE_GROUP_ORDER: readonly string[] = [
  'platform',
  'tenancy',
  'identity',
  'organization',
  'settings',
  'document',
  'files',
  'notifications',
  'jobs',
  'code-generation',
  'serial-numbers',
  'data-approval',
  'auditing',
  'messaging'
];

const moduleGroupOrderIndex = new Map(
  ADMIN_MODULE_GROUP_ORDER.map((moduleKey, index) => [moduleKey, index] as const)
);

/** 解析模块在侧栏/菜单树中的展示顺序；未知模块排在末尾。 */
export function resolveModuleGroupOrder(
  moduleKey: string,
  fallbackOrder = Number.MAX_SAFE_INTEGER
): number {
  return moduleGroupOrderIndex.get(moduleKey) ?? fallbackOrder;
}
