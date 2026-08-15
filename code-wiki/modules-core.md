# 核心业务模块详解

## 一、Tenancy 多租户模块

> 项目：[`src/Modules/Full.NET.Modules.Tenancy`](file:///G:/wwwroot/github_fork/Full.NET/src/Modules/Full.NET.Modules.Tenancy)
> 稳定模块键：`Tenancy`

### 1.1 职责

- 租户开通（Provisioning）：创建租户记录 + 幂等播种 + 发送 `TenantProvisionedIntegrationEvent`
- 租户解析：域名 / 请求 Header / 认证 Claim → 可信 TenantId
- 租户切换：授权用户在其可用租户间切换（写审计 + 刷新会话）
- 租户包（Tenant Package）：功能套餐、配额、资源包
- 缓存失效：租户变更事件 → 清除所有关联缓存

### 1.2 核心类

| 类 | 职责 |
|----|------|
| `Tenant` (Domain) | 租户聚合根：TenantId, Name, Identifier(域名/短名), Status, PackageId |
| `TenantResolver` | 解析链：Host Header → Claim → DefaultLocalTenant |
| `TenantResolutionMiddleware` | 管道中间件：早于认证解析租户（匿名也能解析域名租户） |
| `TenantProvisioningService` | 开通编排：验证标识 → 事务写 Tenant → 播种 Seed Profile → Outbox |
| `HostTenantManagementService` | 宿主管理员 CRUD 租户 |
| `HostTenantPackageManagementService` | 租户包管理 |
| `TenantCacheInvalidator` | 本地缓存失效处理器（Outbox Event Handler） |
| `TenantContextSummary` | 契约：当前租户摘要（Id, Name, Locale, TimeZone） |
| `TenantChangedIntegrationEvent` | 租户变更集成事件（V1 + SchemaVersion） |
| `TenantProvisionedIntegrationEvent` | 开通完成集成事件 |

### 1.3 解析流程

```
HTTP Request
  → TrustedProxyMiddleware (规范化 X-Forwarded)
  → TenantResolutionMiddleware (解析顺序)
      │
      ├── 1. 显式切换 Claim（ChangeTenantContext 写的）
      ├── 2. 认证 Identity Claim 中的 tid
      ├── 3. Host Header 域名匹配（Identifier 字段）
      └── 4. 默认 development 本地租户
  → CurrentTenantAccessor.Push(tenant)  AsyncLocal
  → 后续：SqlScopeGuard 校验 + 缓存键注入 + Outbox 携带
```

---

## 二、Organization 组织架构模块

> 项目：[`src/Modules/Full.NET.Modules.Organization`](file:///G:/wwwroot/github_fork/Full.NET/src/Modules/Full.NET.Modules.Organization)
> 稳定模块键：`Organization`

### 2.1 职责

- 组织单元（部门）：树形层级结构、类型、排序
- 岗位：名称、职级、编制
- 职级序列：P1~P10、M1~M5 等
- 用户组织归属：主部门、兼职部门、岗位分配
- 数据范围：部门树 → Identity 的 DataScope 过滤

### 2.2 核心表

| 表 | 说明 |
|----|------|
| `fn_organization_unit` | 组织单元：Id, TenantId, ParentId, Name, UnitType, Path(物化路径), Sort |
| `fn_organization_position` | 岗位：Id, TenantId, UnitId, PositionName, LevelId |
| `fn_org_position_level` | 职级：Id, TenantId, LevelCode, LevelName, Rank |
| `fn_org_user_unit` | 用户部门：UserId, UnitId, IsPrimary(主部门) |
| `fn_org_user_position` | 用户岗位：UserId, PositionId, StartDate, EndDate |

### 2.3 发布的集成事件

| 事件 | 消费方 |
|------|--------|
| `OrganizationUnitChangedIntegrationEvent` | Identity → 本地投影用于导航/选择目录 |

---

## 三、Settings 配置管理模块

> 项目：[`src/Modules/Full.NET.Modules.Settings`](file:///G:/wwwroot/github_fork/Full.NET/src/Modules/Full.NET.Modules.Settings)
> 稳定模块键：`Settings`

### 3.1 子功能

| 子功能 | 说明 | 硬删除前置条件 |
|--------|------|----------------|
| **Config Entry** | 参数配置项（Host/Tenant 双作用域）、分组、排序、值类型、内置标记 | 必须为"已禁用"状态 |
| **Dictionary** | 字典类型 + 字典项（Host/Tenant 双作用域），支持按编码查询 | 字典类型必须无启用项；字典项必须禁用 |
| **Enum Catalog** | C# 枚举 → 前端下拉目录（只读同步） | N/A |
| **Grid Preferences** | 用户网格列偏好、排序、筛选保存 | N/A |

### 3.2 Config Entry 契约

| 字段 | 说明 |
|------|------|
| ConfigCode | 配置编码（创建可编辑，修改只读） |
| ConfigName | 配置名称（必填） |
| ValueType | String / Integer / Boolean / Decimal / Json（创建可选，修改只读） |
| PropertyValue | 属性值（根据 ValueType 校验） |
| GroupName | 分组 |
| IsBuiltIn | 内置参数（是/否） |
| Sort / Remark / Status | 排序、备注、启用状态 |

### 3.3 批量 API

```
POST /api/v1/settings/config-entries/batch-delete   # 批量删除（仅禁用项）
PUT  /api/v1/settings/config-entries/batch-value     # 批量更新属性值
```

---

## 四、Auditing 审计模块

> 项目：[`src/Modules/Full.NET.Modules.Auditing`](file:///G:/wwwroot/github_fork/Full.NET/src/Modules/Full.NET.Modules.Auditing)
> 稳定模块键：`Auditing`

### 4.1 日志类型

| 类型 | 触发点 | 可靠性等级 | 保留策略 |
|------|--------|------------|----------|
| `fn_auditing_operation_log` | `OperationLogMiddleware` 自动记录 HTTP 操作 | B0 (业务事实，同事务) | 默认 180 天 |
| `fn_auditing_access_log` | 认证/授权阶段（登录、登出、Refresh、租户切换） | B0 | 默认 90 天 |
| `fn_auditing_exception_log` | `ExceptionLogMiddleware` 未处理异常捕获 | B1 | 默认 90 天 |
| `fn_auditing_outbound_call_log` | 出站 HTTP/gRPC 调用（耗时、状态码） | B2 (可丢失) | 默认 30 天 |

### 4.2 写入管道

```
业务写 / 中间件捕获
  → AuditWriteBuffer (批处理缓冲)
  → 按可靠性分类：
     ├── B0：同事务写入（CommandTransaction 附加）
     ├── B1：异步有界队列（后台 Worker）
     └── B2：Fire-and-Forget（可丢弃）
```

### 4.3 查询特性

- 游标分页（Cursor Pagination）：避免深分页性能问题
- 时间范围包含策略：`ContainsTimeBoundary` 防止边界丢日志
- 数据保留 Runner：后台任务按 `AuditingRetentionOptions` 定期清理过期记录

---

## 五、Jobs 任务调度模块

> 项目：[`src/Modules/Full.NET.Modules.Jobs`](file:///G:/wwwroot/github_fork/Full.NET/src/Modules/Full.NET.Modules.Jobs)
> 稳定模块键：`Jobs`

### 5.1 概念模型

```
HostJobDefinition（任务定义）
  ├── JobKey       稳定任务键（唯一）
  ├── DisplayName  显示名称
  ├── Description  描述
  ├── Status       启用/禁用
  ├── HandlerType  任务处理器 Assembly Qualified Name
  └── ParametersJson  默认参数
       │
       └── HostJobSchedule（调度计划，1:N）
              ├── CronExpression  Cron 表达式
              ├── StartAtUtc / EndAtUtc  有效区间
              ├── ParametersJson  本次覆盖参数
              ├── TriggerCount  触发次数
              └── ErrorCount    出错次数（>0 红色 Tag）
                   │
                   └── HostJobExecution（执行记录，1:N）
                          ├── RunAtUtc  实际执行时间
                          ├── DurationMs  耗时
                          ├── Status  Success / Failed / Cancelled
                          ├── OutputMessage  输出（截断）
                          └── ErrorDetailJson  异常详情
```

### 5.2 管理 API 权限

| 操作 | 权限码 | 动态显示条件 |
|------|--------|--------------|
| 查看执行记录 | `jobs.executions.read` | 始终 |
| 立即执行 | `jobs.definitions.trigger` | 仅启用状态 |
| 编辑 | `jobs.definitions.update` | 仅启用状态 |
| 禁用 | `jobs.definitions.disable` | 仅启用状态 |

---

## 六、Files 文件模块

> 项目：[`src/Modules/Full.NET.Modules.Files`](file:///G:/wwwroot/github_fork/Full.NET/src/Modules/Full.NET.Modules.Files)
> 稳定模块键：`Files`

### 6.1 核心概念

- **Host File**：宿主级文件（用户上传到平台），记录元数据 + Blob 路径
- **Reference Claim**：文件引用声明。业务模块在事务内写入 Claim 表示"我正在使用这个文件"；无 Claim 的文件在软删除后由清理器物理回收
- **上传流程**：前端 → 后端流式写入存储（S3/本地/SMB） → 事务写 HostFile 记录 + Outbox → 返回 FileId

### 6.2 Blob 清理

```
DeletedHostFileBlobCleanupRunner（后台 Worker）
  1. 查出 IsDeleted=true 的文件
  2. 检查是否仍有 ReferenceClaim（事务对账）
  3. 无 Claim 且超过宽限期 → 物理删除 Blob
  4. 审计清理记录
```

---

## 七、Document 文档模块

> 项目：[`src/Modules/Full.NET.Modules.Document`](file:///G:/wwwroot/github_fork/Full.NET/src/Modules/Full.NET.Modules.Document)
> 稳定模块键：`Document`

### 7.1 子功能

| 子功能 | 说明 |
|--------|------|
| **Document Item** | 文档项：文件/文件夹、父级、所有者、扩展属性 |
| **Category** | 文档分类：Code、Icon、Color、Description、UseCount |
| **Tag** | 文档标签：字段同 Category，多对多关联 |
| **Permissions** | 细粒度权限：按用户/角色设置读/写/删除/分享/管理 |
| **Shares** | 分享链接：ShareCode(随机 16 进制)、有效期、访问次数 |
| **Statistics** | 文档统计：数量、大小、类型分布、访问趋势 |
| **Recycle Bin** | 回收站：软删除文档、恢复、永久删除 |

### 7.2 分享安全

- ShareCode：`RandomNumberGenerator.GetHexString(8)` 安全随机
- **不支持分享密码**（密码请求返回 400）
- ShareResponse 中 Password 字段 `[JsonIgnore]`，永不返回到客户端

---

## 八、Messaging 消息模块

> 项目：[`src/Modules/Full.NET.Modules.Messaging`](file:///G:/wwwroot/github_fork/Full.NET/src/Modules/Full.NET.Modules.Messaging)
> 稳定模块键：`Messaging`

### 8.1 职责

- 事件流所有权注册与切换：LegacyPolling ↔ ShadowCdcKafka ↔ CdcKafka
- 死信（DLQ）查询与人工重放
- Kafka 范围重放运维 API
- Inbox 积压监控
- CDC 启用/禁用脚本（SQL Server CDC / MySQL Binlog 验证）

### 8.2 所有权切换 CAS

```
切换请求: StreamId, TargetOwner
  1. 读取 EventStreamOwnershipRecord: { CurrentOwner, PreviousOwner, Version }
  2. 检查目标积压（DueRetryCount / ActiveLeaseCount）只看目标流，其他流不阻塞
  3. SQL UPDATE ... 
     WHERE StreamId = @StreamId AND PreviousOwner = @ExpectedPreviousOwner
     （原子 Compare-And-Swap）
  4. 受影响行数 = 0 → 并发冲突异常
  5. 成功后：
     - 旧 Worker 立即停止领取该流
     - CDC Relay 开始（或停止）捕获该流追加 INSERT
     - Kafka Consumer Group 开始（或停止）消费
```

---

## 九、CodeGeneration 代码生成模块

> 项目：[`src/Modules/Full.NET.Modules.CodeGeneration`](file:///G:/wwwroot/github_fork/Full.NET/src/Modules/Full.NET.Modules.CodeGeneration)
> 稳定模块键：`CodeGeneration`

### 9.1 生命周期

```
模板管理
  → 预览（基于 Schema 元数据生成代码片段预览，不写文件）
  → 执行：
      1. 创建 GenerationManifest + Checkpoint
      2. 声明所有权（Path + Hash → 对目标文件 claim → 无覆盖 rename）
      3. 写入产物 + 更新墓碑
      4. 提交 Manifest（最后）
  → 回滚链（按 Checkpoint 逆向恢复）：
      1. 每个 Checkpoint 记录写入前的原始状态
      2. 回滚 = 恢复墓碑 → 删除新文件 → restore 原始
```

### 9.2 Git 集成

- 执行前：确保工作区干净或在专用分支
- 执行后：自动 commit + push 到临时分支 → 创建 PR（可选）
- 远程 Git 操作：专用 `ICodeGenerationGitCommandRunner` 抽象

---

## 十、SerialNumbers 流水号模块

> 项目：[`src/Modules/Full.NET.Modules.SerialNumbers`](file:///G:/wwwroot/github_fork/Full.NET/src/Modules/Full.NET.Modules.SerialNumbers)
> 稳定模块键：`SerialNumbers`

### 10.1 规则引擎

```text
规则表达式示例：
  PO-{yyyyMMdd}-{seq:6,reset:daily}
  INV-{branch:2}-{year:2}-{seq:4,reset:yearly}
```

### 10.2 并发安全

- 使用 `sp_getapplock` (SQL Server) / `GET_LOCK` (MySQL) 按规则键加命名锁
- 或使用原子 `UPDATE ... SET CurrentValue = CurrentValue + 1 OUTPUT` 分配区间
- 分配器按批量预取（Chunk Size = 10~100），内存内部分配减少数据库压力
