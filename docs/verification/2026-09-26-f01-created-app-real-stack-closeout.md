# 底座首轮收口：创建应用、升级安全与代表性样例

本轮按现有[底座完善总计划](../superpowers/plans/2026-09-16-foundation-productization.md)执行，覆盖应用创建、源码升级安全、已有模块装配及开发分支验收入口。该报告不关闭整个 F01、F15、F16 或 W1—W5。

## 基线与交付

- 开工基线：`120d767ca3e70b68f95923fefcb0ad6378bcd0fb`，工作区干净。
- 实现提交：`2c10a5d2db90f2d1598e8397d719067c8568775b`。
- 开发分支：`codex/foundation-acceptance-20260926`；[草稿 PR #2](https://github.com/yan041108/Full.NET/pull/2)。未合并、未发布。
- 远端 main 为 `633ede05c3dc4962e96cf8e2429988075eda1e62`，PR 包含开工基线之前已存在的未发布增量；不能把全部 PR 差异归为本轮新增。
- 下列 Actions 关联实现提交，默认检出 PR 合并引用 `8b32d1c689dc0ea39de97ac1cfb178366ec80d22`。这是 CI 验证引用，不表示实际合并。

本轮实现已通过约定门禁；主 CI、API Native、Worker Native 均为成功终态。本报告将验证结果绑定以上实现提交；后续文档补充仅同步验收事实。

## 实际变化

1. 框架升级校验配对清单、全文摘要、路径和种子注册清单；固定应用预设与迁移闭包。预览后发生变化时拒绝写入；通过同卷暂存、排他锁、旧框架与清单备份恢复失败，保留人工定制与恢复材料。
2. Minimal 应用保留 Identity、Tenancy、Settings、Organization，区分官方契约名称与所选实现。头像、租户 Logo、文件配额在缺 Files 时仍能完成装配，相关操作在副作用前受控拒绝，不伪造零用量；认证入口补齐请求上下文依赖，保留宿主覆盖和重复注册幂等。
3. 历史共享迁移 203/206/207 只在固定全文摘要下按预设清单适配，所选缺表或未选已有表均拒绝；093 的 SQL Server 回填使用固定语句延迟编译。已发布 SQL 资源保持不变。MFA 238 补齐双库部分完成后的重入恢复。
4. 修复租户履约服务作用域与三条 Workflow 写路径的跨模块读取顺序，权益读取位于本地事务前，未扩大架构豁免。
5. Development 首次播种在 local 租户创建后登记管理员成员；Production 仍只允许 Baseline。生成客户端将规范整数字符串转为安全整数，超出范围拒绝。企业申请单创建传递可信组织上下文，由服务端确定主体和审计字段。
6. 统一显式 Baseline 播种验收清单，Development/Test 只追加各自贡献者；精确校验全部审计条目与 Succeeded 状态，新增 Production 仅 Baseline 的审计断言。
7. PR 固定执行独立应用双库真实 CRUD、企业样例双库 API/浏览器及受影响 Integration。迁移恢复分为两个互补双库组，保留原 `build-test` 名称作为强制汇总；迁移失败、取消、跳过或缺失不能被模块成功掩盖。

对应实现与验收入口：

- [升级工具](../../scripts/templates/upgrade-framework.mjs)、[升级安全回归](../../tests/templates/framework-upgrade-safety.test.mjs)。
- [打包应用验收](../../tests/templates/packaged-app.test.mjs)、[独立应用真实栈](../../tests/templates/created-app-real-stack.test.mjs)。
- [CI 工作流](../../.github/workflows/ci.yml)、[执行组与影响集](../../scripts/testing/run-affected-integration.mjs)、[汇总结果检查](../../scripts/testing/verify-ci-job-results.mjs)。

## 验证证据

| 范围 | 实际入口与结果 | 证据 |
| --- | --- | --- |
| 模板、四个预设与独立应用 | `pnpm test:templates`：70 通过，0 失败，0 跳过；Minimal/Platform/SaaS/Enterprise 独立 API 构建与所选实现闭包验证；Minimal Vue 构建；新建 Minimal 应用在双库执行登录、字典创建/读取/版本更新/禁用/删除/404 | [模板作业](https://github.com/yan041108/Full.NET/actions/runs/36238340370/job/108394130378) |
| 企业样例真实浏览器 | 两库各 2 通过、无跳过；保留 Host 页面样例，新增租户企业申请单创建/修改/删除 | [SQL Server](https://github.com/yan041108/Full.NET/actions/runs/36238340370/job/108394130392)、[MySQL](https://github.com/yan041108/Full.NET/actions/runs/36238340370/job/108394130446) |
| 客户端与治理 | 作业成功；Vue 单测 853、OpenAPI 182、uni-app 单测 144、H5 浏览器 7 通过；Vue Mock 浏览器 55 通过、4 跳过（仅 Layui 壳层适用用例）。含构建、依赖审计、许可清单、契约兼容与包体积门禁 | [客户端作业](https://github.com/yan041108/Full.NET/actions/runs/36238340370/job/108394130411) |
| Unit、Compatibility、Architecture、模块与 Smoke | 作业成功；Unit 3038、Compatibility 12、Architecture 230、受影响双库模块/Smoke 295、企业样例 API 6 项全部通过，无跳过。含 Development 首次/重跑、Test Overlay 和 Production Baseline 精确审计验证 | [模块作业](https://github.com/yan041108/Full.NET/actions/runs/36238340370/job/108394130425) |
| 双库历史迁移恢复 | 历史/当前两组分别 250/244 通过，合计 494，0 失败、0 跳过；包含 093 双库旧数据恢复及 238 MFA 部分完成后重入。真实 UID 发现证明无遗漏或重叠 | [历史组](https://github.com/yan041108/Full.NET/actions/runs/36238340370/job/108394130412)、[当前组](https://github.com/yan041108/Full.NET/actions/runs/36238340370/job/108394130407) |
| API Linux Native AOT | 作业成功；Linux 原生发布、Native 架构 73 项、基础 API 9、Notifications 2、Settings Jobs 4、OIDC 16、S3 2、Kafka Replay 2 项通过。基础广筛选额外跳过 16 个 Worker 用例，由 Worker 独立作业验证 | [API Native](https://github.com/yan041108/Full.NET/actions/runs/36238340372) |
| Worker Linux Native AOT | 作业成功；Linux 原生发布、Native 架构 73 项与双库原生进程 E2E 16 项通过，无跳过 | [Worker Native](https://github.com/yan041108/Full.NET/actions/runs/36238340377) |

主 CI 的测试工具 62 项及治理 55 项通过，无跳过；保留的 [build-test 汇总门禁](https://github.com/yan041108/Full.NET/actions/runs/36238340370/job/108399950655)成功。

实际入口：`pnpm test:templates`、`pnpm test:dotnet:unit`、`pnpm test:dotnet:compatibility`、`pnpm test:dotnet:architecture`；受影响 Integration 在 PR 中使用 `pnpm test:integration:affected -- --base <PR_BASE_SHA> --phase merge --execution-group <group>`，分别执行 `modules`、`migrations-legacy`、`migrations-current`。后置企业 API 使用 `FullyQualifiedName~EnterpriseRequestApi` 筛选；API/Worker 的发布与原生套件入口见对应工作流。以上重型验证由上述 Actions 实际执行。

本地快速增量验证：相关 Unit 30 项、无数据库迁移兼容 22 项、聚焦测试工具 52 项、治理 55 项、SQL 安全 5 项通过；API AOT analyzers 为 0 警告/0 错误。干净实现提交的源码包测试 8 项通过、无跳过。回归先复现了可选 Files 装配、请求上下文注册、093 回填及 CI 汇总结果缺口，再验证修复。

Native 发布沿用 ADR-0008 已登记的第三方警告门禁，API/Worker 发布分别记录 17/15 条允许警告；本轮未新增豁免。分析器零警告不等于发布完全无警告。

完整 Integration 编译发现 1075 项（173 + 173 + 494 + 178 + 57），规范分片与迁移执行组经过真实 UID 无遗漏/重复检查。发现数不等于全量执行通过数，PR 未执行的 main 专属 Integration 全量分片及完整真实栈 E2E 不能报告为通过。

本地 Windows 完整 Unit 曾因一个 Linux FIFO 场景跳过而被严格包装器判失败，不作为完整单测通过证据。当前 Linux 完整 Unit 已执行 3038 项全部通过、无跳过。

## 限制与下一切片

- 四个预设的独立构建不代表四个预设全部真实栈、全部业务流程或独立应用 Native 发布认证；本轮独立应用真实双库覆盖 Minimal 字典生命周期。
- `dotnet new fullnet-app` 直接安装、无网络/空包缓存创建、真实历史发行版混合运行，以及数据库/对象文件/密钥的完整灾难恢复仍未验收。
- 企业审批回写、完整注册恢复与邀请、真实邮件/支付渠道和总计划其余消费者继续按各原计划推进。
- Kubernetes 多实例、人工完整页面验收、生产等价故障恢复和容量未验证；保持 `Capacity-not-verified`，不提升 Production-verified。
- 下一切片按总计划 F02 检查生成器到应用业务 CRUD 的接入，复用当前创建与升级门禁；合并与发布仍按另行约定。
