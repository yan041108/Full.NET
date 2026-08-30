/*
 * @Date: 2023-03-29 15:25:37
 * @LastEditors: StavinLi 495727881@qq.com
 * @LastEditTime: 2023-03-29 15:52:38
 * @FilePath: /Workflow-Vue3/src/utils/const.js
 */

export let bgColors = ['87, 106, 149', '255, 148, 62', '50, 150, 250']
export let placeholderList = ["发起人", "审核人", "抄送人"];

/**
 * 节点类型（优先对齐 FlyFlow 的 type 值；部分为扩展类型）
 */
export const NodeType = {
  /** 发起人 */
  Starter: 0,
  /** 审批人 */
  Approver: 1,
  /** 抄送人 */
  Copyer: 2,
  /** 条件分支子节点（Workflow-Vue3 内部使用） */
  ConditionItem: 3,
  /** 条件分支网关（兼容历史：Workflow-Vue3 旧值 4） */
  RouteLegacy: 4,
  /** 触发器（FlyFlow：6） */
  Trigger: 6,
  /** 延时器（FlyFlow：7） */
  Delay: 7,
  /** 子流程（FlyFlow：9） */
  SubProcess: 9,
  /** 路由网关（FlyFlow：10） */
  Route: 10,
  /** 异步触发器（FlyFlow：11） */
  AsyncTrigger: 11,
  /** 并行分支（扩展） */
  ParallelRoute: 12,
  /** 包容分支（扩展） */
  InclusiveRoute: 13,
  /** 动态路由（扩展） */
  DynamicRoute: 14,
  /** 修改数据（扩展） */
  ModifyData: 15,
  /** 删除数据（扩展） */
  DeleteData: 16,
  /** 抢单（扩展） */
  Rob: 17,
  /** 办理人（扩展） */
  Handler: 18,
  /** 投票（扩展） */
  Vote: 19,
  /** 跨租户审批（扩展） */
  CrossTenantApprover: 20,
  /** 分组审批（扩展） */
  GroupApprover: 21,
  /** 空节点（扩展） */
  Empty: 22,
}

/**
 * 节点元信息（用于渲染、默认文案、基础配置入口）
 */
export const nodeTypeMetaMap = {
  [NodeType.Starter]: { label: '发起人', icon: '', bgIndex: 0, configMode: 'promoter' },
  [NodeType.Approver]: { label: '审批人', icon: '', bgIndex: 1, configMode: 'approver' },
  [NodeType.Copyer]: { label: '抄送人', icon: '', bgIndex: 2, configMode: 'copyer' },
  [NodeType.Trigger]: { label: '触发器', icon: '', bgIndex: 0, configMode: 'json' },
  [NodeType.AsyncTrigger]: { label: '异步触发器', icon: '', bgIndex: 0, configMode: 'json' },
  [NodeType.Delay]: { label: '延时器', icon: '', bgIndex: 0, configMode: 'json' },
  [NodeType.SubProcess]: { label: '子流程', icon: '', bgIndex: 0, configMode: 'json' },
  [NodeType.Route]: { label: '条件分支', icon: '', bgIndex: 0, configMode: 'condition' },
  [NodeType.RouteLegacy]: { label: '条件分支', icon: '', bgIndex: 0, configMode: 'condition' },
  [NodeType.ParallelRoute]: { label: '并行分支', icon: '', bgIndex: 0, configMode: 'condition' },
  [NodeType.InclusiveRoute]: { label: '包容分支', icon: '', bgIndex: 0, configMode: 'condition' },
  [NodeType.DynamicRoute]: { label: '动态路由', icon: '', bgIndex: 0, configMode: 'json' },
  [NodeType.ModifyData]: { label: '修改数据', icon: '', bgIndex: 0, configMode: 'json' },
  [NodeType.DeleteData]: { label: '删除数据', icon: '', bgIndex: 0, configMode: 'json' },
  [NodeType.Rob]: { label: '抢单', icon: '', bgIndex: 1, configMode: 'approver' },
  [NodeType.Handler]: { label: '办理人', icon: '', bgIndex: 1, configMode: 'approver' },
  [NodeType.Vote]: { label: '投票', icon: '', bgIndex: 1, configMode: 'approver' },
  [NodeType.CrossTenantApprover]: { label: '跨租户审批', icon: '', bgIndex: 1, configMode: 'approver' },
  [NodeType.GroupApprover]: { label: '分组审批', icon: '', bgIndex: 1, configMode: 'approver' },
  [NodeType.Empty]: { label: '空节点', icon: '', bgIndex: 0, configMode: 'json' },
}

/**
 * 获取节点元信息
 * @param {number} type 节点类型
 */
export const getNodeTypeMeta = (type) => {
  return nodeTypeMetaMap[type] || { label: '节点', icon: '', bgIndex: 0, configMode: 'json' }
}

/**
 * 新增节点面板分组（用于渲染新增节点弹层）
 */
export const addNodeMenuGroups = [
  {
    group: '人工节点',
    items: [
      { type: NodeType.Approver, label: '审批人', cls: 'approver', icon: nodeTypeMetaMap[NodeType.Approver].icon },
      { type: NodeType.GroupApprover, label: '分组审批', cls: 'approver', icon: nodeTypeMetaMap[NodeType.GroupApprover].icon },
      { type: NodeType.Handler, label: '办理人', cls: 'approver', icon: nodeTypeMetaMap[NodeType.Handler].icon },
      { type: NodeType.Vote, label: '投票', cls: 'approver', icon: nodeTypeMetaMap[NodeType.Vote].icon },
      { type: NodeType.Rob, label: '抢单', cls: 'approver', icon: nodeTypeMetaMap[NodeType.Rob].icon },
      { type: NodeType.CrossTenantApprover, label: '跨租户审批', cls: 'approver', icon: nodeTypeMetaMap[NodeType.CrossTenantApprover].icon },
      { type: NodeType.Copyer, label: '抄送人', cls: 'notifier', icon: nodeTypeMetaMap[NodeType.Copyer].icon },
    ],
  },
  {
    group: '路由节点',
    items: [
      { type: NodeType.Route, label: '条件分支', cls: 'condition', icon: nodeTypeMetaMap[NodeType.Route].icon },
      { type: NodeType.ParallelRoute, label: '并行分支', cls: 'condition', icon: nodeTypeMetaMap[NodeType.ParallelRoute].icon },
      { type: NodeType.InclusiveRoute, label: '包容分支', cls: 'condition', icon: nodeTypeMetaMap[NodeType.InclusiveRoute].icon },
      { type: NodeType.DynamicRoute, label: '动态路由', cls: 'condition', icon: nodeTypeMetaMap[NodeType.DynamicRoute].icon },
      { type: NodeType.Empty, label: '空节点', cls: 'condition', icon: nodeTypeMetaMap[NodeType.Empty].icon },
    ],
  },
  {
    group: '集成/过程节点',
    items: [
      { type: NodeType.Trigger, label: '触发器', cls: 'condition', icon: nodeTypeMetaMap[NodeType.Trigger].icon },
      { type: NodeType.AsyncTrigger, label: '异步触发器', cls: 'condition', icon: nodeTypeMetaMap[NodeType.AsyncTrigger].icon },
      { type: NodeType.Delay, label: '延时器', cls: 'condition', icon: nodeTypeMetaMap[NodeType.Delay].icon },
      { type: NodeType.SubProcess, label: '子流程', cls: 'condition', icon: nodeTypeMetaMap[NodeType.SubProcess].icon },
      { type: NodeType.ModifyData, label: '修改数据', cls: 'condition', icon: nodeTypeMetaMap[NodeType.ModifyData].icon },
      { type: NodeType.DeleteData, label: '删除数据', cls: 'condition', icon: nodeTypeMetaMap[NodeType.DeleteData].icon },
    ],
  },
]

export let setTypes = [
  {value: 1, label: '指定成员'},
  {value: 2, label: '主管'},
  {value: 3, label: '指定角色'},
  {value: 4, label: '发起人自选'},
  {value: 5, label: '发起人自己'},
  {value: 6, label: '指定岗位'},
  {value: 7, label: '连续多级主管'},
  {value: 8, label: '指定部门'},
  {value: 9, label: '表单中人员'},
]

export let examineModes = [
  { value: 1, label: '依次审批' },
  { value: 2, label: '会签(须所有审批人同意)' },
  { value: 3, label: '或签(一名审批人同意即可)' },
  { value: 4, label: '并签(同时审批)' },
]

export let countersignTypes = [
  { value: 1, label: '按比例' },
]

export let sameAsStarterActions = [
  { value: 1, label: '发起人继续处理' },
  { value: 2, label: '发起人不用处理' },
  { value: 3, label: '转交给发起人部门主管处理' },
  { value: 4, label: '转交给流程管理员处理' },
  { value: 5, label: '转交给发起人直属领导处理' },
]

export let noHanderActions = [
  { value: 1, label: '自动通过' },
  { value: 2, label: '自动拒绝' },
  { value: 3, label: '指定人员' },
  { value: 4, label: '转交给流程管理员' },
  { value: 5, label: '报错提醒' },
]

export let rejectActions = [
  { value: 1, label: '直接结束流程' },
  { value: 2, label: '驳回到指定节点' },
]

export let selectModes = [
  {value: 1, label: '选一个人'},
  {value: 2, label: '选多个人'},
]

export let selectRanges = [
  {value: 1, label: '全公司'},
  {value: 2, label: '指定成员'},
  {value: 3, label: '指定角色'},
]

export let optTypes = [
  {value: '1', label: '小于'},
  {value: '2', label: '大于'},
  {value: '3', label: '小于等于'},
  {value: '4', label: '等于'},
  {value: '5', label: '大于等于'},
  {value: '6', label: '介于两个数之间'},
]

export let opt1s = [
  {value: '<', label: '<'},
  {value: '≤', label: '≤'},
]
