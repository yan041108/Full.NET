# Workflow-Vue3 流程设计器

## 简介
基于 Vue3 开发的流程设计器，支持多种类型的节点配置，包括审批人、抄送人、条件分支、并行分支、包容分支、动态路由等。

## 目录结构
```
src/
  ├── api/                # API 接口
  ├── assets/             # 静态资源
  ├── components/         # 组件
  │   ├── dialog/         # 弹窗组件
  │   ├── drawer/         # 抽屉组件（属性配置）
  │   │   ├── approverDrawer.vue    # 审批人配置
  │   │   ├── conditionDrawer.vue   # 条件配置
  │   │   ├── copyerDrawer.vue      # 抄送人配置
  │   │   ├── delayDrawer.vue       # 延时器配置
  │   │   ├── dynamicRouteDrawer.vue # 动态路由配置
  │   │   ├── promoterDrawer.vue    # 发起人配置
  │   │   └── triggerDrawer.vue     # 触发器配置
  │   ├── addNode.vue     # 添加节点组件
  │   ├── nodeWrap.vue    # 节点渲染组件（递归）
  │   ├── selectBox.vue   # 选人弹窗
  │   └── selectResult.vue # 选人结果
  ├── stores/             # Pinia 状态管理
  ├── utils/              # 工具函数
  │   ├── const.js        # 常量定义（NodeType 枚举等）
  │   └── index.js        # 通用工具函数
  └── views/
      └── setting.vue     # 流程设计器主入口
```

## 核心功能

### 1. 节点类型 (NodeType)
在 `utils/const.js` 中定义了所有支持的节点类型：
- **发起人 (Starter)**: 流程的起始节点。
- **审批人 (Approver)**: 需要人工介入审批的节点。
- **抄送人 (Copyer)**: 只需要知晓流程进度的节点。
- **条件分支 (Route)**: 根据条件判断进入不同分支（互斥）。
- **并行分支 (ParallelRoute)**: 所有分支同时执行。
- **包容分支 (InclusiveRoute)**: 根据条件判断进入一个或多个分支。
- **动态路由 (DynamicRoute)**: 根据条件判断跳转到流程中的任意节点。
- **触发器 (Trigger)**: 调用外部接口。
- **延时器 (Delay)**: 等待一段时间或到达指定时间点。

### 2. 节点数据结构
节点树采用嵌套结构，每个节点包含以下核心字段：
```javascript
{
  id: "uuid",           // 节点唯一标识
  nodeName: "节点名称",  // 显示名称
  type: 1,              // 节点类型 (NodeType)
  childNode: { ... },   // 子节点（下一个节点）
  conditionNodes: [],   // 分支节点列表（仅路由类节点有）
  error: false,         // 是否配置错误
  // ...其他特定类型的配置字段
}
```

### 3. 动态路由 (DynamicRoute)
动态路由节点允许根据条件跳转到流程中的任意节点（需满足跳转规则）。
- **配置抽屉**: `dynamicRouteDrawer.vue`
- **跳转规则**:
  - 不能跳转到动态路由节点自身。
  - 不能跳转到条件分支内部节点（除非在同一分支内）。
  - 并行/包容分支内部节点不能跳转到外部节点。
  - 只能跳转到当前节点之前的节点（防止死循环，具体规则由 `canJumpTo` 函数控制）。

### 4. 分支命名规则
- **并行分支**: 默认命名为 "并行条件1", "并行条件2"...
- **包容分支**: 默认命名为 "包容条件1", "包容条件2"...
- **普通条件分支**: 默认命名为 "条件1", "条件2"...

## 开发指南

### 添加新节点类型
1. 在 `utils/const.js` 的 `NodeType` 中添加新类型枚举。
2. 在 `nodeTypeMetaMap` 中添加元数据（图标、背景色等）。
3. 在 `addNodeMenuGroups` 中添加菜单项。
4. 在 `addNode.vue` 的 `createNodeConfig` 中添加初始化配置逻辑。
5. 在 `nodeWrap.vue` 中添加渲染逻辑和点击事件处理。
6. 如果需要专用配置抽屉，创建新的 drawer 组件并在 `setting.vue` 中注册。
7. 在 `stores/index.js` 中添加相应的状态管理。

### 修改节点校验逻辑
节点校验逻辑主要在 `nodeWrap.vue` 的 `refreshNodeError` 函数和 `setting.vue` 的 `reErr` 函数中。
- `refreshNodeError`: 校验当前节点的配置是否合法，更新 `error` 状态。
- `reErr`: 递归校验整棵树，生成错误列表。

### 常用工具函数
- `$func.conditionStr`: 生成条件节点的摘要描述。
- `buildNodeContext`: 构建节点上下文，获取所有节点列表和当前节点的路径栈。
- `canJumpTo`: 判断是否可以从源节点跳转到目标节点。
