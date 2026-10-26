export const organizationLeadershipZh = {
  'orgReporting.title': '{name}的直属上级',
  'orgReporting.manager': '直属上级',
  'orgReporting.failed': '无法读取或保存直属汇报关系。',
  'orgReporting.help': '明确配置人员汇报关系，需要完整组织数据范围。部门负责人不会自动成为直属上级；清空表示没有直属上级。',
  'orgUnitLeadership.action': '机构负责人',
  'orgUnitLeadership.title': '{name} · 负责人',
  'orgUnitLeadership.help': '负责人由显式授权配置，流程选人会读取此关系。岗位职级或普通隶属不代表负责人。清空表示未配置。',
  'orgUnitLeadership.leader': '负责人',
  'orgUnitLeadership.failed': '无法读取或保存负责人配置。',
  'orgUnitLeadership.refresh': '刷新权威状态',
  'orgUnitLeadership.refreshHint': '结果可能未知或配置已变化，请先刷新状态再继续。',
  'orgUnitLeadership.unavailable': '已不在活动成员范围',
  'orgUnitLeadership.unavailableHint': '原负责人已失效。该记录会保留，流程不会自动改选普通成员；请明确更换或清空。',
  'orgUnitLeadership.inactiveUnit': '机构已禁用，当前配置只读。'
} as const;
export const organizationLeadershipEn = {
  'orgReporting.title': 'Direct manager for {name}',
  'orgReporting.manager': 'Direct manager',
  'orgReporting.failed': 'Unable to load or save the reporting relationship.',
  'orgReporting.help': 'Configure an explicit reporting relationship with full organization data scope. Department leadership does not assign a direct manager. Clear the selection to remove the manager.',
  'orgUnitLeadership.action': 'Unit leader',
  'orgUnitLeadership.title': '{name} · Leader',
  'orgUnitLeadership.help': 'Workflow reads this explicitly authorized relationship. Position rank or ordinary membership does not appoint a leader. Clear the selection to leave the unit unconfigured.',
  'orgUnitLeadership.leader': 'Leader',
  'orgUnitLeadership.failed': 'Unable to load or save the leadership configuration.',
  'orgUnitLeadership.refresh': 'Refresh authoritative state',
  'orgUnitLeadership.refreshHint': 'The outcome may be unknown or the configuration changed. Refresh before continuing.',
  'orgUnitLeadership.unavailable': 'No longer an active member',
  'orgUnitLeadership.unavailableHint': 'The previous leader is no longer eligible. The record is retained and Workflow will not appoint an ordinary member automatically. Choose a replacement or clear it explicitly.',
  'orgUnitLeadership.inactiveUnit': 'This unit is disabled. The configuration is read-only.'
} as const;
