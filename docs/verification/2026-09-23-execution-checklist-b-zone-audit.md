# 执行清单 B 区（19–47）源码审计（2026-09-23）

**目的**：全量排期 Phase B 的串行槽位以 **核验 + 补证据** 为主；下列「已有」指 Host.Api + `ui/admin` 可见实现，**不等同 Verified**。

| 编号 | 判定 | 主要锚点 |
|------|------|----------|
| 19 | 已有 | `identityCopyHostRole`、`RolesView` copy |
| 20 | 已有 | enable/disable/delete、members API + `RolesView` |
| 21 | 已有 | `tenancy.tenants.enable`、租户生命周期 Endpoint |
| 22 | 已有 | `Tenancy/Features/TenantBranding` |
| 24 | 部分 | Identity 用户 Excel 已有；职位/机构批量见清单子切片 |
| 25 | 待核 | Settings 枚举→字典 UI |
| 26–27 | 部分 | Auditing 列表已有；聚合/导出待逐页核验 |
| 28 | 部分 | Observability 健康/日志；硬件监控页待核 |
| 29 | 已有 | `ObservabilityAdmin` CachePolicy 控制面 |
| 30 | 已有 | `identityRevokeAllHostUserOnlineSessions` |
| 31–32 | 部分 | Files Host CRUD；目录/批量待核 |
| 33–34 | 部分 | Jobs 定义/计划；运行取消/批量待核 |
| 35 | 已有 | `Full.NET.Modules.Calendar`、`PersonalSchedulesView` |
| 36 | 待核 | CodeGen catalog column-sync / 增量合并 |
| 37 | 已有 | `Full.NET.Modules.Platform` ReleaseNote |
| 38 | 已有 | `Full.NET.Modules.Regions` |
| 39–40 | 已有 | `ManageOpenAccessClients` + 观测服务 |
| 41 | 部分 | Overview 摘要；趋势聚合待核 |
| 42 | 部分 | 公告 Host 发布；收到/统计待核 |
| 43 | 部分 | Notifications Intent 附件协调器；SMTP 端到端待核 |
| 44–45 | 缺失/外部 | 回执验签、短信 Provider |
| 46 | 部分 | DataApproval 单场景；第二场景待业务选定 |
| 47 | 已有 | `rollback` Host 文档版本 Endpoint |

**Phase B 串行纪律**：仍按计划 B-1…B-28 顺序，每编号一次 **closeout 提交**（测试/文档/OpenAPI），无源码缺口则仅更新 verification，不重复实现。
