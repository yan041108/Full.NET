/*
 * @Date: 2022-08-25 14:13:11
 * @LastEditors: StavinLi 495727881@qq.com
 * @LastEditTime: 2023-05-24 15:00:32
 * @FilePath: /Workflow-Vue3/src/store/index.js
 */
import { defineStore } from 'pinia';

export const useStore = defineStore('workflow-vue3-store', {
  state: () => ({
    tableId: '',
    isTried: false,
    promoterDrawer: false,
    flowPermission1: {},
    approverDrawer: false,
    approverConfig1: {},
    copyerDrawer: false,
    copyerConfig1: {},
    /** 字段权限配置 { [stageId]: { [fieldKey]: mode } } */
    fieldPermissionJson: {},
    /** 触发器抽屉显隐 */
    triggerDrawer: false,
    /** 触发器配置 */
    triggerConfig1: {},
    delayDrawer: false,
    delayConfig1: {},
    dynamicRouteDrawer: false,
    dynamicRouteConfig1: {},
    modifyDataDrawer: false,
    modifyDataConfig1: {},
    deleteDataDrawer: false,
    deleteDataConfig1: {},
    /** 子流程抽屉显隐 */
    subProcessDrawer: false,
    /** 子流程节点配置 */
    subProcessConfig1: {},
    /** 当前流程ID */
    currentFlowId: '',
    flowNodeConfig: {},
    conditionDrawer: false,
    conditionsConfig1: {
      conditionNodes: [],
    },
    conditions: [],
  }),
  actions: {
    setTableId(
      /** 表ID */ payload
    ) {
      this.tableId = payload
    },
    setConditions(
      /** 条件集合 */ payload
    ) {
      this.conditions = payload
    },
    setIsTried(
      /** 是否已触发校验 */ payload
    ) {
      this.isTried = payload
    },
    setPromoter(
      /** 抽屉显隐 */ payload
    ) {
      this.promoterDrawer = payload
    },
    setFlowPermission(
      /** 发起人配置 */ payload
    ) {
      this.flowPermission1 = payload
    },
    setApprover(
      /** 抽屉显隐 */ payload
    ) {
      this.approverDrawer = payload
    },
    setApproverConfig(
      /** 审批节点配置 */ payload
    ) {
      this.approverConfig1 = payload
    },
    setCopyer(
      /** 抽屉显隐 */ payload
    ) {
      this.copyerDrawer = payload
    },
    setCopyerConfig(
      /** 抄送节点配置 */ payload
    ) {
      this.copyerConfig1 = payload
    },
    setFieldPermissionJson(
      /** 字段权限配置 */ payload
    ) {
      this.fieldPermissionJson = payload
    },
    setTrigger(
      /** 抽屉显隐 */ payload
    ) {
      this.triggerDrawer = payload
    },
    setTriggerConfig(
      /** 触发器节点配置 */ payload
    ) {
      this.triggerConfig1 = payload
    },
    setDelay(payload) {
      this.delayDrawer = payload
    },
    setDelayConfig(payload) {
      this.delayConfig1 = payload
    },
    setDynamicRoute(payload) {
      this.dynamicRouteDrawer = payload
    },
    setDynamicRouteConfig(payload) {
      this.dynamicRouteConfig1 = payload
    },
    setModifyData(
      /** 抽屉显隐 */ payload
    ) {
      this.modifyDataDrawer = payload
    },
    setModifyDataConfig(
      /** 修改数据节点配置 */ payload
    ) {
      this.modifyDataConfig1 = payload
    },
    setDeleteData(
      /** 抽屉显隐 */ payload
    ) {
      this.deleteDataDrawer = payload
    },
    setDeleteDataConfig(
      /** 删除数据节点配置 */ payload
    ) {
      this.deleteDataConfig1 = payload
    },
    setSubProcess(
      /** 抽屉显隐 */ payload
    ) {
      this.subProcessDrawer = payload
    },
    setSubProcessConfig(
      /** 子流程节点配置 */ payload
    ) {
      this.subProcessConfig1 = payload
    },
    setCurrentFlowId(
      /** 当前流程ID */ payload
    ) {
      this.currentFlowId = payload || ''
    },
    setFlowNodeConfig(
      /** 流程节点配置 */ payload
    ) {
      this.flowNodeConfig = payload
    },
    setCondition(
      /** 抽屉显隐 */ payload
    ) {
      this.conditionDrawer = payload
    },
    setConditionsConfig(
      /** 条件节点配置 */ payload
    ) {
      this.conditionsConfig1 = payload
    },
  }
})
