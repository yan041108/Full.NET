# F16 预设发布验收清单

> 状态：企业预设收口批次（2026-09-17）已附证据；见 [closeout](2026-09-17-enterprise-preset-closeout.md)。F16-R2 证据汇总与已知限制见本文末节（2026-11-15）；其中受影响原生路径已由计划 §8.229 的 linux-x64 Native AOT 实测补齐。

## 基础

- [x] `diagnose --profile production` 无 error — 仓库根 `dotnet exec … diagnose --workspace .` 为 warn（非应用模板）；应用模板场景见 `create-first-crud.md`
- [x] SQL Server 与 MySQL 迁移成对应用 — 221–227 成对 SQL；集成断言覆盖 SqlServer/MySql 测试类
- [x] Minimal / Platform / Full 预设模块闭包与 `FullNetModuleSelection` 一致 — `foundation-preset-release.test.mjs` 通过

## 功能抽样

- [x] 租户订阅 API（Tenancy ManageTenantSubscriptions）— `TenantSubscriptionAssertions` + `TenancyApiSqlServerTests`
- [x] Webhook 订阅 API（Webhooks ManageWebhookSubscriptions）— `WebhookDeliveryAssertions` + 单元投递测
- [x] Enterprise Request 样例 Schema 测试通过 — `samples/enterprise-request/tests/schema.test.mjs`
- [x] Host 在企业管理中维护订阅。旧订阅地址检查当前 Host 上下文和订阅读取权限后转入企业管理。

## 部署

- [x] Helm 三角色 overlay 单角色启用 — `helm v3.16.4` 对 `deploy/helm/fullnet` 用 `ci/values-role-{api,worker,migrator}.yaml` 各执行 `helm lint` 与 `helm template`，三次均 exit 0；渲染资源按角色互斥：api 仅 `-api` Deployment/Service/Ingress/HPA/PDB，worker 仅 `-worker` Deployment/HPA/PDB，migrator 仅 `-migrator-1` Job，共享 ServiceAccount/ConfigMap/NetworkPolicy（渲染证据 `%TEMP%\fullnet-f16-r1-logs\helm\role-*.yaml`）；仅本地渲染校验，未部署到真实集群
- [x] `tests/deployment/foundation-preset-release.test.mjs` 通过
- [x] Native AOT 发布路径已验证 — `db43d7af6` 上四个宿主均退出 0 并产出合格 ELF64 x86-64；服务级冒烟用原生 Migrator 在隔离库执行 389 条脚本、原生 Api 监听并返回健康端点、原生 Worker 启动并轮询（见下文与计划 §8.229）；单机单副本，未做 Kubernetes 发布与容量验证；该轮附带仅编排参数与宿主选择的运行器补丁

## 文档

- [x] `docs/operations/application-upgrade.md` — 已存在
- [x] `docs/operations/application-recovery.md` — 已存在
- [x] `docs/development/create-first-crud.md` 实走记录 — 见下文 2026-09-17 记录

## SaaS 子集（2026-09-17）

> 证据：[saas-preset-closeout](2026-09-17-saas-preset-closeout.md)

- [x] `FullNet:Modules:Preset=Saas` 闭包含 Payments + Webhooks（`SaasPresetModuleNames` + 模板 `saas`）
- [x] F12 运营 MVP：Host 创建试用/取消订阅 + `TenantPackageId` 联动（`TenantSubscriptionAssertions`；Docker 集成未跑）
- [x] F07 SaaS 切片：Preset=Saas 默认 Enforced 引导 + Enforced 下无套餐/订阅 reactivate 门禁
- [x] 管理端 Host 订阅/权益阶段可操作或可读（`TenantsView` / `TenantPackagesView`）
- [x] `ITenantSubscriptionPaymentFulfillmentPort` 占位（无真实渠道验收）

## 未验证声明

- 容量与 SLO：保持 `Capacity-not-verified` 直至有等价生产证据
- Docker API 集成分片：本地未跑；依赖 CI Testcontainers
- F12 真实支付 Provider / 回调对账：明确排除于本批次

## create-first-crud 实走记录（2026-09-17）

```text
命令：dotnet exec src/Tools/Full.NET.CodeGeneration.Cli/bin/Release/net10.0/Full.NET.CodeGeneration.Cli.dll diagnose --workspace .
结果：exit 0；DIAG_WORKSPACE_OK / DIAG_SDK_OK；连接串与 FullNet:Modules 为占位 warn（符合仓库根诊断预期）
备注：Enterprise 预设与 F09–F11 子集见 `docs/verification/2026-09-17-enterprise-business-integration-closeout.md`；Reporting/Printing 全量集成未验。
```

## F16-R2 证据汇总与已知限制（2026-11-15）

> 只汇总已取得的新鲜实跑证据与未验证项，不把源码计数、夹具或旧通过记录外推为发布通过。

### 有效证据（按固定候选）

| 事项 | 固定输入 | 实测结论 | 边界 |
| --- | --- | --- | --- |
| F15-R1 源码分发 | `1688b8029`，清单 SHA-256 `e82a6200…b4835` | .NET SDK 10.0.402 实际创建 Minimal / SQL Server 应用，布局与冻结客户端生成检查通过 | 仅源码分发与安装 |
| F15-R2 源码升级与迁移中断 | `76fa073c…`，清单 SHA-256 `5d627f31…90cb`；实包升级 `4c744e87…`，清单 `3d786698…7d16` | 1181 个受管文件更新、5 个退役文件按字节移除；377 记账中断后重跑 1 条、再跑 0 条；普通账号读回一致、越权 403、匿名 401 | Minimal 未装配 Files/Reporting |
| F15-R2 报表排空与下载 | `a8262810…`，清单 SHA-256 `e03685b3…c5aa` | 旧 API 停准入后门禁 SQL 归零，旧 Worker 排空后任务完成；定义级/版本级授权下载 200/403/401 符合预期，文件 1735 字节逐字节一致 | 本机独立进程，非真实 Kubernetes |
| F15-R3 Enterprise 联合恢复 | `db43d7af6`，清单 SHA-256 `79345da7…4c5a` | 演练 `f15-r3-20261115t042720`：RPO 222ms、RTO 120.415s；领取中投递受控确认、故障后到期投递仅外发一次；撤销重放、JWT/DP 密钥恢复通过 | 单租户、单订阅、两条投递、SQL Server |
| F15-R3 Enterprise / MySQL 联合恢复 | `db43d7af6`，清单 SHA-256 `79345da7…4c5a` | 演练 `f15-r3-mysql-20261115t081325`：RPO 1.434s、RTO 119.854s；337 条迁移/273 表、逻辑一致性备份恢复、Migrator 重跑 0 条；原文件哈希一致、备份后文件 404；领取中投递仅外发一次；五类回执校验退出 0 | 单租户、单订阅、两条投递、MySQL 8.4；未覆盖 SSL、binlog 时间点恢复与主从切换 |
| F16-R1 Minimal 联合恢复与接管 | `db43d7af6`，清单 SHA-256 `79345da7…4c5a` | 演练 `f16-r1-20261115t073721`：RPO 449ms、RTO 13.921s（目标 30/1800s）；RESTORE 成功、库 ONLINE、DBCC 通过、Migrator 重跑 0 条；故障前记录按原 ID/时间可读、备份后记录 404；双 API/Worker 接管成功；五类回执经 `Verify-FullNetRecoveryEvidence.mjs` 校验 | 单机隔离容器、Minimal 预设、SQL Server |
| F16-R1 受影响原生路径（Native AOT） | `db43d7af6` 候选源码 + 运行器补丁（`run-api-aot-publish-linux.mjs` SHA-256 最终 `A8CB4611…22DB`） | 四宿主均 exit 0：API 178,229,072、Worker 111,722,184、Migrator 110,527,864、LogConsumer 12,797,688 字节；均 ELF64 LSB x86-64（`e_machine 0x3E`），警告门禁 9/9/8/1 条允许项，`publish-manifest.json` 记录 SDK 10.0.404。服务级冒烟（隔离 SQL Server/Redis）：原生 Migrator 执行 389 条脚本退出 0（journal 397、346 表、`SchemaMode=Binary16`）；原生 Api 监听 8080，`/health/live`、`/health/startup` 200 Healthy，`/health/ready` 200 Degraded（Backplane 无成功证据）；原生 Worker 启动并轮询 | 候选脚本未把 `buildBudget` 应用到 `docker run` 且 `--host` 只接受 api/worker，原样运行在 restore 阶段反复 `MSB4166`；单机单副本，未做 Kubernetes 发布与容量验证 |

F16-R1 程序集 SHA-256：Migrator `cc159cbe…c38e4`、Api `e79840d4…e6c5`、Worker `8af722ab…d5ac`。证据目录 `%TEMP%\fullnet-f16-r1-db43d7af6\runs\f16-r1-20261115t073721`（`result.json`、`milestones.json`、`commands.json`、`cleanup.json`、`progress.ndjson`、`logs/`、`evidence/`），清理记录为容器已删除、进程已停止、`failed=false`。六个失败轮次（`t073253`—`t073557`）与 `t073631` 的派生出错均完整保留，属驱动缺陷，未用于通过结论。

### 未覆盖项与分别判定

| 项目 | 当前状态 | 判定 |
| --- | --- | --- |
| 容量与 SLO | 未实测 | `Capacity-not-verified`，不使用单机演练数字推导生产容量 |
| 真实 Kubernetes 停稳/排空 | 未执行 | 归 F15-R2，需现网 SSH 与发布窗口授权；缺授权不以夹具冒充。Helm 单角色 overlay 仅做过本地 `helm template`/`lint` 渲染校验，未部署到真实集群 |
| 受影响原生路径（AOT/Native 发布） | 四宿主发布与服务级冒烟通过；单机单副本，未做 Kubernetes 发布与容量 | 发布与启动链路已取证，集群级与容量单独判定 |
| MySQL SSL、主从切换与 binlog 时间点恢复 | 未演练 | 本机联合恢复已由 §8.228 覆盖；上述生产级能力单独判定 |
| 外部渠道：真实 163/SMTP、支付商户回调对账 | 缺受控凭据 | 归 F03-R1 / F12-R1，明确排除于本候选 |
| 生产启用（真实域名、证书、外部网络） | 未执行 | 单独判定，不由隔离容器证据代表 |
| 未选模块（Jobs/Messaging/Notifications/Webhooks 等） | 未装配或未演练 | 不阻塞发布候选，按实际预设闭包判定 |

本清单仅汇总到固定候选 `db43d7af6051f294a7507ba75e1bcef56fe535f1` 的已取得证据；任何未列出的组合、渠道或环境均保持未验证。
