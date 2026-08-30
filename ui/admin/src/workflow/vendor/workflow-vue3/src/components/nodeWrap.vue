<!--
 * @Date: 2022-09-21 14:41:53
 * @LastEditors: StavinLi 495727881@qq.com
 * @LastEditTime: 2023-05-24 15:20:24
 * @FilePath: /Workflow-Vue3/src/components/nodeWrap.vue
-->
<template>
  <div class="node-wrap" v-if="isCardNode">
    <div class="node-wrap-box" :class="(nodeConfig.type == NodeType.Starter ? 'start-node ' : '') +(isTried && nodeConfig.error ? 'active error' : '')" @click="setPerson">
        <div class="title" :style="`background: rgb(${bgColors[nodeTypeMeta.bgIndex]});`">
          <span v-if="nodeConfig.type == NodeType.Starter">{{ nodeConfig.nodeName }}</span>
          <template v-else>
            <span class="iconfont">{{ nodeTypeMeta.icon }}</span>
            <input
              v-if="isInput"
              type="text"
              class="ant-input editable-title-input"
              @blur="blurEvent()"
              @focus="$event.currentTarget.select()"
              @click.stop
              v-focus
              v-model="nodeConfig.nodeName"
              :placeholder="defaultText"
            />
            <span v-else class="editable-title" @click.stop="clickEvent()">{{ nodeConfig.nodeName }}</span>
            <i class="anticon anticon-close close" @click.stop="delNode"></i>
          </template>
        </div>
        <div class="content" :class="{ 'content-error': isTried && nodeConfig.error && isApproverLike(nodeConfig.type) }">
          <div class="text">
              <i class="anticon anticon-exclamation-circle warn-icon" v-if="isTried && nodeConfig.error && isApproverLike(nodeConfig.type)"></i>
              <span class="placeholder" :class="{ 'placeholder-error': isTried && nodeConfig.error && isApproverLike(nodeConfig.type) }" v-if="!showText">请选择{{defaultText}}</span>
              {{showText}}
          </div>
          <i class="anticon anticon-right arrow"></i>
        </div>
        <div class="error_tip" v-if="isTried && nodeConfig.error" :title="nodeConfig.errorTip || '节点配置不完整'">
          <i class="anticon anticon-exclamation-circle"></i>
        </div>
    </div>
    <addNode v-model:childNodeP="nodeConfig.childNode" />
  </div>
  <div class="branch-wrap" v-if="isRouteNode">
    <div class="branch-box-wrap">
      <div class="branch-box">
        <button class="add-branch" @click="addTerm">添加条件</button>
        <div class="col-box" v-for="(item, index) in nodeConfig.conditionNodes" :key="index">
          <div class="condition-node">
            <div class="condition-node-box">
              <div class="auto-judge" :class="(isTried || item.localTried) && item.error ? 'error active' : ''">
                <div class="sort-left" v-if="index != 0" @click="arrTransfer(index, -1)">&lt;</div>
                <div class="title-wrapper">
                  <input
                    v-if="isInputList[index]"
                    type="text"
                    class="ant-input editable-title-input"
                    @blur="blurEvent(index)"
                    @focus="$event.currentTarget.select()"
                    v-focus
                    v-model="item.nodeName"
                  />
                  <span v-else class="editable-title" @click="clickEvent(index)">{{ item.nodeName }}</span>
                  <span class="priority-title" @click="setPerson(item.priorityLevel)">优先级{{ item.priorityLevel }}</span>
                  <span class="copy-branch" @click.stop="copyTerm(index)">复制</span>
                  <i class="anticon anticon-close close" @click="delTerm(index)"></i>
                </div>
                <div class="sort-right" v-if="index != nodeConfig.conditionNodes.length - 1" @click="arrTransfer(index)">&gt;</div>
                <div class="content" @click="setPerson(item.priorityLevel)">{{ $func.conditionStr(nodeConfig, index) }}</div>
                <div class="error_tip" v-if="(isTried || item.localTried) && item.error">
                    <i class="anticon anticon-exclamation-circle"></i>
                </div>
              </div>
              <addNode v-model:childNodeP="item.childNode" />
            </div>
          </div>
          <nodeWrap v-if="item.childNode" v-model:nodeConfig="item.childNode" />
          <template v-if="index == 0">
            <div class="top-left-cover-line"></div>
            <div class="bottom-left-cover-line"></div>
          </template>
          <template v-if="index == nodeConfig.conditionNodes.length - 1">
            <div class="top-right-cover-line"></div>
            <div class="bottom-right-cover-line"></div>
          </template>
        </div>
      </div>
      <addNode v-model:childNodeP="nodeConfig.childNode" />
    </div>
  </div>
  <nodeWrap v-if="nodeConfig.childNode" v-model:nodeConfig="nodeConfig.childNode"/>

  <el-drawer
    v-model="jsonDrawerVisible"
    :append-to-body="true"
    title="节点配置"
    class="workflow-designer-drawer"
    :show-close="false"
    :size="550"
  >
    <div class="demo-drawer__content">
      <div class="drawer_content" style="padding: 20px;">
        <el-input v-model="jsonDrawerText" type="textarea" :rows="18" />
      </div>
      <div class="demo-drawer__footer clear" style="padding: 0 20px 20px;">
        <el-button type="primary" @click="saveJsonDrawer">确 定</el-button>
        <el-button @click="closeJsonDrawer">取 消</el-button>
      </div>
    </div>
  </el-drawer>
</template>
<script setup>
import { onMounted, ref, watch, getCurrentInstance, computed } from "vue";
import { ElButton, ElDrawer, ElInput, ElMessage } from "element-plus";
import $func from "../utils/index.js";
import { useStore } from '../stores/index.js'
import { bgColors, placeholderList, NodeType, getNodeTypeMeta } from '../utils/const.js'
import addNode from './addNode.vue'
const store = useStore();
let _uid = getCurrentInstance().uid;

/** 标题进入编辑态后聚焦输入框，替代来源应用的全局指令依赖。 */
const vFocus = {
    mounted(element) {
        element.focus()
    },
}

let props = defineProps({
    nodeConfig: {
        type: Object,
        default: () => ({}),
    },
    flowPermission: {
        type: Array,
        default: () => [],
    },
});

/**
 * 是否为审批类节点（使用审批人抽屉配置）
 */
const normalizeType = (type) => {
    const num = Number(type)
    return Number.isNaN(num) ? type : num
}

const isApproverLike = (type) => {
    const normalizedType = normalizeType(type)
    return [
        NodeType.Approver,
        NodeType.GroupApprover,
        NodeType.Handler,
        NodeType.Vote,
        NodeType.Rob,
        NodeType.CrossTenantApprover,
    ].includes(normalizedType)
}

/**
 * 是否为路由节点（条件/并行/包容等）
 */
const isRouteType = (type) => {
    const normalizedType = normalizeType(type)
    return [
        NodeType.RouteLegacy,
        NodeType.Route,
        NodeType.ParallelRoute,
        NodeType.InclusiveRoute,
    ].includes(normalizedType)
}

const isDynamicRouteType = (type) => {
    return normalizeType(type) === NodeType.DynamicRoute
}

/**
 * 计算修改数据节点错误信息
 * @param {any} cfg 节点配置
 * @returns {{ error: boolean; tip: string; placeHolder: string }} 错误结果
 */
const calcModifyDataError = (cfg) => {
    const conditionItem = cfg?.conditionConfig
    let conditionSummary = ''
    if (conditionItem) {
        conditionSummary = $func.conditionStr({ type: NodeType.Route, conditionNodes: [conditionItem] }, 0)
        if (conditionSummary === '请设置条件' || conditionSummary === '其他条件进入此流程') conditionSummary = ''
    }

    const rows = Array.isArray(cfg?.modifyList) ? cfg.modifyList : []
    const isEmptyValue = (val) => !String(val ?? '').trim()

    if (!conditionSummary) {
        return { error: true, tip: '请选择执行条件', placeHolder: '请配置数据写入' }
    }
    if (!rows.length) {
        return { error: true, tip: '请配置数据写入', placeHolder: '请配置数据写入' }
    }
    const seen = new Set()
    for (let i = 0; i < rows.length; i += 1) {
        const row = rows[i] || {}
        const key = String(row.fieldKey || '').trim()
        if (!key) return { error: true, tip: `第${i + 1}行：请选择表单字段`, placeHolder: '请配置数据写入' }
        if (seen.has(key)) return { error: true, tip: `第${i + 1}行：表单字段重复`, placeHolder: '请配置数据写入' }
        seen.add(key)
        if (row.value === undefined || row.value === null || isEmptyValue(row.value)) {
            return { error: true, tip: `第${i + 1}行：请输入值`, placeHolder: '请配置数据写入' }
        }
    }

    const fieldSummary = rows.map((r) => String(r?.fieldKey || '').trim()).filter((x) => !!x).join('、')
    const placeHolder = fieldSummary ? `修改字段：${fieldSummary}` : '已配置'
    return { error: false, tip: '', placeHolder }
}

/**
 * 计算删除数据节点错误信息
 * @param {any} cfg 节点配置
 * @returns {{ error: boolean; tip: string; placeHolder: string }} 错误结果
 */
const calcDeleteDataError = (cfg) => {
    const conditionItem = cfg?.conditionConfig
    let conditionSummary = ''
    if (conditionItem) {
        conditionSummary = $func.conditionStr({ type: NodeType.Route, conditionNodes: [conditionItem] }, 0)
        if (conditionSummary === '请设置条件' || conditionSummary === '其他条件进入此流程') conditionSummary = ''
    }

    const rows = Array.isArray(cfg?.deleteList) ? cfg.deleteList : []

    if (!conditionSummary) {
        return { error: true, tip: '请选择执行条件', placeHolder: '请配置数据删除' }
    }
    if (!rows.length) {
        return { error: true, tip: '请配置数据删除', placeHolder: '请配置数据删除' }
    }
    const seen = new Set()
    for (let i = 0; i < rows.length; i += 1) {
        const row = rows[i] || {}
        const key = String(row.fieldKey || '').trim()
        if (!key) return { error: true, tip: `第${i + 1}行：请选择表单字段`, placeHolder: '请配置数据删除' }
        if (seen.has(key)) return { error: true, tip: `第${i + 1}行：表单字段重复`, placeHolder: '请配置数据删除' }
        seen.add(key)
    }

    const fieldSummary = rows.map((r) => String(r?.fieldKey || '').trim()).filter((x) => !!x).join('、')
    const placeHolder = fieldSummary ? `删除字段：${fieldSummary}` : '已配置'
    return { error: false, tip: '', placeHolder }
}

const isParallelOrInclusive = (type) => {
    const normalizedType = normalizeType(type)
    return [NodeType.ParallelRoute, NodeType.InclusiveRoute].includes(normalizedType)
}

/**
 * 判断是否为延时器节点（兼容缺失 type 的配置数据）
 * @param {any} cfg 节点配置
 */
const isDelayLike = (cfg) => {
    const normalizedType = normalizeType(cfg?.type)
    if (normalizedType === NodeType.Delay) return true
    return (
        cfg &&
        (cfg.delayUnit !== undefined || cfg.timePointSource !== undefined || cfg.durationValue !== undefined)
    )
}

/**
 * 规范化延时器模式
 * 业务规则变更：兼容 mode 为 0 或 "0" 表示固定时长
 * @param {any} mode 节点模式
 * @returns {boolean} 是否为固定时间点
 */
const resolveDelayMode = (mode) => {
    return !(mode === false || mode === 0 || mode === '0')
}

/**
 * 判断时间点字符串是否合法
 * @param {any} value 待判断值
 * @returns {boolean} 是否为时间点字符串
 */
const isDateLike = (value) => {
    if (typeof value !== 'string') return false
    const text = value.trim()
    if (!text) return false
    return /^\d{4}-\d{2}-\d{2}/.test(text)
}

/**
 * 获取时间点值
 * 业务规则变更：时间点仅接受日期格式字符串，避免固定时长数值误判
 * @param {any} cfg 节点配置
 * @param {string} timePointSource 时间点来源
 * @returns {string} 时间点值
 */
const getTimePointValue = (cfg, timePointSource) => {
    if (timePointSource !== 'input') return ''
    const directValue = String(cfg?.timePointValue || '').trim()
    if (isDateLike(directValue)) return directValue
    const legacyValue = cfg?.value
    return isDateLike(legacyValue) ? String(legacyValue).trim() : ''
}

/**
 * 获取时间点表单字段
 * @param {any} cfg 节点配置
 * @param {string} timePointSource 时间点来源
 * @returns {string} 表单字段 Key
 */
const getTimePointFormKey = (cfg, timePointSource) => {
    if (timePointSource !== 'form') return ''
    const directKey = String(cfg?.timePointFormKey || '').trim()
    if (directKey) return directKey
    return String(cfg?.formFieldKey || '').trim()
}

/**
 * 获取固定时长数值
 * @param {any} cfg 节点配置
 * @returns {number} 固定时长
 */
const getDurationValue = (cfg) => {
    const rawDuration = cfg?.durationValue ?? cfg?.value
    if (isDateLike(rawDuration)) return 0
    const num = Number(rawDuration)
    return Number.isFinite(num) ? num : 0
}

/**
 * 节点元信息
 */
let nodeTypeMeta = computed(() => getNodeTypeMeta(props.nodeConfig.type))

/**
 * 是否渲染为卡片节点
 */
let isCardNode = computed(() => {
    if (!props.nodeConfig) return false
    if (normalizeType(props.nodeConfig.type) === NodeType.ConditionItem) return false
    if (isRouteType(props.nodeConfig.type)) return false
    return true
})

/**
 * 是否为分支节点
 */
let isRouteNode = computed(() => {
    if (!props.nodeConfig) return false
    return isRouteType(props.nodeConfig.type)
})

let defaultText = computed(() => {
    const normalizedType = normalizeType(props.nodeConfig.type)
    if (normalizedType < 3) return placeholderList[normalizedType]
    return nodeTypeMeta.value.label
});
let showText = computed(() => {
    // 业务规则变更：空节点作为占位节点展示固定说明文案，不需要配置
    if (normalizeType(props.nodeConfig.type) == NodeType.Empty) return '默认执行通过'
    if (props.nodeConfig.placeHolder) return props.nodeConfig.placeHolder
    if (normalizeType(props.nodeConfig.type) == NodeType.Starter) return $func.arrToStr(props.flowPermission) || '所有人'
    if (isApproverLike(props.nodeConfig.type)) return $func.setApproverStr(props.nodeConfig)
    if (normalizeType(props.nodeConfig.type) == NodeType.Copyer) return $func.copyerStr(props.nodeConfig)
    return ''
});

let isInputList = ref([]);
let isInput = ref(false);
const getConditionPrefix = () => {
    const normalizedType = normalizeType(props.nodeConfig?.type)
    if (normalizedType === NodeType.ParallelRoute) return '并行条件'
    if (normalizedType === NodeType.InclusiveRoute) return '包容条件'
    return '条件'
}
const getConditionDefaultName = (index) => {
    const normalizedType = normalizeType(props.nodeConfig?.type)
    if (normalizedType === NodeType.ParallelRoute) return `并行条件${index + 1}`
    if (normalizedType === NodeType.InclusiveRoute) return `包容条件${index + 1}`
    return '条件'
}
const resetConditionNodesErr = () => {
    for (var i = 0; i < props.nodeConfig.conditionNodes.length; i++) {
        props.nodeConfig.conditionNodes[i].error = $func.conditionStr(props.nodeConfig, i) == "请设置条件";
    }
}
const calcApproverListenerError = (cfg) => {
    const listeners = cfg?.listeners || []
    for (const l of listeners) {
        if (!l?.enable) continue
        if (!String(l.url || '').trim()) return `${l.name}：请求地址不能为空`
        if (l?.errorHandling?.defaultAction === 4 && !String(l?.errorHandling?.defaultJumpNodeId || '').trim()) {
            return `${l.name}：默认异常处理跳转节点ID不能为空`
        }
        for (const row of (l?.errorHandling?.codeActions || [])) {
            if (!String(row.code || '').trim()) continue
            if (!/^-?\d+$/.test(String(row.code).trim())) return `${l.name}：code必须是数字`
            if (row.action === 4 && !String(row.jumpNodeId || '').trim()) return `${l.name}：code跳转节点ID不能为空`
        }
    }
    return ''
}

/**
 * 计算子流程节点错误信息
 * 业务规则新增：子流程必须配置子流程、多实例来源，且禁止死循环子流程
 * @param {any} cfg 节点配置
 * @returns {{ error: boolean; tip: string; placeHolder: string }} 错误结果
 */
const calcSubProcessError = (cfg) => {
    const subFlowKey = String(cfg?.subFlowKey || '').trim()
    if (!subFlowKey) {
        return { error: true, tip: '请选择子流程', placeHolder: '请配置子流程' }
    }
    if (store.currentFlowId && String(store.currentFlowId) === subFlowKey) {
        return { error: true, tip: '子流程不能选择当前流程', placeHolder: '请配置子流程' }
    }
    if (Array.isArray(cfg?.forbiddenSubFlowKeys) && cfg.forbiddenSubFlowKeys.includes(subFlowKey)) {
        return { error: true, tip: '子流程会形成死循环，禁止选择', placeHolder: '请配置子流程' }
    }
    if (cfg?.multiInstanceEnabled) {
        const rate = Number(cfg?.completionRate)
        if (!Number.isFinite(rate) || rate < 1 || rate > 100) {
            return { error: true, tip: '完成比例需在1-100之间', placeHolder: `子流程：${cfg?.subFlowName || subFlowKey}` }
        }
        const sourceType = String(cfg?.multiInstanceSourceType || 'fixed')
        if (sourceType === 'fixed') {
            const count = Number(cfg?.multiInstanceCount)
            if (!Number.isFinite(count) || count < 1) {
                return { error: true, tip: '固定数量必须为正整数', placeHolder: `子流程：${cfg?.subFlowName || subFlowKey}` }
            }
        } else if (sourceType === 'numberField') {
            if (!String(cfg?.multiInstanceNumberFieldKey || '').trim()) {
                return { error: true, tip: '请选择数字表单字段', placeHolder: `子流程：${cfg?.subFlowName || subFlowKey}` }
            }
        } else if (sourceType === 'multiField') {
            if (!String(cfg?.multiInstanceMultiFieldKey || '').trim()) {
                return { error: true, tip: '请选择多项表单字段', placeHolder: `子流程：${cfg?.subFlowName || subFlowKey}` }
            }
        } else if (sourceType === 'role') {
            const roleList = Array.isArray(cfg?.multiInstanceRoleList) ? cfg.multiInstanceRoleList : []
            if (roleList.length === 0) {
                return { error: true, tip: '请选择角色', placeHolder: `子流程：${cfg?.subFlowName || subFlowKey}` }
            }
        }
    }
    if (cfg?.timeoutEnabled) {
        const timeoutValue = Number(cfg?.timeoutValue)
        if (!Number.isFinite(timeoutValue) || timeoutValue <= 0) {
            return { error: true, tip: '超时时间必须为正数', placeHolder: `子流程：${cfg?.subFlowName || subFlowKey}` }
        }
        if (cfg?.timeoutAction === 'jump' && !String(cfg?.timeoutJumpNodeId || '').trim()) {
            return { error: true, tip: '请选择超时跳转节点', placeHolder: `子流程：${cfg?.subFlowName || subFlowKey}` }
        }
    }
    return { error: false, tip: '', placeHolder: `子流程：${cfg?.subFlowName || subFlowKey}` }
}

const calcApproverRequiredError = (cfg) => {
    const nodeUserList = Array.isArray(cfg?.nodeUserList) ? cfg.nodeUserList : []
    const emptyNodeUserList = Array.isArray(cfg?.emptyNodeUserList) ? cfg.emptyNodeUserList : []
    const nodeType = Number(cfg?.type)
    const isHandler = nodeType === NodeType.Handler
    const isRob = nodeType === NodeType.Rob
    const isVote = nodeType === NodeType.Vote
    const settype = Number(cfg?.settype)

    if (cfg?.noHanderAction === 3 && emptyNodeUserList.length === 0) {
        if (isRob) return '抢单人为空时请选择指定人员'
        if (isHandler) return '办理人为空时请选择指定人员'
        return '审批人为空时请选择指定人员'
    }

    if (isRob) {
        const robSetType = String(cfg?.robSetType || 'member').trim() || 'member'
        if (['member', 'role', 'post', 'dept'].includes(robSetType)) {
            if (nodeUserList.length === 0) {
                if (robSetType === 'role') return '请添加/修改角色'
                if (robSetType === 'post') return '请添加/修改岗位'
                if (robSetType === 'dept') return '请添加/修改部门'
                return '请添加/修改成员'
            }
        } else if (robSetType === 'formUser') {
            if (!String(cfg?.formUserFieldKey || '').trim()) return '请输入表单字段Key'
        } else if (robSetType === 'formDept') {
            if (!String(cfg?.formDeptFieldKey || '').trim()) return '请输入表单字段Key'
        } else if (robSetType === 'syncNode') {
            if (!String(cfg?.syncNodeId || '').trim()) return '请选择节点'
        }
        return ''
    }

    if (nodeType === NodeType.GroupApprover) {
        const groupList = Array.isArray(cfg?.groupList) ? cfg.groupList : []
        if (groupList.length < 2) return '请配置至少2组审批人'
        for (const group of groupList) {
            if (!String(group?.name || '').trim()) return '请填写分组名称'
            const groupSetType = String(group?.setType || group?.settype || 'member')
            const groupUsers = Array.isArray(group?.nodeUserList) ? group.nodeUserList : []
            if (['member', 'role', 'post', 'dept'].includes(groupSetType)) {
                if (groupUsers.length === 0) return '请为分组选择成员'
                if (groupUsers.length > 1) {
                    const groupExamineMode = Number(group?.examineMode)
                    if (![1, 2, 3, 4].includes(groupExamineMode)) return '请选择分组审批方式'
                    if (groupExamineMode === 2) {
                        const groupRate = Number(group?.countersignPassRate)
                        if (Number.isNaN(groupRate) || groupRate < 1 || groupRate > 100) return '会签通过比例需在1-100之间'
                    }
                }
            } else if (groupSetType === 'formUser') {
                if (!String(group?.formUserFieldKey || '').trim()) return '请输入表单字段Key'
            } else if (groupSetType === 'formDept') {
                if (!String(group?.formDeptFieldKey || '').trim()) return '请输入表单字段Key'
            } else if (groupSetType === 'syncNode') {
                if (!String(group?.syncNodeId || '').trim()) return '请选择节点'
            }
        }
        const passCount = Number(cfg?.groupPassCount)
        if (!Number.isInteger(passCount) || passCount < 1 || passCount > groupList.length) return '审批通过的组数必须在1到当前组数之间'
    } else {
        if (settype === 1) {
            if (nodeUserList.length === 0) return '请添加/修改成员'
        } else if (settype === 2) {
            const level = Number(cfg?.directorLevel)
            if (!Number.isInteger(level) || level < 1) return '请选择主管级别'
            if (cfg?.examineMode === 2) return '主管不支持会签'
        } else if (settype === 3) {
            if (nodeUserList.length === 0) return '请添加/修改角色'
        } else if (settype === 4) {
            const selectMode = Number(cfg?.selectMode)
            const selectRange = Number(cfg?.selectRange)
            if (![1, 2].includes(selectMode)) return '请选择自选模式'
            if (![1, 2, 3].includes(selectRange)) return '请选择选择范围'
            if (selectRange === 2 && nodeUserList.length === 0) return '请选择可选成员'
            if (selectRange === 3 && nodeUserList.length === 0) return '请选择可选角色'
        } else if (settype === 6) {
            if (nodeUserList.length === 0) return '请添加/修改岗位'
        } else if (settype === 7) {
            const endLevel = Number(cfg?.examineEndDirectorLevel)
            if (!Number.isInteger(endLevel) || endLevel < 1) return '请选择审批终点层级'
        } else if (settype === 8) {
            if (nodeUserList.length === 0) return '请添加/修改部门'
        } else if (settype === 9) {
            if (!String(cfg?.formUserFieldKey || '').trim()) return '请输入表单字段Key'
        } else {
            return `请选择${defaultText.value}`
        }

        const needExamineMode =
            settype === 2 ||
            settype === 3 ||
            (settype === 1 && nodeUserList.length > 1) ||
            (settype === 4 && Number(cfg?.selectMode) === 2)
        if (needExamineMode) {
            const examineMode = Number(cfg?.examineMode)
            if (![1, 2, 3, 4].includes(examineMode)) return '请选择多人审批方式'
        }
        if (Number(cfg?.examineMode) === 2) {
            const rate = Number(cfg?.countersignPassRate)
            if (Number.isNaN(rate) || rate < 1 || rate > 100) return '会签通过比例需在1-100之间'
        }
    }

    if (cfg?.rejectAction === 2 && !String(cfg?.rejectToNodeId || '').trim()) return '请输入驳回节点ID'
    if (isVote) {
        return 'Full.NET 不允许脚本型投票节点'
    }
    if (cfg?.enableResultCallback && !String(cfg?.resultCallbackFieldKey || '').trim()) return '请选择回传表单字段'
    if (cfg?.timeLimitEnabled) {
        const hours = Number(cfg?.timeLimitHours)
        if (!Number.isInteger(hours) || hours < 1) return '请设置审批时限'
        const timeLimitAction = Number(cfg?.timeLimitAction ?? 1)
        if (timeLimitAction === 1) {
            const remindMode = Number(cfg?.timeLimitRemindMode ?? 1)
            if (remindMode === 1) {
                const remindCount = Number(cfg?.timeLimitRemindCount)
                const remindInterval = Number(cfg?.timeLimitRemindInterval)
                if (!Number.isInteger(remindCount) || remindCount < 1) return '请设置提醒次数'
                if (!Number.isInteger(remindInterval) || remindInterval < 1) return '请设置提醒间隔'
            }
        }
    }

    return ''
}

const flowNodeConfig1 = computed(() => store.flowNodeConfig)

const buildNodeContext = (root, currentId) => {
    const nodes = []
    let currentStack = []
    const walk = (node, stack) => {
        if (!node) return
        const normalized = normalizeType(node.type)
        if (node.id) {
            nodes.push({
                id: node.id,
                label: node.nodeName || getNodeTypeMeta(normalized).label,
                type: normalized,
                stack: [...stack],
            })
            if (node.id === currentId) currentStack = [...stack]
        }
        if (isRouteType(normalized)) {
            const conditionNodes = Array.isArray(node.conditionNodes) ? node.conditionNodes : []
            conditionNodes.forEach((conditionNode) => {
                const nextStack = [...stack, { routeId: node.id, routeType: normalized, conditionId: conditionNode.id }]
                if (conditionNode.childNode) walk(conditionNode.childNode, nextStack)
            })
            if (node.childNode) walk(node.childNode, stack)
            return
        }
        if (node.childNode) walk(node.childNode, stack)
    }
    walk(root, [])
    return { nodes, currentStack }
}

const isPrefixStack = (source, target) => {
    if (source.length > target.length) return false
    for (let i = 0; i < source.length; i += 1) {
        if (source[i].routeId !== target[i].routeId || source[i].conditionId !== target[i].conditionId) return false
    }
    return true
}

const canJumpTo = (currentStack, targetStack) => {
    if (!currentStack.length) return targetStack.length === 0
    const lastParallelIndex = currentStack.map((item, idx) => isParallelOrInclusive(item.routeType) ? idx : -1).filter((v) => v >= 0).pop()
    if (lastParallelIndex !== undefined) {
        if (targetStack.length <= lastParallelIndex) return false
        const currentKey = currentStack[lastParallelIndex]
        const targetKey = targetStack[lastParallelIndex]
        return targetKey && currentKey.routeId === targetKey.routeId && currentKey.conditionId === targetKey.conditionId
    }
    if (targetStack.length === 0) return true
    if (isPrefixStack(targetStack, currentStack)) return true
    if (isPrefixStack(currentStack, targetStack)) return true
    return false
}

const getDynamicRouteSummary = (route) => {
    const summary = $func.conditionStr({ type: NodeType.Route, conditionNodes: [route] }, 0)
    if (summary === '请设置条件' || summary === '其他条件进入此流程') return ''
    return summary
}

const calcDynamicRouteError = (cfg) => {
    const routes = Array.isArray(cfg?.routeList) ? cfg.routeList : []
    if (!routes.length) {
        return { error: true, tip: '请配置动态路由', placeHolder: '请配置动态路由' }
    }
    const { nodes, currentStack } = buildNodeContext(flowNodeConfig1.value, cfg?.id)
    const allowedIds = nodes
        .filter((node) => node.id !== cfg?.id)
        .filter((node) => node.type !== NodeType.DynamicRoute && node.type !== NodeType.ConditionItem)
        .filter((node) => canJumpTo(currentStack, node.stack || []))
        .map((node) => node.id)
    const allowedIdSet = new Set(allowedIds)
    for (const route of routes) {
        if (!route?.targetNodeId || !allowedIdSet.has(route.targetNodeId)) {
            return { error: true, tip: '请选择可跳转节点', placeHolder: '请配置动态路由' }
        }
        if (!getDynamicRouteSummary(route)) {
            return { error: true, tip: '请选择执行条件', placeHolder: '请配置动态路由' }
        }
    }
    return { error: false, tip: '', placeHolder: `已配置${routes.length}条路由` }
}

const refreshNodeError = () => {
    const cfg = props.nodeConfig || {}
    let nextError = false
    let nextTip = ''

    if (isApproverLike(cfg.type)) {
        const requiredTip = calcApproverRequiredError(cfg)
        const listenerTip = calcApproverListenerError(cfg)
        nextTip = listenerTip || requiredTip
        nextError = !!nextTip
    } else if (cfg.type == NodeType.Copyer) {
        const ok = !!$func.copyerStr(cfg)
        nextError = !ok
        nextTip = ok ? '' : `请选择${defaultText.value}`
    } else if ([NodeType.Trigger, NodeType.AsyncTrigger].includes(cfg.type)) {
        const url = String(cfg.url || '').trim()
        const ok = !!(url && /^https?:\/\/.+/i.test(url))
        nextError = !ok
        nextTip = url ? '请求地址格式不正确' : '请设置请求地址'
        if (cfg.placeHolder !== nextTip) cfg.placeHolder = nextTip
    } else if (isDelayLike(cfg)) {
        // 业务规则变更：当节点配置缺失 type，但含延时字段时仍视为延时器
        const mode = resolveDelayMode(cfg.mode)
        const timePointSource = cfg.timePointSource === 'form' ? 'form' : 'input'
        const timePointValue = getTimePointValue(cfg, timePointSource)
        const timePointFormKey = getTimePointFormKey(cfg, timePointSource)
        const durationValue = getDurationValue(cfg)
        const delayUnit = cfg.delayUnit || (mode ? 'TS' : 'S')
        const unitLabelMap = {
            S: '秒',
            M: '分钟',
            H: '小时',
            D: '天',
        }
        const unitLabel = unitLabelMap[delayUnit] || ''
        if (mode) {
            if (timePointSource === 'input') {
                nextError = !String(timePointValue || '').trim()
                nextTip = nextError ? '请设置时间点' : ''
                if (cfg.placeHolder !== (nextError ? '请配置延时器' : `固定时间点：${timePointValue}`)) {
                    cfg.placeHolder = nextError ? '请配置延时器' : `固定时间点：${timePointValue}`
                }
            } else {
                nextError = !String(timePointFormKey || '').trim()
                nextTip = nextError ? '请选择表单字段' : ''
                if (cfg.placeHolder !== (nextError ? '请配置延时器' : `固定时间点：${timePointFormKey}`)) {
                    cfg.placeHolder = nextError ? '请配置延时器' : `固定时间点：${timePointFormKey}`
                }
            }
        } else {
            nextError = !(durationValue > 0)
            nextTip = nextError ? '请设置延时时长' : ''
            const summary = nextError ? '请配置延时器' : `固定时长：${durationValue}${unitLabel}`
            if (cfg.placeHolder !== summary) cfg.placeHolder = summary
        }
    } else if (isDynamicRouteType(cfg.type)) {
        const { error, tip, placeHolder } = calcDynamicRouteError(cfg)
        nextError = error
        nextTip = tip
        if (cfg.placeHolder !== placeHolder) cfg.placeHolder = placeHolder
    } else if (normalizeType(cfg.type) === NodeType.SubProcess) {
        const { error, tip, placeHolder } = calcSubProcessError(cfg)
        nextError = error
        nextTip = tip
        if (cfg.placeHolder !== placeHolder) cfg.placeHolder = placeHolder
    } else if (normalizeType(cfg.type) === NodeType.ModifyData) {
        const { error, tip, placeHolder } = calcModifyDataError(cfg)
        nextError = error
        nextTip = tip
        if (cfg.placeHolder !== placeHolder) cfg.placeHolder = placeHolder
    } else if (normalizeType(cfg.type) === NodeType.DeleteData) {
        const { error, tip, placeHolder } = calcDeleteDataError(cfg)
        nextError = error
        nextTip = tip
        if (cfg.placeHolder !== placeHolder) cfg.placeHolder = placeHolder
    } else if (isRouteType(cfg.type)) {
        resetConditionNodesErr()
        return
    }

    if (cfg.error !== nextError) cfg.error = nextError
    if (cfg.errorTip !== nextTip) cfg.errorTip = nextTip
}

onMounted(() => {
    refreshNodeError()
})

watch(
    () => props.nodeConfig,
    () => {
        refreshNodeError()
    },
    { deep: true }
)
let emits = defineEmits(["update:flowPermission", "update:nodeConfig"]);
let {
    setPromoter,
    setApprover,
    setCopyer,
    setTrigger,
    setDelay,
    setCondition,
    setDynamicRoute,
    setModifyData,
    setFlowPermission,
    setApproverConfig,
    setCopyerConfig,
    setTriggerConfig,
    setDelayConfig,
    setDynamicRouteConfig,
    setConditionsConfig,
    setModifyDataConfig,
    setDeleteData,
    setDeleteDataConfig,
    setSubProcess,
    setSubProcessConfig,
} = store;
let isTried = computed(()=> store.isTried)
let flowPermission1 = computed(()=> store.flowPermission1)
let approverConfig1 = computed(()=> store.approverConfig1)
let copyerConfig1 = computed(()=> store.copyerConfig1)
let triggerConfig1 = computed(()=> store.triggerConfig1)
let delayConfig1 = computed(()=> store.delayConfig1)
let conditionsConfig1 = computed(()=> store.conditionsConfig1)
let dynamicRouteConfig1 = computed(()=> store.dynamicRouteConfig1)
let modifyDataConfig1 = computed(()=> store.modifyDataConfig1)
let deleteDataConfig1 = computed(()=> store.deleteDataConfig1)
watch(flowPermission1, (flow) => {
    if (flow.flag && flow.id === _uid) {
        emits("update:flowPermission", flow.value);
    }
});
watch(approverConfig1, (approver) => {
    if (approver.flag && approver.id === _uid) {
        emits("update:nodeConfig", approver.value);
    }
});
watch(copyerConfig1, (copyer) => {
    if (copyer.flag && copyer.id === _uid) {
        emits("update:nodeConfig", copyer.value);
    }
});
watch(triggerConfig1, (trigger) => {
    if (trigger.flag && trigger.id === _uid) {
        emits("update:nodeConfig", trigger.value);
    }
});
watch(delayConfig1, (delay) => {
    if (delay.flag && delay.id === _uid) {
        emits("update:nodeConfig", delay.value);
    }
});
watch(conditionsConfig1, (condition) => {
    if (condition.flag && condition.id === _uid) {
        emits("update:nodeConfig", condition.value);
    }
});
watch(dynamicRouteConfig1, (dynamicRoute) => {
    if (dynamicRoute.flag && dynamicRoute.id === _uid) {
        emits("update:nodeConfig", dynamicRoute.value);
    }
});
watch(modifyDataConfig1, (modifyData) => {
    if (modifyData.flag && modifyData.id === _uid) {
        emits("update:nodeConfig", modifyData.value);
    }
});
watch(deleteDataConfig1, (deleteData) => {
    if (deleteData.flag && deleteData.id === _uid) {
        emits("update:nodeConfig", deleteData.value);
    }
});

const clickEvent = (index) => {
    if (index || index === 0) {
        isInputList.value[index] = true;
    } else {
        isInput.value = true;
    }
};
const blurEvent = (index) => {
    if (index || index === 0) {
        isInputList.value[index] = false;
        props.nodeConfig.conditionNodes[index].nodeName = props.nodeConfig.conditionNodes[index].nodeName || getConditionDefaultName(index);
    } else {
        isInput.value = false;
        props.nodeConfig.nodeName = props.nodeConfig.nodeName || defaultText
    }
};
const delNode = () => {
    emits("update:nodeConfig", props.nodeConfig.childNode);
};
const addTerm = () => {
    let len = props.nodeConfig.conditionNodes.length + 1;
    props.nodeConfig.conditionNodes.push({
        nodeName: `${getConditionPrefix()}${len}`,
        type: 3,
        priorityLevel: len,
        conditionType: 'static',
        conditionGroupMode: 'fixed',
        conditionList: [],
        nodeUserList: [],
        conditionGroupType: 'and',
        conditionGroupExpression: '',
        relationSystem: 'user',
        conditionGroupList: [{
            conditionList: [],
            nodeUserList: [],
        }],
        childNode: null,
    });
    resetConditionNodesErr()
    emits("update:nodeConfig", props.nodeConfig);
};
const genNodeId = () => {
    if (typeof globalThis.crypto?.randomUUID === 'function') return globalThis.crypto.randomUUID()
    return `node-${Date.now()}-${_uid}`
}
const resetNodeIds = (node) => {
    if (!node) return
    node.id = genNodeId()
    if (Array.isArray(node.conditionNodes)) {
        node.conditionNodes.forEach((item) => {
            resetNodeIds(item)
        })
    }
    if (node.childNode) resetNodeIds(node.childNode)
}
const copyTerm = (index) => {
    const source = props.nodeConfig.conditionNodes[index]
    if (!source) return
    const cloned = JSON.parse(JSON.stringify(source))
    resetNodeIds(cloned)
    props.nodeConfig.conditionNodes.splice(index + 1, 0, cloned)
    const prefix = getConditionPrefix()
    props.nodeConfig.conditionNodes.map((item, idx) => {
        item.priorityLevel = idx + 1
        item.nodeName = `${prefix}${idx + 1}`
    })
    resetConditionNodesErr()
    emits("update:nodeConfig", props.nodeConfig);
}
const delTerm = (index) => {
    props.nodeConfig.conditionNodes.splice(index, 1);
    const prefix = getConditionPrefix()
    props.nodeConfig.conditionNodes.map((item, index) => {
        item.priorityLevel = index + 1;
        item.nodeName = `${prefix}${index + 1}`;
    });
    resetConditionNodesErr()
    emits("update:nodeConfig", props.nodeConfig);
    if (props.nodeConfig.conditionNodes.length == 1) {
        if (props.nodeConfig.childNode) {
            if (props.nodeConfig.conditionNodes[0].childNode) {
                reData(props.nodeConfig.conditionNodes[0].childNode, props.nodeConfig.childNode);
            } else {
                props.nodeConfig.conditionNodes[0].childNode = props.nodeConfig.childNode;
            }
        }
        emits("update:nodeConfig", props.nodeConfig.conditionNodes[0].childNode);
    }
};
const reData = (data, addData) => {
    if (!data.childNode) {
        data.childNode = addData;
    } else {
        reData(data.childNode, addData);
    }
};
const setPerson = (priorityLevel) => {
    const normalizedType = normalizeType(props.nodeConfig?.type)
    if (normalizedType == NodeType.Starter) {
        setPromoter(true);
        setFlowPermission({
            value: props.flowPermission,
            flag: false,
            id: _uid,
        });
    } else if (isApproverLike(normalizedType)) {
        setApprover(true);
        setApproverConfig({
            value: {
                ...JSON.parse(JSON.stringify(props.nodeConfig)),
                ...{ settype: props.nodeConfig.settype ? props.nodeConfig.settype : 1 },
            },
            flag: false,
            id: _uid,
        });
    } else if (normalizedType == NodeType.Copyer) {
        setCopyer(true);
        setCopyerConfig({
            value: JSON.parse(JSON.stringify(props.nodeConfig)),
            flag: false,
            id: _uid,
        });
    } else if ([NodeType.Trigger, NodeType.AsyncTrigger].includes(normalizedType)) {
        setTrigger(true);
        setTriggerConfig({
            value: JSON.parse(JSON.stringify(props.nodeConfig)),
            flag: false,
            id: _uid,
        });
    } else if (isDelayLike(props.nodeConfig)) {
        // 业务规则变更：当节点配置缺失 type，但含延时字段时仍视为延时器
        setDelay(true);
        setDelayConfig({
            value: JSON.parse(JSON.stringify(props.nodeConfig)),
            flag: false,
            id: _uid,
        });
    } else if (normalizedType === NodeType.DynamicRoute) {
        setDynamicRoute(true)
        setDynamicRouteConfig({
            value: JSON.parse(JSON.stringify(props.nodeConfig)),
            flag: false,
            id: _uid,
        })
    } else if (normalizedType === NodeType.ModifyData) {
        setModifyData(true)
        setModifyDataConfig({
            value: JSON.parse(JSON.stringify(props.nodeConfig)),
            flag: false,
            id: _uid,
        })
    } else if (normalizedType === NodeType.DeleteData) {
        setDeleteData(true)
        setDeleteDataConfig({
            value: JSON.parse(JSON.stringify(props.nodeConfig)),
            flag: false,
            id: _uid,
        })
    } else if (normalizedType === NodeType.SubProcess) {
        setSubProcess(true)
        setSubProcessConfig({
            value: JSON.parse(JSON.stringify(props.nodeConfig)),
            flag: false,
            id: _uid,
        })
    } else if (isRouteType(normalizedType) || normalizedType == NodeType.ConditionItem) {
        if (normalizedType === NodeType.ParallelRoute && priorityLevel) return
        setCondition(true);
        setConditionsConfig({
            value: JSON.parse(JSON.stringify(props.nodeConfig)),
            priorityLevel,
            flag: false,
            id: _uid,
        });
    } else if (normalizedType === NodeType.Empty) {
        ElMessage.info('空节点无需配置')
    } else {
        openJsonDrawer()
    }
};
const arrTransfer = (index, type = 1) => {
    //向左-1,向右1
    props.nodeConfig.conditionNodes[index] = props.nodeConfig.conditionNodes.splice(
        index + type,
        1,
        props.nodeConfig.conditionNodes[index]
    )[0];
    props.nodeConfig.conditionNodes.map((item, index) => {
        item.priorityLevel = index + 1;
    });
    resetConditionNodesErr()
    emits("update:nodeConfig", props.nodeConfig);
};

let jsonDrawerVisible = ref(false)
let jsonDrawerText = ref('')

/**
 * 打开 JSON 配置抽屉
 */
const openJsonDrawer = () => {
    jsonDrawerText.value = JSON.stringify(props.nodeConfig || {}, null, 2)
    jsonDrawerVisible.value = true
}

/**
 * 关闭 JSON 配置抽屉
 */
const closeJsonDrawer = () => {
    jsonDrawerVisible.value = false
}

/**
 * 保存 JSON 配置抽屉内容
 */
const saveJsonDrawer = () => {
    try {
        const parsed = JSON.parse(jsonDrawerText.value || '{}')
        emits("update:nodeConfig", parsed)
        jsonDrawerVisible.value = false
    } catch (e) {
        return
    }
}
</script>
<style>
.error_tip {
    position: absolute;
    top: 0px;
    right: 0px;
    transform: translate(150%, 0px);
    font-size: 24px;
}

.copy-branch {
    margin-left: 8px;
    font-size: 12px;
    color: #409eff;
    cursor: pointer;
}

.promoter_person .el-dialog__body {
    padding: 10px 20px 14px 20px;
}

.selected_list {
    margin-bottom: 20px;
    line-height: 30px;
}

.selected_list span {
    margin-right: 10px;
    padding: 3px 6px 3px 9px;
    line-height: 12px;
    white-space: nowrap;
    border-radius: 2px;
    border: 1px solid rgba(220, 220, 220, 1);
}

.selected_list img {
    margin-left: 5px;
    width: 7px;
    height: 7px;
    cursor: pointer;
}
</style>
