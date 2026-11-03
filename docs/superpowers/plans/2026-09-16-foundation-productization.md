# Full.NET 企业应用与 SaaS 底座完善开发计划

> 执行方式：按仓库 AGENTS.md 逐切片推进，不自动创建工作树、派发代理、提交或推送。本文件负责新增能力和跨专项依赖；已有专项的实现步骤与进度继续在原计划维护。

**Goal：** 将现有模块组合为可创建项目、可由企业自主使用、可运营 SaaS、可升级恢复的开发底座。

> **当前排期入口（2026-10-29）：** 使用本文 [§8 当前剩余工作队列](#8-当前剩余工作队列2026-10-29)。用户要求将快速开发底座缺口按优先级合并到本计划，已完成的不重复安排。本轮只更新计划；§5 的初始未勾选清单及后续过程记录保留为历史范围和证据，不再直接作为待开发清单。专项已交付内容按 §8.1 复用，进行中的专项仍由原计划维护；本次不据文档更新提升功能状态。

> **复审生效（2026-11-02）：** 用户要求重新审查并直接修正，采用本计划作为后续开发依据。当前任务顺序、依赖和完成出口以 §8（含 §8.14 首批执行基线）为准；§1 的既往授权/过程、§2 初始排序、§4 初始文件地图、§5 历史清单及 §7 历史运行记录只供追溯。发生冲突时执行当前项目规则与 §8，不从历史文字推导全量测试、CI 前置、兼容层或提交/发布授权。专项自己维护具体进度，本文只消费其实际已交付范围。

**Architecture：** 保持强化型模块化单体、API/Worker/Migrator 分离、Dapper 双库和 Host.Api Native AOT。复用 Identity、Tenancy、Organization、Payments 等数据所有者，不复制账号库，不为目录完整度增加业务项目。

**Tech Stack：** .NET 10、Dapper、自有事务/SQL 边界、SQL Server/MySQL、Vue、OpenAPI 客户端生成、Jobs、Files、Notifications、Outbox、Kubernetes/Helm；新增外部依赖必须在对应切片核对许可、维护状态与原生可行性。

## 1. 授权、状态与计划所有权

- 日期：2026-09-16；用户要求将八项补齐能力和已有模块收口重点写入文档并制定详细计划。
- 审查修订：补齐未注册受邀者入驻、存量权益迁移、F05/F08 无环依赖及版本化源码分发；仅完善计划，不改变实施状态。
- 设计依据：[总体规格 §24.1](../specs/2026-07-17-fullnet-architecture-design.md#241-企业应用与-saas-底座完善2026-09-16)、[产品能力队列](../../roadmap/adminnet-feature-parity.md#8-企业应用与-saas-底座完善队列2026-09-16)。
- 文档核对基线：`main` / `ccd12944d0a77d295d062bfccf7f985fcb7b1362`。按用户要求，工作区其他改动不作为本计划实施证据；开工时重新核对实际提交和相关实现。
- 状态（2026-09-22 核对）：F00 已有[基线核对](../../verification/2026-09-17-foundation-productization-f00-baseline.md)；F01—F16 已有部分模板、领域、API、Vue 和测试资产，企业与 SaaS 子集见[企业预设](../../verification/2026-09-17-enterprise-preset-closeout.md)、[业务接入](../../verification/2026-09-17-enterprise-business-integration-closeout.md)、[SaaS 预设](../../verification/2026-09-17-saas-preset-closeout.md)。F08b 席位编排与 `module-local-transaction-debt` 清债见 [F08b 收口](../../verification/2026-09-22-f08b-foundation-closeout.md)（`da77e74e` 及以后提交）。下方未勾选项表示整项验收未关闭，不代表没有实现。不得根据局部收口标题直接升级 Verified 或 SaaS Production-verified。
- 初版交付仅修改文档；2026-09-19 用户已授权按审查顺序继续实施：当前命名门禁 → 当前提交 CI/SSO 验收 → 安全修复清单 → 企业/SaaS 缺口 → 生产认证。生产启用、真实收费、批量通知、不可逆删除仍遵循对应授权与发布流程。
- 本文件是“底座完善”唯一活动总计划。OIDC、AI、现有安全修复等专项不被替代；按所需契约的实际已交付范围消费证据，不等待整个专项完成。新增切片在本文展开，确需独立专项时先登记任务转移及唯一所有者，不能双处维护勾选状态。

### 2026-09-26 自主检查与首轮验收

- 授权：用户允许按本总计划自主检查、修改、补测试、本地快速验证、开发分支提交/推送及修复 GitHub Actions；合并与发布另行约定。
- 基线：`120d767ca3e70b68f95923fefcb0ad6378bcd0fb`，工作区干净；开发分支 `codex/foundation-acceptance-20260926`。远端 main 的历史结果不作为本基线通过证据。
- 首轮范围：F00 核对既有增量；优先关闭 F01/F15 的应用创建、升级安全与开发分支验收入口；用双库基础生命周期及企业样例验证已有能力。暂不改变产品政策、生产启用和真实支付渠道。
- 执行步骤：先为升级包摘要、路径、预设闭包、预览后修改与失败恢复建立可失败回归；再修复 `scripts/templates/upgrade-framework.mjs` 及必要辅助工具，同步模板打包；补齐 PR 的创建应用真实栈与受影响双库门禁；执行相关 Template/Contract/Governance 快速检查，独立只读复审后提交推送；按精确 SHA 等待 CI 终态并处理失败。
- 首轮远端反馈：`37ca424c` 的 API/Worker Native Actions 成功，主 CI 失败，尚不能关闭里程碑。失败暴露精简应用可选契约识别、并行模板构建初始化、租户履约服务生命周期及 MFA 迁移目录问题；分别补运行回归、串行模板验收、作用域装配检查与双库迁移重入测试。复审另发现 Workflow 三条变更路径在事务内读取租户权益，已移至事务前并用六个先失败的用例验证允许/拒绝路径，未新增事务债务豁免。
- 后续反馈：固定预设的历史共享迁移 203/206/207 在已核对全文摘要下按建表清单适配，未选表已存在时拒绝漂移，所选表缺失仍失败；完整闭包与无清单运行保持历史正文。代表性浏览器 CRUD 又发现首次 Development 播种早于本地租户创建，现补 Development 专属成员贡献者并覆盖首次与重跑。客户端保留数字模型、规范整数字符串转为安全整数，超出安全范围拒绝；申请单创建使用可信组织上下文头。上述修复在当前提交取得远端双库/浏览器证据前不关闭里程碑。
- `f1e23ee6` 的双库迁移 472 项全通过；受影响基础模块 290 项有 8 项失败。核对为精简测试宿主缺 Files 用量端口、MFA 校验错误状态码预期过时、商业重新激活用例漏开启已有策略。已补装配快速回归、稳定错误码断言与场景专属配置；未改变生产商业策略或去掉宿主依赖校验。
- `8894fb73` 的企业样例双库浏览器及客户端门禁通过，独立 Minimal 应用迁移后启动暴露产品装配缺陷：头像、租户 Logo 和文件配额服务强制依赖可选 Files Port。修正为带默认值的可选依赖；无 Files 时，媒体操作及文件用量对账在数据库/文件副作用前返回既有受控错误，席位对账保持可用，不能把缺失用量视为零。新增 16 项无数据库回归先全部因 DI 失败，修复后通过；保留完整 DI 验证。另隔离钉钉回执测试的环境变量，解决 Linux MethodLevel 并行清理造成的偶发失败。当前修复的独立应用真实栈与完整 CI 仍需按精确提交取得证据。
- `f2692b90` 完整迁移恢复在 90 分钟超时（467 通过、1 失败、未完成其余用例），失败指向 093 的同批次补列后回填编译。SQL Server 执行前仅在固定全文摘要下延迟编译两个回填语句，不改写历史 SQL 资源；MySQL 保持原路径，双库旧数据恢复继续必验。四项快速边界测试先 1 失败/3 通过，修复后连同前序兼容用例 22 项全通过。PR 将模块/Smoke 与两个互补双库迁移组并行执行，发现阶段验证迁移 UID 无遗漏/重叠；main 原完整迁移组按实际耗时保留 120 分钟预算。失败、跳过与未发现不能视为通过。
- `122cff25` 企业双库浏览器与客户端再次通过；独立应用越过 Files 装配后，发现当前会话授权缺 `IHttpContextAccessor`。认证入口现自行补齐该单例依赖，保留宿主覆盖与重复注册幂等；两场景先 1 失败/1 通过，修复后通过。PR 的原 `build-test` 检查名保留为汇总门禁，等待模块及两个迁移分组全部成功；失败、取消、跳过或缺失的迁移结果不得被独立模块成功掩盖。
- `93ef282f` 的独立应用 70 项、企业双库浏览器各 2 项、客户端、API/Worker Linux Native 均通过；双库迁移恢复 250/244 两组共 494 项全部通过，无跳过。Unit 3038、Compatibility 12、Architecture 230 项全部通过；受影响 Integration 295 项为 293 通过、2 失败。失败均为 Development 播种精确清单漏了开工基线已有的 `tenancy.entitlement_catalog_baseline`，实际 8 项、期望 7 项；Test Overlay 的旧清单也漏了同项。现共享显式 Baseline 清单，Overlay 仅追加自身条目，保留精确集合与 Succeeded 状态校验，并增加 Production 仅 Baseline 的审计断言。未改变运行时播种策略；修复后双库生命周期及其后置企业 API 验收仍待当前提交 CI。
- `2c10a5d2` 最终验收：主 CI、API Native、Worker Native 均为成功终态；播种双库生命周期、受影响模块、企业 API、四预设构建、Minimal 独立应用双库真实 CRUD、双库浏览器及迁移恢复门禁通过。首轮“创建应用、源码升级安全与代表性样例”切片关闭，证据与未验证项见[本轮交付报告](../../verification/2026-09-26-f01-created-app-real-stack-closeout.md)。后续文档提交仅同步事实，不改变已验收实现；F01/F15/F16 整项及 W1 不因此关闭。下一切片按 F02 核对既有诊断与生成器在新应用的真实业务 CRUD、跨租户拒绝和再生成保护。
- 验收：应用拥有的源码不被覆盖；损坏包、未知路径和本地定制拒绝写入；升级保留既定预设及成对迁移闭包；失败不丢原框架且恢复材料可定位；创建应用双库和受影响基础模块/样例有真实执行证据。
- 停止条件：需要新的业务政策、外部认证或发布决定，或恢复不能保留人工内容。未执行、跳过及生产等价验证继续保留未验证状态，不降低门禁。

### 2026-09-19 首批阻塞收口

- 基线：`main / 4ad8f0393ae0b0064fb55a63a4496cfe9b9141d2`；任务快照 `unfinished-closeout-20260919`。本节记录该基线上的未提交增量，不代表全计划完成。
- 命名：补齐 224/225/228 表列中文说明和目录覆盖，224/225 双库说明对齐；SQL Server 注释移出建表条件，支持表已创建但脚本未记账时补齐。228 索引使用两库一致的确定性压缩名称；223 固定动态 DDL 逐文件登记审查理由。迁移均首次进入本地基线提交，未执行已记账数据库升级；真实双库恢复仍待 CI。
- C01：修复 OIDC 关闭时旧会话切租户、Host 强撤服务无法装配。协议签发依赖保持按开关注册，OIDC 分支在任何数据库访问前检查完整依赖；历史授权撤销服务始终可用。关闭场景两项回归先失败，修复后通过，另补启用但依赖缺失时零数据库访问的失败关闭断言。
- F05/F08：邀请接受与配额预留/确认/释放改用 `ExecuteResultAsync`，修复后续 CAS 或确认失败仍提交前一条写入。五项真实事务协调器回归先复现提交错误，再验证回滚；不把此局部修复等同于跨模块事务拆分。
- 验证：聚焦 Unit 34/34、命名 32/32、SQL safety 5/5、OpenAPI 172/172、provisioner 37/37、AOT Architecture 73/73；API AOT analyzers 与 Integration Release 构建均为 0 警告/0 错误。集成分片按当前源码重新发现并验证无遗漏/重复后更新矩阵；发现数不是数据库执行通过数。
- 治理仍为 53/54：`module-local-transaction-debt.json` 的 RegisterAccount 与 AcceptTenantInvitation 两条债务尚未消除。第二条理由已纠正为实际的“事务内跨模块写入”，保持失败，不降低门禁。
- 远端核对：`a6d3d02e` 的 [Worker Native](https://github.com/yan041108/Full.NET/actions/runs/35164362025) 成功；[主 CI](https://github.com/yan041108/Full.NET/actions/runs/35164362020) 与 [API Native](https://github.com/yan041108/Full.NET/actions/runs/35164362019) 失败。当前基线及本轮增量没有远端通过证据。旧 E2E 编码错误已有修复与发现证据，仍需真实浏览器重跑。

### 2026-09-19 注册事务边界后续收口

- F04：Tenancy 活动租户权威读取移至 Identity 事务之前；事务内重新解析并核对政策版本/模式、注册方式版本/租户与邀请快照，目标变化时在消费挑战前拒绝。挑战消费和账号写入仍由 `ExecuteResultAsync` 统一提交或回滚。注册命令没有外层事务标记。
- 六项回归在旧实现全部失败；修复后连同邀请正常注册、租户变化、撤销共 9/9 通过。该切片只消除跨模块本地事务调用，不承诺租户在账号提交时仍处于活动状态的分布式原子保证。
- `module-local-transaction-debt.json` 已删除 RegisterAccount 条目，保留 AcceptTenantInvitation 席位写入债务；治理仍为 53/54，不降低门禁。
- 本轮验证：聚焦 Unit 43/43（含首批修复）、事务边界 Architecture 4/4、API Native AOT Architecture 73/73、命名 32/32、SQL safety 5/5；API AOT analyzers 0 警告/0 错误。单元发现数门槛随新增九例同步增加，不代表已执行完整单元套件。独立只读复审未发现新增阻断问题。
- 用户授权由代理决定是否提交：本切片与首批修复整理为本地提交；未推送，本提交的双库、浏览器和 Linux 原生执行仍待 GitHub Actions。

### 2026-09-19 配额恢复前置：安全重放

- F08a：先修正配额预留/确认/释放的重放语义，再建设跨模块恢复编排。相同完成终态重放只读返回当前用量，不重复改动计数；操作键绑定首次金额，Released、Expired 及过期 Reserved 不可作为新额度凭证；Confirmed 同金额预留可返回已有结果。
- 回归：首轮 11 例在旧实现 5 失败/6 通过，确认重复确认、重复释放、金额冲突、已释放和过期预留误成功。另补显式 Expired 的三种操作拒绝、Confirmed 金额冲突四例；双库共用 API 验证扩展原有测试，覆盖响应丢失后顺序重放与用量不重复变化。
- 发现恢复前置缺口：预留记录没有保存实际 MetricId/PeriodKey，完成时重新查 default 或当前月份；过期补偿可能指向错误周期，因此本轮仍拒绝过期未完成操作，不把安全重放等同于可恢复编排。
- 后续顺序：先持久化配额记录归属并处理历史预留回填/歧义，统一跨指标 OperationId 的寻址约束；再开放准确的过期释放；随后以 Identity 持久化操作状态及可靠事件/后台重试驱动席位预留、成员写入、确认、补偿和对账。必须覆盖跨月、重复投递、确认响应丢失、进程中断与邀请撤销竞争。
- 本轮验证：聚焦 Unit 22/22（新增重放 15 例及既有回滚/校验 7 例）、API Native AOT Architecture 73/73、SQL safety 5/5、Integration tooling 46/46；更新后的 Integration Release 构建与 API AOT analyzers 均为 0 警告/0 错误。独立只读审查未发现新增阻断问题。
- 本切片不移除 AcceptTenantInvitation 架构债务，治理保持 53/54，不提升 F08 或 SaaS 预设状态；双库实际执行待 CI。影响计划指向 integration-matrix、smoke、Tenancy；按用户授权整理本地提交，未推送。

### 2026-09-19 配额记录绑定与过期释放

- F08a：新增双库 229 前向迁移，为预留增加可空 MetricId；新预留在同一 Tenancy 事务记录实际配额度量标识。确认、释放和同向终态重放只查询 Id + TenantId + MetricCode 对应记录，不再随 default 或当前月份重新选取配额；Native AOT 映射同步增加可空 UUID 列。
- 兼容与历史数据：迁移不猜测回填旧记录、不更改用量，重复运行保留已绑定值。旧 NULL 记录不能复用或完成，保留计数等待权威业务事实对账；没有可靠归属证据时不得自动绑定或直接释放。绑定缺失或指向已不存在的记录时失败关闭。
- 已绑定且仍为 Reserved 的过期预留允许释放回原始配额记录；过期确认继续拒绝，Confirmed/Released 不允许反向转换。取消/并发失败仍使用结果感知事务，保持计数与预留终态一起回滚。
- 回归：旧实现三例全部失败，修复后扩展跨月、default 变化、终态重放和绑定记录缺失。双库共用 API 验证预留后新增 default 仍完成到原月记录；新增两库迁移恢复测试验证缺列重建、历史 NULL 保留、已有绑定保留及再次执行无迁移。
- 部署约束：先暂停配额及成员变更并排空旧 API/Worker，再运行迁移并统一切换新二进制；不能混跑仍按当前周期完成操作的旧版本。回退只保留新列/新数据且维持写入暂停，不能用旧代码继续确认/释放已绑定操作；历史归属修复需另行提供可审计工具和双库证据。
- 验证：聚焦 Unit 32/32（本切片新增 10 例）、API Native AOT Architecture 73/73、命名 32/32、SQL safety 5/5；API AOT analyzers 与 Integration Release 构建均为 0 警告/0 错误。Integration 分片发现 api-sqlserver 153、api-mysql 153、migrations 470、infrastructure 176、messaging-heavy 57，合计 1009，无遗漏/重复；这是发现证据，不是数据库运行证据。独立只读复审未发现新增阻断问题。
- 当前切片未实现后台扫描、跨指标操作键唯一性或 Identity 持久化编排，AcceptTenantInvitation 债务继续保留，治理仍为 53/54；未获得双库运行及 Linux 原生执行证据前不提升 F08/SaaS 状态。已按用户授权准备本地提交，未推送；CI 影响集为 integration-matrix、migrations、smoke、Tenancy。

### 2026-09-19 跨指标操作键精确寻址

- F08a：确认/释放请求新增可选 MetricCode，保持原单参数构造与 Deconstruct；现有 source-generated JSON 上下文自动覆盖新属性。提供指标时按 TenantId + MetricCode + OperationId 唯一查询，不存在时不回退。空白或超长指标在查询前拒绝。
- 旧调用兼容：省略或传 null 时，仅返回同租户操作键唯一的预留；自关联 NOT EXISTS 排除歧义行，返回既有 quota reservation not found 404，避免 QuerySingle 多行异常或任意选取。同操作键的其他租户不构成歧义。席位适配器固定传 identity.seats，减少对旧协议的依赖。
- 数据兼容：不改变数据库唯一键，不重写历史 OperationId，不新增迁移。精确寻址复用现有三列唯一索引，完成仍以已绑定的 MetricId 定位实际用量。旧请求查询时点后的并发新增不构成跨模块原子保证。
- 测试：首轮两条 sourcegen JSON/精确寻址回归在旧实现均失败；另补无效指标、无回退、席位适配器用例，共新增 12 例。双库共用 API 增加两个指标共用操作键、旧调用拒绝、精确确认/释放及同向重放。现有客户端导出 manifest 不含这些 quota 端点，本轮不扩大导出集合。
- 验证：聚焦 Unit 44/44、API Native AOT Architecture 73/73、OpenAPI 172/172、命名 32/32、SQL safety 5/5；AOT analyzers 与更新后 Integration Release 构建均为 0 警告/0 错误。独立只读复审未发现新增阻断问题；真实双库和 Linux 原生执行仍待 CI。
- 工具链补漏：本轮发现上一切片 229 未登记 migrationSelections，导致工具链 45/46；补上双库筛选并将参数化迁移测试拆为具名 SqlServer/MySql 入口后恢复 46/46，测试数量不变。
- 剩余：历史 NULL 绑定仍需有权威证据的对账与修复工具，未建设 Identity 持久化编排及后台补偿；AcceptTenantInvitation 债务继续保留，治理仍为 53/54，不提升 F08/SaaS 状态。按用户授权整理本地提交，未推送。

**下一切片（2026-09-19 记录，已由 2026-09-22 切片部分兑现）：** 完成历史归属对账，再消除 F05/F08 席位跨模块写事务；以持久化操作身份、业务权威状态、补偿和对账覆盖确认丢失/进程中断，不能仅把调用移出事务或删除债务条目。随后按当前提交执行双库恢复、OIDC/legacy 真实栈和 Linux 原生门禁。治理与环境证据未齐时，不提升企业/SaaS 预设状态。

### 2026-09-22 F08b 席位编排与 F08a 对账工具

- 基线：`main` / `da77e74e`（Wave 1 Lane C）；Spec [`2026-09-22-f08b-tenant-invitation-seat-orchestration.md`](../specs/2026-09-22-f08b-tenant-invitation-seat-orchestration.md)。
- F08b：`AcceptTenantInvitationService` 与 `TenantMemberProvisionService` 将 Tenancy 席位预留/确认/释放移出 Identity 本地事务；确认失败执行成员/邀请补偿；`module-local-transaction-debt.json` **entries: []**，治理 `capability-debt-sync` 与债务目录一致。
- F08a：历史 NULL `MetricId` 预留对账 API `POST /api/v1/tenancy/quota/reservations/reconcile-metric-ids`（`dryRun` 默认 true）；权限 `tenancy.tenant_quota.reconcile_metric_ids`。
- 验证：Unit `TenantInvitationRollbackTests`、Architecture 模块边界；双库 Integration 对账端点、邀请接受 real-stack 与 SaaS 预设升档仍属 Wave 2–3 门禁，本切片不单独提升 F08/SaaS 为 Verified。
- **下一切片（2026-09-22）：** F05 企业成员全链路双库/real-stack 验收；F12 SaaS 预设在席位编排与配额对账证据齐全后的门禁；持久化操作身份与 Outbox/Jobs 补偿仍按 §F08 正文未勾选项推进。

### 2026-10-27 F08b 持久化席位恢复

接替 Workflow 之外的开发，任务基线为 main / 2fe0d52bab8441c528b251ff28fa652b8dd76c7f，快照 non-workflow-development-20261027；保留共享工作区原有改动。邀请接受、成员开通及重新激活先提交 Pending 成员与 Identity 自有工作项，再在事务外预留/确认；后台以租约接续，移除后按原消费身份幂等归还。配额对账在成员计数前后探测未收敛操作，保护激活和取消窗口。双库 324 迁移与 AOT 固定列序物化同步。

实际通过：业务单元 60/60、注册 2/2、双库 6/6、事务/表归属/外键架构 8/8、测试矩阵 Node 7/7；迁移命名/UUID/注释零违规，Identity/Tenancy AOT 静态分析构建零警告、零错误。设计与范围见 [席位编排规格](../specs/2026-09-22-f08b-tenant-invitation-seat-orchestration.md)。未执行全量或 Native AOT 发布；不把本切片等同于 F08 全部消费者或 F12 支付履约。Workflow 后续能力由另一处开发，本任务不修改其实现。

## 2. 能力、顺序与范围

### 2026-09-25 认证事件日志专项

新增[认证事件日志开发计划 AE01—AE06](2026-09-25-authentication-event-logs.md)，作为 Identity/安全运维收口项。复用现有 `fn_identity_auth_audit`，顺序为事件覆盖与契约 → 兼容迁移/可靠写入 → 认证及 SSO 采集 → 查询 API → Vue 页面 → 保留/导出与验收。F04 的密码/MFA 恢复事件与 SSO 专项共同提供事件来源；本专项负责统一展示及覆盖闭环。首条纵向切片已落地，状态为 Partial；完整事件矩阵与故障验证仍待完成。

| 能力 | 优先级 | 本计划任务 | 最小交付 | 不纳入首切片 |
| --- | --- | --- | --- | --- |
| B01 项目创建与升级工具链 | P0 | F01、F02、F15 | 空目录创建、模块预设、诊断、示例、版本升级演练 | 云端在线 IDE、插件市场、自动覆盖人工代码 |
| B02 企业开通与成员生命周期 | P0 | F05、F06 | 开通流程、邀请、成员管理、所有者交接、退出与停用 | 跨公司复杂 HR、通用组织主数据平台 |
| B03 套餐权益与统一配额 | P0 | F07、F08 | 套餐绑定、功能权益、席位/存储配额、原子预留和对账 | 全局计费引擎、按任意 SQL 动态定义权益 |
| B04 账号自助与恢复 | P0 | F03、F04 | 可配置注册、邮箱验证、密码/MFA 恢复、会话撤销 | 默认匿名开放企业注册、同时铺开所有短信渠道 |
| B05 SaaS 订阅运营 | P1 | F12 | 试用、续期、取消、到期策略、支付对账驱动权益 | 自动税务、复杂账务总账、任意币种自动换算 |
| B06 开放集成与 Webhook | P1 | F13、F14 | 注册事件、签名、投递重试/重放、接收样例、SDK | ESB、任意脚本集成、运行时扫描插件 |
| B07 业务接入标准样板 | P1 | F09、F10、F11 | 业务单据贯通附件、权限、审批、通知和数据交付 | 完整 ERP/CRM 产品、第二套审批引擎 |
| B08 发布升级恢复交付包 | P1，发布必需 | F15、F16 | 支持矩阵、发布清单、备份恢复、升级回退演练 | 未经证据承诺所有部署拓扑和容量 |

本节表格和依赖图保留初始能力分解；当前只执行 §8 中的剩余增量，不从 F00/F01 重建已完成能力。基础预设可明确不启用配额，限额/SaaS 预设复用 F07/F08 已交付内核并验证实际消费者，故障不得降级为无限额。优先级不绕过依赖和权限、数据、恢复边界；可选能力不全部阻塞核心预设。

```mermaid
flowchart LR
  F00[核对与安全基线] --> F01[创建模板] --> F02[诊断与CRUD样板]
  F00 --> F03[验证挑战] --> F04[账号恢复]
  F04 --> F05[企业与成员] --> F06[企业生命周期]
  F00 --> F07[套餐权益与兼容迁移] --> F08a[配额协议内核]
  F08a --> F08b[席位与存储接入]
  F05 --> F08b
  F02 --> F09[业务单据]
  F05 --> F09
  F09 --> F10[审批与通知] --> F11[导入报表打印]
  F06 --> F12[订阅运营]
  F08b --> F12
  F10 --> F13[Webhook] --> F14[SDK与接入样例]
  F02 --> F15[升级演练]
  F11 --> F16[预设发布验收]
  F12 --> F16
  F14 --> F16
  F15 --> F16
```

## 3. 数据、契约与跨模块边界

1. Identity 拥有账号、验证挑战、认证会话、企业成员的登录/授权绑定；Organization 拥有部门、职位和组织隶属；Tenancy 拥有租户生命周期、套餐、权益与配额。F00 必须先映射现有表/Port，扩展原关系，不另造平行成员事实源。
2. Tenancy 拥有开通与订阅编排记录；Payments 继续拥有支付、退款和渠道事实。支付成功不直接修改另一模块表；可信业务事件推动订阅状态，按版本去重，失败可重放与对账。
3. 企业所有者表示租户内受保护职责，不是 Host 超级管理员；最后一名可管理成员和所有者交接使用 Identity 内原子校验，不绕过会话、租户和操作权限。
4. 功能权益决定租户可用功能，RBAC 决定当前人可执行操作，配额决定剩余使用量。有效调用须分别通过对应检查。业务权益不存入通用 Settings ConfigEntry；Settings 只承接展示偏好或明确的平台配置。
5. TenantMembership、Entitlement、QuotaReservation 等是本计划的领域概念，不承诺现有类同名。F00 在本文登记实际表、内部类型、消费者及方法签名后才开始相应编码；外部稳定契约先冻结，再生成客户端。
6. 配额预留与业务写入分属不同所有者时，不承诺一个本地事务原子完成。以稳定 OperationId、预留凭据、确认/释放和恢复对账组成协议；未知执行结果保留占用，不能到期直接释放并允许重复消耗。
7. 会话、成员状态、付费权益和硬配额的拒绝判断使用权威状态或已批准的强一致策略，不能依赖可能陈旧的 L1 授权。业务操作与权威判断通过最小 Port；后台列表、搜索、报表及同语义导出允许按 ADR-0014 登记依赖的集中只读 JOIN，保留租户、数据和字段权限。跨模块写入走可靠事件/幂等工作流，事务内不调用其他模块 Port，不进行跨模块写表或事务。
8. Vue 为唯一后台主线。普通业务 API 用 ProblemDetails；OIDC 端点沿用 ADR-0011 的协议例外。后台任务、导出、通知、Webhook 与 AI 均保留租户及数据权限边界。
9. 模块按真实消费者编译期装配，沿用现有 Composition 预设。新增可选模块只创建一个主项目；Contracts 拆分需要真实消费者证据，不为框架完整度预建空层。
10. 首版应用模板使用固定提交、带版本与内容摘要的源码分发包。生成应用在 `framework/fullnet/` 保存受管框架源码，在 `src/`、`ui/` 保存应用拥有的宿主/业务/界面；框架与应用所有权由 manifest 区分。现阶段不以尚未发布的 Full.NET NuGet/npm 包为前提，也不访问开发者原始仓库或任意最新分支。
11. 邀请制允许没有账号和会话的受邀者验证邀请并注册。邀请授权、目标邮箱验证、账号认证和成员激活是不同步骤；原有账号必须完成其原认证/MFA，不能通过邀请重置密码或绕过 MFA。账号/挑战/邀请/成员均由 Identity 原子维护，企业状态等跨模块约束仍走受控协作。
12. 新 SaaS 预设使用强制权益策略并要求有效套餐；无绑定、状态读取失败与未知权益必须失败关闭。现有阶段策略按真实消费者维护，不为假定历史部署新增兼容阶段、回填或混合运行要求；实际数据变更仍须保证双库一致与失败恢复。

## 4. 文件与验证地图

以下新路径是实施目标，不表示文件已存在。实现任务按当时库存选择成对迁移编号，禁止预占或改写已应用迁移。测试类可按实际夹具收敛，但必须在本文记录最终入口，不能只有无调用者的断言辅助类。

| 任务 | 主要修改/新增位置 | 测试目标 |
| --- | --- | --- |
| F00 | 本计划；`docs/roadmap/capability-status.md`；既有专项执行记录 | 核对证据与实际入口，不新建形式化空测试 |
| F01–F02 | 新增 `templates/fullnet-app/.template.config/template.json`、模板内容与 `scripts/templates/verify-created-app.mjs`；复用 `src/Tools/Full.NET.CodeGeneration.Cli/`、`src/Composition/` | 新增 `tests/templates/created-app.test.mjs`、`tests/templates/template-options.test.mjs` |
| F01/F15 | 新增 `scripts/templates/build-source-bundle.mjs`、`templates/fullnet-app/framework-manifest.schema.json`；分发包内包含锁定来源、项目/包依赖及迁移清单，生成应用保存 `framework/fullnet/` 和 `framework-manifest.json` | 新增 `tests/templates/source-bundle.test.mjs`、`tests/templates/source-bundle-upgrade.test.mjs`，包含摘要损坏、外部路径、离线来源与人工冲突 |
| F03–F04 | 复用 `src/Modules/Full.NET.Modules.Notifications/Features/VerifyRecipientEndpoints/`；扩展 Identity 的 `Features/ManageRegistrationPolicy/`、新增 `Features/RegisterAccount/`、`Features/RecoverAccount/` | `tests/Full.NET.UnitTests/Identity/AccountRecoveryTests.cs`、`tests/Full.NET.IntegrationTests/Identity/AccountLifecycleAssertions.cs` |
| F03–F05 | Identity 新增 `Features/RegistrationInvitations/`，F04 提供一次性邀请/注册领域能力，F05 接管理与入驻入口；Notifications 新增 `Features/SendIdentityChallenge/` 的内部受控投递适配，复用现有邮件 Provider | `tests/Full.NET.IntegrationTests/Identity/InvitedRegistrationAssertions.cs`；覆盖无账号/无会话、已存在账号、撤销竞争及未入租户的收件人 |
| F05–F06 | Tenancy `Features/ProvisionTenant/`；Identity 新增 `Features/ManageTenantMembers/`、`Features/AcceptTenantInvitation/`；Organization 成员适配；Vue 新增 `TenantMembersView.vue`、`TenantOnboardingView.vue` | `tests/Full.NET.UnitTests/Identity/TenantMembershipTests.cs`、`tests/Full.NET.IntegrationTests/Identity/TenantMembershipAssertions.cs`、`tests/Full.NET.IntegrationTests/Api/TenantLifecycleAssertions.cs` |
| F07–F08 | Tenancy `Features/ManageHostTenantPackages/`；新增 `Features/ManageTenantEntitlements/`、`Features/ReserveTenantQuota/`；首个 Identity/Files 消费者 | `tests/Full.NET.UnitTests/Tenancy/TenantEntitlementTests.cs`、`TenantQuotaTests.cs`；`tests/Full.NET.IntegrationTests/Api/TenantEntitlementAssertions.cs`、`TenantQuotaAssertions.cs` |
| F09–F11 | 新增 `samples/enterprise-request/`；复用 Workflow、Files、Notifications、ImportExport、Reporting、Printing 模块及生成器 | `samples/enterprise-request/tests/`；`tests/e2e/admin-real-stack/tests/enterprise-request.spec.mjs` |
| F12 | Tenancy 新增 `Features/ManageTenantSubscriptions/`；Payments `Features/ManageOrders/`、`ManageRefunds/`、`ReceiveWeChatNotify/`；Vue 新增 `TenantSubscriptionView.vue` | `tests/Full.NET.UnitTests/Tenancy/TenantSubscriptionTests.cs`、`tests/Full.NET.IntegrationTests/Api/TenantSubscriptionAssertions.cs` |
| F13–F14 | 首个消费者出现时新增 `src/Modules/Full.NET.Modules.Webhooks/`；复用 Identity OpenAccess、Auditing、Outbox；新增 `samples/webhook-consumer/`；`packages/client-contracts/` | `tests/Full.NET.UnitTests/Webhooks/WebhookDeliveryTests.cs`、`tests/Full.NET.IntegrationTests/Api/WebhookDeliveryAssertions.cs`、样例契约测试 |
| F15–F16 | `deploy/helm/fullnet/`、`docs/operations/`、`docs/development/onboarding.md`、发布工作流；新增 `docs/operations/application-upgrade.md`、`docs/operations/application-recovery.md` | `tests/deployment/`；模板旧版本升级夹具；现有 Native API 与 Worker 外部进程夹具 |

数据任务共同涉及 `src/BuildingBlocks/Full.NET.Migrations.DbUp/Migrations/SqlServer/` 与 `MySql/`、模块 SQL/JSON/AOT 登记、`contracts/database/object-comments.json`、`contracts/architecture/global-sql-statements.json`。增加测试时同步 `eng/testing/test-matrix.json`；普通管理前端复用 `packages/client-contracts/` 的 OpenAPI 生成，不手写第二套 DTO。

## 5. 新能力实施任务

以下清单是初始交付范围，勾选状态已落后于各节及 §7 的后续实施记录。自 2026-10-29 起停止用这些历史复选框安排开发，统一以 §8 的剩余队列为准；不能将未勾选等同于尚无实现，也不能将局部验收等同于整项完成。任务完成记录实际提交、验证结果和未验证边界；同一功能批次集中完成关联实现，再集中执行聚焦验证。

### F00：冻结现状、已有计划分工和第一条交付基线

**依赖：** 无。**产出：** 可追踪的模块/入口/证据清单和首批实际接口签名。

- [ ] 核对八项能力在当前提交中的 Endpoint、表、Vue、测试和专项；对已实现内容只记录缺口，禁止重建。特别核对 Registration/OAuth/OIDC、通知验证码、ImportExport/Reporting 与 Payments。
- [ ] 将 §6 的 C01—C07 映射到原计划的具体未关闭任务及证据；文档冲突时记录“实现存在、验收待核”，不凭标题直接改为完成。
- [ ] 在本计划追加第一批切片的契约登记：所有者、调用方、请求/结果字段、错误码、精确权限、幂等键、并发版本及数据兼容策略。跨模块先证明现有 Port 不足再扩契约。
- [ ] 选择最小可发布预设及其真实验证范围；带已知安全/隔离问题的链路先修复，未选入的可选模块不使核心预设假通过。

**验收：** 每个新能力可定位到现有资产、增量和任务所有者；每个当前发布阻塞有关闭条件，未产生代码能力通过声明。

### F01：从空目录创建独立应用

**依赖：** F00。**消费：** Naming Profile、Composition 预设。**提供：** 基础管理、企业应用、SaaS 的可选择模板；未满足能力门禁的预设标为实验性且不默认启用。

- [ ] 先实现版本化源码包构建：固定源提交，登记每个受管文件摘要、框架版本、模块闭包、中央包版本、构建 props/targets、源生成器、迁移资源、CLI 与前端 workspace 依赖及锁文件。包不含 bin/obj、秘密、本机缓存、绝对路径或指向原仓库的链接；摘要不符拒绝实例化。
- [ ] 从现有静态注册清单产生所选预设的 Composition 投影及项目引用闭包，不把当前引用所有模块的 Composition 原样复制后仅关闭运行开关。登记哪些迁移/种子属于所选闭包及其历史依赖，禁止按名称过滤后静默跳过必要迁移。
- [ ] 建立模板参数负例：非法/保留 OwnerKey、未知模块、缺硬依赖、输出目录冲突均拒绝且零覆盖。
- [ ] 定义项目名、OwnerKey、SQL Server/MySQL、模块预设、前端和端口选项；冻结输出 manifest，秘密只生成占位配置。
- [ ] 模板包随附受管框架源码，以应用内部相对 ProjectReference 和 workspace 引用完成接线；初始化时复制的宿主/业务/界面骨架之后属于应用，不被框架升级器直接覆盖。NuGet/npm 第三方依赖仅从配置的源按锁定版本还原，Full.NET 自有代码与前端共享包来自分发包。
- [ ] 在不能访问原仓库、没有自有包缓存的干净环境，仅凭版本化模板包与声明的第三方依赖源创建两种数据库应用，执行还原/构建、迁移/引导、登录和一个管理读取；重复创建不覆盖用户文件。原生构建验证包内 analyzers/generators/迁移资源齐全；额外无网络还原只有在依赖预缓存条件明确时才要求。

**验收：** 新应用不依赖原仓库绝对路径；未选择模块没有意外运行依赖；首次运行说明与实际步骤一致。后续计划命令示例：`dotnet new fullnet-app --name Demo --owner-key demo --database mysql`，模板未登记前不可报告该命令可用。

### F02：环境诊断与生成一个真实 CRUD

**依赖：** F01。**提供：** 可复用的应用开发起点及受控诊断结果。

- [ ] 对缺 SDK、错误数据库配置、遗漏模块依赖、未配置密钥建立诊断断言；输出机器码和修复指引，不泄露凭据。
- [ ] 在现有 CLI 内扩展诊断入口，先只读检查，用户显式要求后才执行初始化；区分本地开发和生产配置。
- [ ] 用现有生成器在新应用生成一个租户 CRUD，贯通数据库、后端、OpenAPI、Vue 和精确权限；开发者修改业务实现后再生成，证明人工文件不会丢失。
- [ ] 完成从创建到首个 CRUD 的教程并实走；记录耗时与失败原因，性能目标由实测基线后制定，不虚称固定分钟数。

**验收：** 新应用的真实新增/编辑/查询和跨租户拒绝通过；生成器只更新其持有的产物。

2026-09-26 执行顺序（用户已授权自主检查与升级；基线 `264ea40d`，干净工作区）：

1. 诊断收口：以 `tests/Full.NET.UnitTests/CodeGeneration/DiagnoseCommandTests.cs` 复现未知 Profile、配置类型错误与目标工作区 SDK 选择缺口；修改 `src/Tools/Full.NET.CodeGeneration.Cli/CodeGenerationCli.cs` 与 `DiagnoseCommand.cs`，保持只读、脱敏、稳定机器码及非零失败语义。执行聚焦 Unit 与治理，更新 `docs/development/create-first-crud.md` 的真实入口和限制。
2. 生成接入：复用现有 Schema、工作区所有权与模块接入工具，在 `tests/templates/support/created-app-real-stack.mjs` 中增加独立应用生成路径；先检查宿主、Migrator、OpenAPI 和 Vue 的实际接入点，不复制官方模块或改写受管框架作为业务实现。
3. 真实验收：在双库新应用验证生成业务的新增/编辑/查询、精确权限、跨租户拒绝；保留人工业务文件及人工修改的受管文件冲突拒绝，二次生成不得损坏内容。重型验证由 GitHub Actions 执行，失败先定位再修复。
4. 教程与关闭：修正 `docs/development/create-first-crud.md` 与 `first-crud-from-template.md` 中仓库布局和独立应用布局混用的命令；只依据实际执行证据记录耗时与结果。诊断子集通过不代表整个 F02 关闭；SDK 缺失时不能承诺依靠尚未启动的 .NET CLI 自救。

停止条件：需要新的业务政策或发布决定、再生成不能保留人工内容、跨租户或权限拒绝缺失；不得放宽双库、冻结的 Layui 边界或 Native AOT 门禁。

诊断第一增量：合法 `--profile` 曾因默认值被当作已传参而全部返回 64；修复为解析后再应用默认值，并拒绝未知 Profile。SDK 检查改用目标工作区，类型错误输出脱敏 `DIAG_APPSETTINGS_INVALID`，取消继续传播，环境连接占位符不得视为已配置。回归先 11 项中 10 失败；入口修复后 10 通过、1 失败，精确复现生产环境变量占位符误报。审查追加 6 条回归，其中 inline 空白、ConnectionStrings 非对象及 Redis 秘密 bool/number 4 项先失败，再统一为空白/占位符拒绝与严格字符串类型。最终 CLI 聚焦 66 项、CodeGeneration/Realtime 414 项、治理 55 项通过，无跳过，Release 构建 0 警告/0 错误。Integration 1075 项仅为分片发现证据；独立 Minimal 双库应用现必验应用自带 CLI 的 SDK/工作区/预设/模块闭包及配置只读性，实际执行与远端门禁待此增量推送后验证。完整配置覆盖、SDK 版本兼容矩阵、UserSecrets 内容与独立生成业务链路仍待后续增量，不关闭 F02。

生成接入第一增量（基线 `0702cc54`）：内部模型已支持省略 Layui 路由，但 CLI JSON 读取器仍把路由与控制器字段标为 required，且同命名空间旧实现遮蔽共享路由接入器。读取器回归先 5 项中 4 失败；实际 CLI 回归先 3 项全部因空引用失败。三个字段改为可选并移除旧实现后，实际 CLI 3 项通过，覆盖 Vue 路由写入、重复执行幂等、无 Layui 输出或目录，以及缺少前提或聚合桥所有权时拒绝写盘。CodeGeneration/Realtime 422 项、治理 55 项全部通过，无跳过，Release 构建 0 警告/0 错误。未知字段拒绝及显式 Layui 控制器配对校验保留；未修改冻结客户端。诊断提交 `0702cc54` 的模板真实栈作业已成功，包含独立 Minimal 双库应用自带 CLI 与配置只读性检查；其余门禁与生成接入增量按精确 SHA 继续核对，不替代完整生成 CRUD 验收。

2026-09-26 授权接入修复计划（基线 `ee3461da`）：现有 `AuthorizationContributorIntegrationEditor` 把集合元素追加到类型外，且只凭一个权限标记跳过整条接入。先在 `tests/Full.NET.UnitTests/CodeGeneration/AuthorizationContributorIntegrationEditorTests.cs` 复现三个集合插入、重复幂等、部分标记与人工漂移、注释/字符串伪装及非标准集合拒绝；复用既有轻量 C# 词法分析确认唯一标准集合位置，保持手写元素并逐集合验证完整生成块，任何歧义保持原文返回失败。生成文件以独立小项目做实际编译实验，执行聚焦 Unit、治理及影响集规划后独立审查、提交推送。此增量不新增 CLI 命令、不改变权限作用域政策或数据库结构，不代表完整生成业务验收。

授权结构接入结果：首批 Unit 8 项先 7 失败/1 通过，修复集合内插入后通过；追加边界回归复现非标准重复声明，独立复审又复现“完整块藏入被丢弃的嵌套集合”伪幂等，现已要求生成块开始和结束均处于直属元素边界。新增 15 项回归覆盖标准插入、手写元素保留、两实体依次接入与各自幂等、CRLF、部分/重复/越界/人工漂移、字符串伪标记与不明确形态拒绝。旧追加方式的独立结构编译实验报 11 错误，实际编辑结果编译为 0 警告/0 错误；该实验使用最小类型定义，不替代实际授权目录或应用运行。

本地新鲜证据：`pnpm test:dotnet:unit -- --selection code-generation-realtime` 437 项通过，`pnpm test:aot:analyzers` 0 警告/0 错误，`pnpm test:dotnet:architecture -- --selection api-native-aot` 73 项通过，`pnpm test:governance` 55 项通过，均无跳过；`pnpm test:integration:partitions` 发现 1075 项，无遗漏或重复，不计为完整 Integration 通过。影响集规划基线为 `ee3461da`，目标 CodeGeneration。首次分析构建曾与测试构建争用 DLL 而失败，串行复验已通过；不将第一次失败计为成功。独立复审无剩余阻断，远端按新提交 SHA 核对。生成片段的权限作用域政策、CLI 授权接入、应用 Migrator、完整生成业务与宿主整条接入的并发/恢复仍未验收，不关闭 F02。

授权作用域增量（基线 `34ab3bcc`）：片段生成器原来对租户 CRUD 固定输出 Host 权限，违背 `TenantRequired` 数据上下文。新增 7 项 Unit；纠正测试样例中显式能力禁止的审计列后，正确 RED 为 3 失败/4 通过，覆盖两种实体能力格式的租户映射及旧 Host 块不允许静默改写。现六条权限共用 `TenantRequired → Tenant` 映射，`HostOnly/Global → Host` 保留现有最小授权范围，未改变精确权限码或官方模块贡献者。租户 CatalogProduct golden 仅两处作用域随实际输出更新；CodeGeneration/Realtime 444 项、治理 55 项通过，无跳过，独立复审无阻断。实际授权运行、CLI 贡献者接入和独立应用 CRUD 仍待 F02 全链验收。

作用域增量补充验证：`pnpm test:aot:analyzers` 0 警告/0 错误，`pnpm test:dotnet:architecture -- --selection api-native-aot` 73 项通过、无跳过；`pnpm test:integration:partitions` 发现 1075 项，无遗漏或重复，不作为完整 Integration 通过。影响集按基线 `34ab3bcc` 规划为 CodeGeneration 与 integration-matrix，重型双库及 Native 运行门禁交由新提交的 GitHub Actions，未取得终态前不升级 Verified。

### F03：复用通知平台完成账号验证挑战

**依赖：** F00、C04 通知安全收口。**提供：** Identity 账号操作挑战及 Notifications 投递衔接。

- [ ] 核对现有收件端点验证与验证码能力，区分“拥有邮箱地址”和“获准恢复账号”；不得把通用收件端点 verified 直接当作密码重置授权。
- [ ] 建立挑战用途/账号/目标地址绑定、过期、尝试上限、一次消费、并发重放与重发替换测试；未知账号不通过响应差异泄露存在性。
- [ ] Identity 保存其操作挑战的不可逆凭据摘要，通过稳定投递意图请求 Notifications；外部发送事务外进行，失败/未知送达状态可追踪且不消费成功凭据。
- [ ] 提供仅供受信 Identity 调用的挑战投递 Port：绑定用途、ChallengeId/InvitationId、规范化目标邮箱、有效期、发起操作及可选账号/目标租户，不要求接收人已有 UserId、已加入租户或 verified RecipientEndpoint。公开请求不能自行选择模板、渠道配置、任意内容或批量地址；按地址/来源/用途限流，原普通通知用户目录与 verified 校验保持不变。
- [ ] 邀请/注册凭据若需异步投递，原值只能在受限、加密且限期清理的投递载荷中存在，身份校验侧仍只保存摘要；过期/撤销挑战不得继续重发。跨模块交付通过幂等投递身份与状态对账恢复，不让“创建挑战成功”被当成“已送达”。
- [ ] 选择一个邮件测试渠道验证真实投递和消费，日志与界面不回显验证码；测试环境受控收件箱证据与生产渠道认证分别记录。

**验收：** 两个并发消费最多一个成功；未送达不被标记为已验证；不能跨用途复用挑战。未注册且未加入任何租户的邮箱可通过受控挑战渠道收信，但不能借该渠道调用普通通知 API 或发送任意邮件。

### F04：注册、密码恢复与 MFA 恢复

**依赖：** F03；新旧会话撤销消费 C01。**提供：** Identity 的完整账号自助流程。

- [ ] 以现有注册政策为入口，明确 Disabled/InvitationOnly/Open：Disabled 拒绝所有新账号，InvitationOnly 仅接受有效一次性邀请，Open 才允许无邀请注册；默认 InvitationOnly。F04 在 Identity 内实现邀请校验与注册领域原语，由受控测试夹具创建邀请验证闭环，F05 再交付正式签发/撤销管理入口，因此 F04 不依赖 F05。
- [ ] 无会话受邀者先校验邀请，再以独立邮箱挑战证明目标地址控制权，设置密码后建立账号与可重试的入驻状态；不要求先登录不存在的账号。邀请、邮箱挑战、账号创建与幂等注册结果在 Identity 内原子提交；账户可建立时不得提前获得尚未激活的企业权限。既有账号进入其原登录/MFA 流程，不因邮箱文本相同自动绑定、重置或合并。
- [ ] 区分邀请未使用、已绑定账号待入驻、已激活和已撤销状态：注册完成后原凭据不能创建第二个账号，只允许经该账号认证恢复同一入驻记录；待入驻期间仍可被撤销，激活与撤销竞争由 Identity 原子状态转换决定。
- [ ] 实现注册与账号验证，维持统一账号目录和密码/锁定策略；现有管理员重置密码保持独立权限，不挪作公开接口。
- [ ] 实现密码恢复，消费挑战与变更凭据在 Identity 事务内完成；撤销适用旧/新会话和刷新族，旧中心 Cookie 不得重新获取有效授权。
- [ ] 实现 MFA 恢复码生成/摘要保存/一次消费；无恢复码的人工恢复使用独立权限、审核和审计，不以邮箱验证自动清除 TOTP。
- [ ] Vue 完成成功、过期、错误和限流流程，检查 CSRF、Origin、日志去敏及无账号枚举。
- [ ] 建立无账号/无会话的邀请注册测试，覆盖邀请目标替换、过期/撤销与注册竞争、同邮箱并发创建、已存在账号登录绑定、重试返回同一注册结果，以及撤销邀请后待入驻记录不能激活。

**验收：** 双库和浏览器验证正常恢复、重放拒绝与旧会话失效；公开注册仍受部署政策控制。

### F05：企业开通、邀请与租户成员管理

**依赖：** F04。**提供：** 可恢复开通流程、正式邀请入口及不依赖配额的成员基础能力。仅在明确未启用配额的基础预设验收；F08b 再接席位限制，限额/SaaS 预设在 F08b 完成前不得启用。不设置 F05 → F08 的前置依赖。

- [ ] 冻结统一账号与租户成员关系的映射，已有账号接受邀请时仅新增合法成员关系，不复制账号；同账号跨企业的角色/部门独立。
- [ ] 将 ProvisionTenant 扩展为持久化步骤：申请、资源建立、所有者绑定、初始化、激活；跨模块写入用幂等命令/事件，各步骤失败可重试或补偿，未完成不对外宣称可用。
- [ ] 接通 F04 的邀请签发/撤销与注册原语，邀请绑定租户、目标邮箱、可选已有账号、角色范围、有效期及一次消费凭据。新用户经邀请注册后继续入驻，已有用户经原认证/MFA 且身份匹配后接受；不允许邀请发起者授予其无权授予的角色。激活成员前重新检查邀请/待入驻状态、发起者授权和企业状态，撤销后不继续激活。
- [ ] 实现成员分页、启停、角色/组织关系管理与邀请撤销；使用受信 Tenant 上下文和各操作独立权限，提供 Vue 页面。
- [ ] 双库覆盖从未注册邮箱到成员激活的全流程、重复接受、邀请撤销与接受竞争、账号已创建但成员未激活时中断恢复、已存在账号加入、跨企业越权和只剩最后一名管理成员。基础模式无配额与强制模式不允许绕过的验收分别留证；后一组由 F08b 关闭。

**验收：** 企业可由其管理员完成成员入驻；开通失败不会形成可登录但未初始化的企业，跨模块不使用共享本地事务。

### F06：所有者交接、成员退出与企业停用

**依赖：** F05、C01/C05。**提供：** 企业生命周期状态和各消费者的撤销行为。

- [ ] 冻结 Active/Suspended/Closing/Closed 状态以及允许的恢复转换；首版停用与逻辑关闭，物理清除需要单独保留政策和执行授权。
- [ ] 所有者交接绑定明确接收人并做强认证、最后一名保护和并发版本校验；验证两个并发交接不会产生零管理者。
- [ ] 成员退出/移除只撤销目标企业授权，不禁用其全局账号或其他企业成员关系；梳理待办改派、后台执行、文件访问、API 凭据和实时连接处理。
- [ ] 企业停用后阻断新业务写入、异步副作用及新授权；定义恢复入口、只读/导出策略和通知，按各模块 Port/事件收敛并提供对账状态。

**验收：** 退出 A 不影响 B；停用后下一次受保护操作拒绝；未完成的跨模块清理可见且不可误报全部完成。

### F07：套餐绑定与功能权益

**依赖：** F00。**提供：** Tenancy 拥有的版本化套餐/租户权益及最小读取 Port。

- [ ] 冻结权益目录、类型、套餐版本与租户绑定生效时间；未知权益一律拒绝，强制模式下缺绑定/无授权默认拒绝。运营人员仅能配置代码登记的权益，不借通用配置创建任意授权表达式。
- [ ] 在 Tenancy 持久化 Compatibility/Shadow/Enforced 阶段及版本：旧部署升级默认 Compatibility，仅已登记的历史功能保持原权限判断；Shadow 比较将来权益决策但不切断原有功能；新 SaaS 预设创建时直接 Enforced，租户在绑定有效套餐前不得激活。阶段由部署/受控管理决定，不能由请求头或数据库异常推导。
- [ ] 在启用前按旧部署实际可用功能生成存量兼容清单，dry-run 展示受影响租户、未绑定项及差异；经运营确认后使用幂等迁移/回填记录建立显式兼容套餐或租户授权，不授予新增功能、不修改用户 RBAC。覆盖回填中断重入及回填期间新增租户，切换前以一致的版本/受控写入门禁确保无遗漏。
- [ ] 只有所有在用租户均有有效绑定、差异已处置且所有 API/Worker 实例支持权益检查后，才能切为 Enforced。混合旧实例期间维持兼容/影子模式；切换后读状态失败拒绝，不能退回 Compatibility。回退默认保留强制策略并前向修复；会绕过权益的旧版本不得直接重新上线。
- [ ] 建立“有 RBAC 无权益”“有权益无 RBAC”“未知权益”“另一实例命中旧值”的失败断言。
- [ ] 实现套餐绑定、升级/降级预览、定时生效和明确租户特例；权限校验独立执行，返回原因可区分未购买、无权限与配额不足。
- [ ] 选择 Workflow 启用作为首个业务消费者，覆盖页面、API、后台启动三个入口；停用权益不删除已有业务数据，存量实例处置按冻结政策执行。
- [ ] 从未绑定套餐的真实旧版本数据升级，验证已有 Workflow/API/后台操作在兼容阶段保持原结果；执行 dry-run、回填、Shadow、Enforced，再验证取消授权、未绑定新租户、状态库故障与旧实例切换拒绝。此双库升级用例在 F07 关闭，F15 复用并扩展为整应用升级。

**验收：** 单改 UI 或角色不能绕过权益；套餐切换有版本和审计，同场景双库及多实例拒绝成立。旧项目升级不意外停用已有功能，强制阶段缺绑定/状态故障不放行，兼容模式不扩张为永久的付费功能旁路。

### F08：席位、存储与调用额度的统一配额协议

**依赖：** 分两部分验收：F08a 只依赖 F07；F08b 依赖 F08a、F05 和 C02。**提供：** 配额协议内核及真实成员/文件消费者；两部分均通过才关闭 F08，协议单测通过不能代替业务接入。

**F08a：协议内核，独立双库验收。** 使用受控消费者夹具，先验证预留/确认/释放/未知结果，不要求成员功能已完成。

- [ ] 用席位和存储定义首批度量：单位、计数范围、周期、上限、预留期限及幂等 OperationId；API/AI 计量作为后续消费者，保留已有 AI 账本所有权，不能双扣。
- [ ] 建立剩余 1 额度的并发竞争、重试重复预留、业务失败释放、成功后确认丢失、超时未知结果、跨周期结算和套餐降级测试。
- [ ] 在 Tenancy 内原子校验并预留；消费者凭据不可跨租户/用途使用。提交业务后确认，失败释放；对未知结果先权威对账，不能简单 TTL 退款。
- [ ] 配额减少到当前用量以下时保留历史数据并拒绝新增；实现用量查询、差异对账、精确权限的修复与审计。

**F08b：真实消费者接入，独立入口验收。** F05 的基础行为保持，限额模式在本切片接通后才能上线。

- [ ] 成员激活前预留席位，成功激活后确认，失败释放；邀请待接受阶段不计活动席位。以同一入驻 OperationId 重试，避免账号创建成功后反复扣席位。企业首次所有者按冻结套餐规则计数，试用/SaaS 开通同样经过该入口。
- [ ] 将文件上传/发布/删除接入存储额度凭据，冻结临时文件与活动文件占用口径；Worker 中断后按文件权威状态对账，不因租约到期直接释放已使用空间。
- [ ] 在切换为限额模式前建立既有活动成员/文件用量基线并处理并发增量，证明无漏计/双计；Mode 与版本由服务端固定，错误/超时不能切到无限额。
- [ ] 用真实邀请接受/成员激活和文件入口执行“余量 1、两个并发操作”、消费后确认失败、重试、移除/删除及套餐降级；分别验证基础预设无配额与 SaaS 强制配额，未知模式拒绝。

**验收：** F08a 证明协议并发不超卖、重复不双扣；F08b 证明真实业务没有遗漏入口，宕机后能够解释每份占用且两个数据库结果一致。限额/SaaS 预设只有 F08b 通过后才可启用，F05 无须等待本任务才能关闭其基础切片。

### F09：业务单据基础样板

2026-10-27 接替开发：新增应用自有附件/报表日记、成对迁移 002、文件响应丢失重绑与持久清理、Reporting/Printing 业务渲染消费方、原会话/对象/文档金额复核及 Vue 交付页面。本地双库 2/2、架构 11/11、OpenAPI 导出 2/2、协议/矩阵 16/16、页面 3/3 通过，样例 AOT 静态分析构建零警告/错误。验证为限定影响集，完整 F09–F11 的本人/部门矩阵、原 CRUD 金额策略、旧导入及真实审批页面不凭本批结果整体关闭。详见[接替开发记录](../../verification/2026-10-27-non-workflow-delivery.md)。

**依赖：** F02、F05、C05；先完成样板基础，不等待全量 SaaS 运营。**提供：** 示例所有者 `demo` 的企业申请单。

- [ ] 在新应用样板内定义主表/明细、申请人、组织归属、金额/数量、状态和版本；通过命名内核生成固定 `demo_*` 表，不写进框架 `fn_*` 业务表。
- [ ] 使用生成器建立列表、详情、编辑、附件与权限；样板依赖官方模块稳定 Port，不反向成为框架必选依赖。
- [ ] 建立 A/B 租户、本人/部门/全租户数据范围、敏感字段、越权附件和并发编辑断言。

**验收：** 开发者可从模板运行完整单据 CRUD，权限与附件所有权贯通列表和详情。

### F10：业务审批、状态回写与通知

**依赖：** F09、C03/C04。**提供：** 单据提交到审批结果的端到端样板。

- [ ] 通过 Workflow 稳定接入契约启动实例，固定业务键、定义版本和提交版本；重复提交只创建一个逻辑流程。
- [ ] 审批结果经可靠事件回写单据，按事件身份与版本去重；乱序、撤销竞争、实例失败和重复完成有明确状态转换。
- [ ] 复用 Notifications 内核产生待办/完成提醒；提醒失败不回滚已提交审批，补投与主业务状态分别展示。
- [ ] 执行 Vue 发起、审批、驳回、改派与通知查看；人工完成页面验收前保持对应待验收标记。

**验收：** 一个真实申请从创建到完成可追踪；停止 Worker 后恢复不会重复产生业务副作用。

### F11：导入、报表与打印接入样板

**依赖：** F09、C02/C05。**提供：** 现有三个模块的受控业务接入范例。

- [ ] 复用 ImportExport 的静态 Schema/任务机制，提供模板、预校验、逐行错误与幂等写入策略；禁止导入绕过单据领域校验。
- [ ] 复用 Reporting/Printing 生成有权限的数据报表和打印视图；查询与结果下载分别验证租户、会话、数据范围和字段权限。
- [ ] 测试排队后撤权、取消、租约失效、Worker 崩溃、结果文件上传后提交失败和重试；限制输入文件、解压、结果大小及执行预算。
- [ ] Vue 展示进度、部分失败、错误文件、取消和结果过期；打印内容按现有净化与隔离策略执行，服务端生成文本遵循语言偏好。

**验收：** 导入/导出/打印不会扩大列表权限；任务恢复和文件清理可证明，未跑真实 Worker 不称恢复通过。

### F12：订阅、试用与支付驱动权益

2026-10-27 接替开发：已用真实 `ITenantSubscriptionBillingPort` 替换测试/空履约占位，实现版本报价、冻结账单、幂等下单、资金事实事务 Outbox、Payments 恢复 Worker、Tenancy 原子授权/配额、退款来源撤销与到期整理，以及 Vue 报价/付款/履约入口。本地定向单元 48/48、双库履约与席位回归 4/4、架构 10/10、OpenAPI 双库导出 2/2、契约 27/27、组件 6/6、浏览器 4/4 均通过，Tenancy/Payments AOT 静态构建零警告/错误。真实商户扣款、自动续费、实际 Native 发布及整套预设验收不在本批验证范围；标准前端启动仍受并行 Workflow/VForm 运行时摘要未同步阻塞。详见[接替开发记录](../../verification/2026-10-27-non-workflow-delivery.md)。下列历史里程碑不凭本批局部结果整体勾选。

**依赖：** F06、F08、现有 Payments 安全修复/渠道验收。**提供：** Tenancy 订阅生命周期；Payments 仍拥有资金事实。

- [ ] 冻结 Trial/Active/PastDue/Cancelled/Expired 状态、期限与宽限；取消默认期末生效，首版升级/降级使用明确生效日，不引入自动按比例复杂计费。
- [ ] 先实现测试付款或人工登记的受审计订阅闭环，再接已有支付 Provider；真实自动扣款只在选定渠道能力、用户授权和回调验收完成后启用。
- [ ] 订阅消费已验证支付结果，绑定租户、订单、金额/币种和套餐版本；建立重复/乱序回调、跨商户回调、退款、超时未知结果与对账测试。
- [ ] 支付成功但权益更新失败时保持待履约、可靠重试和对账；不把租户全局禁用作为所有欠费情况的默认处理，按冻结政策限制新增并保留必要恢复入口。
- [ ] Vue 提供试用期限、当前套餐、变更预览、续期/取消及失败解释；到期任务多实例只产生一次逻辑状态转换。

**验收：** 订阅、资金事实和权益可逐笔对应；本地成功不能替代真实渠道认证，不自动承诺发票/税务能力。

### F13：首个可靠 Webhook 事件

**依赖：** F00、F10、C05；选择申请完成事件作为首个消费者。**提供：** 可选 Webhooks 模块及可审计投递。

- [ ] 登记固定事件类型、版本和最小载荷，业务所有者发布集成事件；Webhooks 拥有订阅、投递/尝试/回执，不读取业务模块表。
- [ ] 注册精确目标地址与签名密钥引用，执行 DNS/重定向/目标网络限制、出站超时和响应大小限制；密钥不回显，轮换有受控过渡。
- [ ] 以稳定 EventId/DeliveryId、时间戳、KeyId 对原始请求体签名；接收样例验证签名、时间窗及重复投递，兼容重试/重放时的稳定业务身份。
- [ ] Worker 使用租约、退避、最大尝试和人工重放，网络调用位于事务外；“已接收”与“业务已处理”不混称，超时未知结果允许重复投递但要求消费者幂等。

**验收：** 双库/双实例并发、SSRF、签名篡改、撤销订阅和重复消费场景通过；不得用生产客户地址进行测试发送。

### F14：开放 API 与 SDK 接入包

**依赖：** F13、C01/C05。**提供：** 外部应用可复现的认证、调用和事件接入样例。

- [ ] 复用 API Key/OpenAccess/OIDC，按交互用户与机器调用选择明确认证方式；限制客户端允许的 API 范围、租户与操作，不能把 Host 全权限默认授给外部应用。
- [ ] 用 OpenAPI 生成 TypeScript 接入包，冻结版本、分页、ProblemDetails、文件/流和重试语义；仅幂等或有业务幂等键的操作允许自动重试。
- [ ] 提供凭据配置/轮换、限流响应、错误定位、Webhook 验签和幂等接收教程；样例使用环境秘密引用。
- [ ] 在干净消费项目安装本地打包产物，验证合法请求、凭据撤销、跨租户拒绝与一条事件；公共包发布只在单独授权后执行。

**验收：** 样例无需读取 Full.NET 内部实现即可接入，已发布契约变更可检测。

### F15：应用版本升级与恢复演练

**依赖：** F02、C07；先覆盖基础预设，再覆盖企业/SaaS 增量。**提供：** 应用开发者可执行的升级路径。

- [ ] 定义支持的起始/目标框架版本、数据库版本、模块组合与兼容窗口；以真实已发布或带固定提交的候选基线创建升级夹具，不虚构历史发行版。
- [ ] 沿用 F01 的版本化源码包，以旧 manifest 为共同基线比较本地内容与新分发包：未改的受管文件可计划更新，框架本地定制或应用拥有文件只生成合并建议与冲突报告；先预览/校验再提交受管更新，失败不留下半更新状态。同步框架依赖锁文件、迁移/资源和工具兼容矩阵，不静默升级到最新版本。
- [ ] 夹具包含人工定制代码、旧数据、活动会话、排队任务与文件；升级只改受管资产，冲突生成报告，禁止自动覆盖人工实现。
- [ ] 演练 Expand/Migrate/Contract、混合版本运行、迁移中断重入；破坏性收缩前满足旧消费者退役门禁。数据库不可逆变化使用前向修复或备份恢复，不宣称简单降版本可回滚。
- [ ] 复用 F07 的存量权益回填/强制切换用例及 F08b 的用量基线演练；恢复必须保留或重建正确的权益阶段/版本，不能把 Enforced 租户恢复成兼容模式绕过订阅。
- [ ] 写明数据库、对象文件、Data Protection/签名密钥、运行状态的备份恢复顺序和秘密保护；在隔离环境实际恢复，测量数据丢失与恢复时间并对照冻结 RPO/RTO。

**验收：** 双库升级与恢复证据可定位到版本/产物；回退后权限、会话与任务不被意外复活。

### F16：按预设完成发布验收

**依赖：** 基础预设需 F01/F02/F15 和所含模块 C 门禁；企业预设追加 F03—F06/F09—F11/C01；SaaS 预设追加 F07/F08/F12；开放集成作为选装需 F13/F14。AI 选装另需 C06。

- [ ] 固定预设及依赖闭包，生成发布清单、版本、许可、配置说明与已知限制；默认关闭未验收外部渠道和高风险运维入口。
- [ ] 在 GitHub Actions 对对应提交运行双库、Linux Native、真实栈和受影响治理门禁；人工验收企业核心流程、可访问性和错误恢复。
- [ ] 按现有 Helm 基线验证 API/Worker/Migrator 顺序、多实例、密钥共享、排空、升级、故障接管和恢复；容量按独立容量计划验证。
- [ ] 只有全部适用门禁具备证据才提升该预设状态；文档、构建、运行、生产认证分别报告，不把一个预设的成功外推到所有模块组合。

**验收：** 发布候选可以从干净环境复现，未验证容量继续标 `Capacity-not-verified`；不存在以精简预设名义隐藏必选模块的失败。

## 6. 已有模块的收口队列

本节只管理交付依赖和验收，不复制原计划的实现勾选。F00 将实际未关闭项补入对应原计划；原任务已完成时直接引用证据。

| 编号 | 所有者与既有执行入口 | 必须收口的重点 | 对本计划的门禁 |
| --- | --- | --- | --- |
| C01 OIDC/SSO | [OIDC 唯一计划](2026-09-13-identity-oidc-sso-evolution.md) | 两客户端、新旧会话、管理入口强制下线、切租户保留授权、工具/后台消费者、双库 Native 与 Vue；逐项执行 V01—V24 | F04/F06/F14 消费适用会话能力；企业 SSO 发布必须全链路通过 |
| C02 导入/报表/打印/文件 | [全项目修复计划](2026-09-07-full-project-review-fixes.md) R02/R03/R07/R16/R17 | SQL 租户谓词、结果文件所有权、输出预算、打印净化、取消/租约与崩溃恢复；真实 Worker 与下载再授权 | F11 与含数据交付的预设不得仅凭 Unit 通过关闭 |
| C03 Workflow | [节点整改唯一实施计划](2026-10-24-workflow-node-completion.md)、[当前跨机续办记录](2026-10-28-workflow-cross-machine-handoff.md) | 只推进专项最新明确未关闭差集；协作执行、Files、实时授权及物理消息路径后续已有分批证据，不按早期待办重复开发 | F10 消费稳定业务接入契约；本计划只负责业务样板接入，不复制专项任务 |
| C04 Notifications | [平台扩展](2026-08-30-notifications-platform-extension.md)、[SMTP](2026-08-31-notifications-smtp-provider.md)、[收件端点](2026-09-01-notifications-recipient-endpoint-management.md)、全项目修复 R08 | 收件端点验证、验证码用途/并发消费、渠道认证、租约、重试和回执；逐渠道真实投递与多语言 | F03/F05/F10 的通知不可用时不伪报发送；先收口一个邮件渠道 |
| C05 权限全链路 | [全项目修复](2026-09-07-full-project-review-fixes.md)、[字段投影计划](2026-08-01-field-projection-authorization.md)、OIDC T02/T03 | 同一身份跨列表/详情/导出/报表/附件/后台/AI 的数据与字段权限；排队后撤权、切租户、停用及未知绑定拒绝 | 所有新能力共同门禁；F00 登记真实入口矩阵，各切片补回归 |
| C06 AI/Agent | [AI 唯一计划](2026-09-08-ai-agentic-web-alignment.md) | 当前会话授权、预算/未知费用、审批意图绑定、Checkpoint/副作用幂等、取消/恢复、协议互操作和 Native | AI 为选装；先交付一个已授权业务助手，不阻塞无 AI 核心预设，不重复建设全局配额账本 |
| C07 生产运行 | [多实例实施计划](2026-08-01-fullnet-high-concurrency-multi-instance-implementation.md)、[Worker 原生计划](2026-08-29-worker-native-aot-phase0.md)及后续 Phase、现有运维文档 | 双库运行、所选模块原生闭包、滚动升级、密钥/对象存储共享、备份恢复、告警和故障接管；容量独立认证 | F15/F16；先冻结故障/RPO/RTO 目标再测量，未达标不反向放宽 |

## 7. 验证执行、阶段出口与记录格式

执行位置和风险分层唯一依据是[开发质量 §11](../../../rules/development-quality.md#11-测试与验证)。每次实施先读取相关规则/Skill；中高风险行为建立真正可失败的测试，数据库场景双库成对，公共契约和源生成路径覆盖 Native AOT。

```powershell
git branch --show-current
git status --short
git rev-parse HEAD
pnpm test:task:start -- foundation-f01
pnpm test:integration:affected:plan -- --snapshot foundation-f01 --phase inner
```

上例 `foundation-f01` 是 F01 的建议任务 ID，其余任务使用自己的稳定 ID；文档任务不创建代码快照。先审查影响集，再运行该集的实际命令。新增选择器要先登记矩阵并证明非零发现，不能把本计划里的测试目标当成已经存在的命令。

验证入口按实际变更和测试矩阵选择精确用例/过滤器；上述历史命令和下表阶段出口不构成整模块或全量测试授权。规定范围本地实际通过即可验收，CI 为可选补充。SQL、事务或迁移变化验证同场景双库，页面和 Native 路径按实际影响覆盖；未变化且输入匹配的有效证据直接复用。仅当用户明确要求全量测试时才执行全量，不自动提交、推送或触发 CI 扩围。纯计划变更只检查结构、链接和差异。

| 阶段 | 完成出口 | 不允许替代的证据 |
| --- | --- | --- |
| W0 基线 | F00，受影响已有安全修复无未解释阻塞 | 目录存在、文档状态、历史测试 |
| W1 快速建项目 | F01/F02，空目录到真实 CRUD | 仅原仓库 build |
| W2 企业使用 | F03—F06，注册/恢复/邀请/交接/退出，C01 所需会话链路 | 仅管理员后台维护数据 |
| W3 权益与业务 | F07—F11，配额并发、单据审批通知及数据交付 | 仅按钮隐藏、模拟付款、Mock UI |
| W4 SaaS/集成 | F12—F14，支付履约对账和外部接入 | 仅收到 HTTP 200 或模拟渠道成功 |
| W5 发布 | F15/F16，按选定预设升级恢复与运行验收 | Unit/JIT 替代原生、工具替代生产容量 |

每个任务记录：`任务 ID / 实际提交 / 产物与契约 / 测试入口及结果 / 双库与原生证据 / 页面验收 / 未验证项 / 下一消费者`。不预估虚假的统一人天；F00 核对增量后按纵向切片给出估算，每个切片遵循仓库 slice 节奏，失败则先缩小范围或修复，不降低安全门禁。

停止当前切片的条件：数据所有权冲突、发现权限/租户绕过、双库行为不一致、原生闭包不可支持、依赖许可不满足、恢复路径无法保留数据。保持原入口可用，记录决策与修复，不通过扩大全局豁免继续。无此阻塞时按已批准范围推进，不为例行实现选择重复请求确认。

Vue 再生成所有权保护增量（基线 `4fc046e7`）：Host 原先直接覆盖页面、页面模型和客户端，绕过生成清单。执行顺序为三类人工文件失败回归→复用 GenerationWorkspaceStore 捕获/规划/写入→验证受管升级与其他产物保留→本地聚焦验证与独立复审。新增 12 项回归；修改前 9 项为 8 失败/1 通过，修复后纠正测试夹具必须存在工作区根目录，最终 CodeGeneration/Realtime 456 项通过、无跳过。现在人工未受管文件或已拥有文件的漂移会拒绝写入，保持其他实体和生成器的清单条目及其摘要，不允许意外删除或重新接管漂移。Host 将受控冲突返回失败，取消继续传播。

独立复审发现工作区通用写盘器逐文件提交后的 Create/Update 尚无完整中途失败恢复；本增量仅收口所有权及写入前冲突保护，不声称 Vue 批次或整条 Host 接入原子。后续必须使用 ApplyForTestingAsync 的 afterArtifactCommit 注入建立失败回归，覆盖第一文件提交后 I/O 故障、后续目标并发修改和清单提交失败，再补齐恢复证据。完整 F02 不关闭。

本地新鲜验证：`pnpm test:dotnet:unit -- --selection code-generation-realtime` 456/456，`pnpm test:aot:analyzers` 0 警告/0 错误，`pnpm test:dotnet:architecture -- --selection api-native-aot` 73/73，`pnpm test:governance` 55/55，均无跳过。`pnpm test:integration:partitions` 仅发现并校验 1075 项，无遗漏/重复，不能算完整 Integration 通过；影响集规划目标为 CodeGeneration、integration-matrix。独立复审在所有权/前置冲突范围无其他阻断，明确保留上述恢复缺口。远端双库与 Native 状态须绑定此增量提交 SHA。

写盘恢复增量执行计划（基线 `b0501004`）：复用现有故障注入入口，先覆盖 Create/Update 在首个提交后、清单前失败，以及后续目标/已提交目标人工并发修改。实现范围为 GenerationWorkspaceStore 的写入提交与恢复边界，使用同卷无覆盖声明、旧内容备份和落盘阶段证据；失败逆序恢复，已变更目标不覆盖，无法恢复保留证据并阻断 Capture/Apply。清单一旦提交不回退；未完成或进程中断只失败关闭等待审查，不自动恢复或宣称全 Host 原子。补充成功清理、重试、取消和既有删除/清单回归，随后串行聚焦 Unit、AOT、架构、治理、分片与独立审查；双库/Native 重验证进入绑定提交的 Actions。

恢复实现与证据：Create/Update 先落盘 pending（动作、路径、旧/新摘要），Update 同卷无覆盖声明旧文件并复验摘要；新文件只进入空目录项。清单提交前故障逆序恢复写入，并继续恢复其他写入及删除；人工并发改动、活跃写句柄或非法 UTF-8 无覆盖移回原位，旧备份/阶段证据保留，Capture/CapturePaths/ReadManifestOrEmpty/Apply 拒绝未完成恢复。备份清理通过无覆盖声明与持有拒绝写入的读取句柄校验，清单提交后只清理，不回退已提交状态。

新增 23 项恢复回归；首次根目录文件夹具触发已有 EnsureParentDirectory 根路径拒绝，修正为 backend 目录后正确 RED 为 10 项中 9 失败/1 通过。审查追加的备份清理/清单入口三项先失败；活跃句柄及非法编码原位恢复四项先全部失败。最终 CodeGeneration/Realtime 479/479、0 跳过。此前根目录产物路径误拒绝另列 F02 后续缺陷，本增量不扩张修复。进程终止仅留下证据并失败关闭，尚无自动恢复或杀进程验收；整条 Host 仍分阶段，完整 F02 不关闭。

最终本地命令：`pnpm test:dotnet:unit -- --selection code-generation-realtime` 479/479，`pnpm test:aot:analyzers` 0 警告/0 错误，`pnpm test:dotnet:architecture -- --selection api-native-aot` 73/73，`pnpm test:governance` 55/55，无跳过；`pnpm test:integration:partitions` 校验 1075 项无遗漏/重复，仅为发现和分片证据。独立复审三项问题经失败回归与修复后无剩余阻断，工作区/提交门禁按当前 SHA 校验，远端双库与 Native 尚待推送后验收。

根目录产物修复增量（基线 `732a719f`）：此前测试夹具揭示 EnsureParentDirectory 对根目录文件误把合法父目录 fullRoot 判为逃逸。新增 9 项回归，正确 RED 为 3 失败/6 通过；现只在合法单段产物的父目录检查允许 fullRoot，并再次拒绝根 reparse，实际文件 Resolve/EnsureContained 与嵌套目录规则未放宽。真实 Store 验证根文件创建、更新、重复幂等、清单前故障恢复与重试，同时覆盖未受管人工文件、四种非法路径及真实根链接拒绝。CodeGeneration/Realtime 488/488、0 跳过；独立复审无阻断。该根路径缺陷子项已修复，完整 F02 仍需独立生成业务全链与进程中断验收。

本地新鲜验证：`pnpm test:dotnet:unit -- --selection code-generation-realtime` 488/488，`pnpm test:aot:analyzers` 0 警告/0 错误，`pnpm test:dotnet:architecture -- --selection api-native-aot` 73/73，`pnpm test:governance` 55/55，无跳过；`pnpm test:integration:partitions` 发现并校验 1075 项无遗漏/重复，不算完整 Integration 通过。影响集为 CodeGeneration 与 integration-matrix；远端验收须绑定此增量新 SHA。

CLI 接入实现收口计划（基线 `ec00c868`）：CLI 仍存在九份共享后端/模块入口/Composition 编辑、编译与投影副本，现有 Unit 直接引用 CLI 副本。对比确认七份除命名空间、可见性与注释外主体一致；模块投影另有共享编译探针可见性/说明属性差异，入口编辑器共享词法器另供授权标记复用。先运行既有 CLI/生成基线，再移除九份内部副本，让 CLI 命令绑定现有共享公共实现；迁移四个既有编辑/投影测试文件引用，测试数和公共命令不扩张。通过既有聚焦 Unit、编译、治理、分片及独立复审核对实际消费者，不把纯合并伪装为新行为修复。完整独立应用与真实编译 Integration 仍交由绑定提交的 Actions 验收。

CLI 收口结果：九份内部副本已移除（约 2,900 行），四组既有测试改为验证共享实现，CLI 保持原命令解析与结果输出。`pnpm test:dotnet:unit -- --selection code-generation-realtime --no-build` 基线 488/488；收口后 `pnpm test:dotnet:unit -- --selection code-generation-realtime` 488/488，Release 构建 0 警告/0 错误，`pnpm test:dotnet:architecture -- --selection api-native-aot` 73/73，`pnpm test:governance` 55/55，均无跳过。`pnpm test:integration:partitions` 1075 项仅为发现与分片校验，无遗漏/重复；影响集目标 CodeGeneration。未改变共享 API/Worker 可达实现，本轮未重跑本地 AOT 分析；真实候选编译、双库独立应用、代表性样例和 Native 验收等待新提交 Actions，不关闭 F02。

独立复审无阻断：删除后 CLI 绑定共享公共类型，未发现遗漏消费者；七份主体相同，另两份的共享差异不会改变默认命令语义；四份测试只迁移引用、未削弱断言。需在新 SHA 的 CodeGeneration affected Integration 验证 ModuleIntegrationBackendApplyTests 三种 CLI apply 的候选编译、幂等与冲突，以及 ModuleIntegrationCompilationTests 和独立应用模板门禁。未用本地聚焦通过替代这些运行证据。

CLI 只读规划路径边界增量（基线 `97ae07ee`）：检查授权接入前置链时发现 ModuleIntegrationPlanCommand 用 Path.Combine/File.Exists 直接读取目标，未复用工作区的路径保护。新增六项真实 CLI 回归（仓库根/父目录/文件/悬空链接、大小写别名、目录占用）及一项所有目标缺失时的直接命令取消回归；RED 7 项全部失败。现使用既有友元可访问的 GenerationWorkspacePath.NormalizeRoot/Resolve，目录占用返回受控冲突，开始及逐路径检查取消；未增加公共 API，UTF-8/BOM、合法缺失目标的 Blocked 规划语义与只读性保留。CodeGeneration/Realtime 495/495、无跳过；授权 CLI 提交、应用 Migrator 与真实独立生成业务链仍未验收，不关闭 F02。

本增量交付核验：Release 构建 0 警告/错误；API Native AOT 架构选择 73/73、治理 55/55，均无跳过；Integration 分片发现 1075 项无遗漏/重复（不是完整集成测试通过），影响计划命中 CodeGeneration 与 integration-matrix。独立复审无阻断，git diff --check 通过。另确认后端 Apply 与模块入口/Composition 的部分路径解析仍使用 Path.Combine，需要下一增量按真实写入链建立失败验证并收口；本次只读规划修复不代表整条接入链路径安全或原子性。

接入命令静态路径边界增量（基线 2ed32ff5）：沿后端 Apply、模块入口、Composition 与模块编译调用链确认 Path.Combine/GetFullPath 只保证字符串路径，不能拒绝原仓库根/父目录/文件链接、悬空链接、大小写别名或目录占用。计划为同一切片先建立真实 CLI 失败回归，再复用 GenerationWorkspacePath.NormalizeRoot/Resolve 的现有边界，并以内部 ResolveFile 保留普通缺失目标的原有前置失败、拒绝目录占用；最后执行相关 Unit、AOT 分析、Architecture、治理、分片及独立复审，提交推送交由 Actions 执行重型验收。四个入口新增 24 项目标项目回归，模块入口/Composition 项目/Catalog 新增 12 项手写目标回归；RED 42 项中新增 36 项全部失败、已有规划 6 项通过，无跳过（其中后端旧实现进入临时项目 MSBuild）。修复后相关 Unit 531/531，Release 0 警告/错误，治理 55/55，Integration 分片发现 1075 项无遗漏/重复，影响计划命中 CodeGeneration 与 integration-matrix。此增量只保护静态入口目标，不证明编译后路径替换、锁文件链接、MSBuild 传递引用或整链原子性；这些边界及完整 F02 验收仍需后续收口。

本增量最终核验：pnpm test:aot:analyzers 退出 0、0 警告/错误；API Native AOT 架构选择 73/73、无跳过。独立复审确认静态目标在读取/编译前受控拒绝、普通缺失前置语义保留，内部辅助方法未扩大公共 API，无阻断。git diff --check 通过；Linux Native 与真实独立应用/代表性样例重型验收仍交由新提交的 Actions，不使用本地选择器结果代替远端通过。

候选编译后提交检查点增量（基线 1022120c）：确认模块入口与 Composition 提交沿用缓存绝对路径并直接打开锁文件，内容相同的链接替换可越过旧内容复核。按缺陷定位、测试驱动和计划技能组织为单一切片：直接测试真实提交阶段（通过既有友元访问 internal 方法，无反射、不启动 MSBuild、不增加公共 API），复用原仓库路径保护，再执行 Unit/AOT/Architecture/治理及独立复审、提交推送触发远端验收。新增 37 项：16 项候选编译后的目标文件/父目录链接、别名、目录替换，12 项三个锁位置的文件链接/悬空链接/别名/目录占用，3 项持锁冲突、4 项手写内容漂移、2 项正常提交及暂存清理。有效 RED 为 28 失败/9 通过，无跳过；首轮正常用例对未使用的 Composition 锁删除断言错误，修正后重新确认 RED，不将该测试错误计作缺陷。修复后相关 Unit 568/568，Release 0 警告/错误。两个提交函数改为 internal，模块入口保留原 repositoryRoot；RevalidateFile 将缓存绝对路径重新约束到原根，提交开始/锁内读取/提交 Move 前复核目标，锁先安全解析再创建父目录并重新解析；Composition 回滚 Move 也先复核项目路径。仅证明这些检查点，不宣称所有 await 间 TOCTOU 已消除、操作系统原子读写、MSBuild 传递引用或整条 Host 原子性，完整 F02 与突然进程终止恢复仍未关闭。

本增量最终核验：pnpm test:aot:analyzers 退出 0、0 警告/错误；API Native AOT 架构选择 73/73、治理 55/55，均无跳过；Integration 分片发现 1075 项无遗漏/重复（不是完整集成通过），影响计划命中 CodeGeneration 与 integration-matrix。独立复审无阻断；新增回滚复核仍进入既有 IOException 恢复处理并保留恢复副本，但本轮仅通过源码审查确认该分支，37 项测试未注入首次项目 Move 后的 Catalog 失败/回滚路径替换，因此不能作为整链故障恢复证据。git diff --check 通过，远端重型验收等待新 SHA 的 Actions。

Composition 首次写入后故障恢复增量（基线 4ea8a1aa）：上一提交主 CI 36249084881、API Native 36249084779、Worker Native 36249084804 均成功，仅作为前置实现证据。根据上轮复审的动态验收边界，给 internal CommitAsync 增加默认空的首次项目 Move 后故障注入；公共 Apply 不传递该回调，无新增公共 API/反射。新增 9 项真实提交阶段测试：普通故障与 Catalog 目录占用能恢复原项目；人工项目内容/删除/非法 UTF-8/链接、恢复副本漂移/链接须保留材料；项目首次写入后的人工 Catalog 内容不得覆盖。有效 RED 6 失败/3 通过，无跳过，确认旧回滚虽复核路径却无内容所有权检查，且漂移恢复副本仍会被 Move 覆盖使用。现 Catalog Move 前再核对原内容；回滚先核对目标仍为本次 desired、恢复副本仍为 original，非法编码纳入恢复冲突处理并保留材料。相关 Unit 577/577、无跳过，Release 0 警告/错误。仅关闭这些确定性故障注入场景；检查与 Move 之间 TOCTOU、恢复材料自动登记/阻断重试、恢复副本删除/父目录置换、进程中断与整条 Host 原子性仍未验收，不关闭完整 F02。

本增量最终核验：pnpm test:aot:analyzers 退出 0、0 警告/错误；API Native AOT 架构选择 73/73、治理 55/55，均无跳过；Integration 分片发现 1075 项无遗漏/重复（不是完整集成通过），影响计划命中 CodeGeneration 与 integration-matrix。独立复审无阻断，确认首次项目 Move 后真实执行恢复分支、CancellationToken.None 防止取消打断提交补偿，恢复材料不进入 finally 删除；检查点以外仍未认证。git diff --check 通过；上一基线的三条 Actions 成功不替代新 SHA 的远端验收。

Composition 恢复登记与重试阻断增量（基线 `915d08e0b5b7aa19ea90ca3daaa1fff66765d0fe`）：先建立恢复登记及遗留材料的失败回归，再补内部登记/入口保护和登记失败组合验证，最后串行 Unit、AOT、Architecture 与独立复审。恢复失败写入工作区根 `.fullnet/codegeneration-composition-recovery.pending`，内部 v1 文本记录项目、Catalog、恢复副本的根内相对路径与可信原内容摘要；不读取或信任已漂移的恢复副本，不增加公共 DTO/API。公共 Apply 在前置读取前、内部提交开始及持锁后拒绝待审查登记；未知、残缺或非法编码登记同样阻断。项目/Catalog 两个目标目录的旧 `.fullnet-composition-*.tmp` 条目也阻断重试，涵盖大小写别名、目录、链接及悬空链接，不读取其内容。旧副本被删除或变为悬空链接仍进入恢复失败登记；登记 I/O 失败时同时保留尚存 Catalog 候选作为阻断材料，不自动恢复或删除。

新增 23 项测试，涵盖六种登记占用、两个独立目标目录的十二种遗留材料、真实 CLI 在缺少前置条件时优先阻断、旧副本删除/悬空及登记失败组合。有效 RED 28 项全部失败（包含六项既有恢复场景的新登记断言）；追加的“旧副本删除且登记创建前失败”回归 1 项先失败，修复后确认恢复正常 Catalog 目标仍被遗留候选阻断，避免目录占用造成假通过。曾纠正 Windows 悬空链接 File.Exists 断言；临时还原源码验证 RED 后，复制保留旧时间戳导致 MSBuild 复用旧 DLL，核对恢复源码并更新时间戳后重新构建，未把缓存结果计作通过。最终相关 Unit 600/600、0 失败/跳过，Release 0 警告/错误；AOT 分析退出 0、0 警告/错误；治理 55/55；分片发现校验 1075 项，无遗漏/重复，影响集 CodeGeneration 与 integration-matrix。独立复审无阻断。没有 Catalog 候选、材料被外部全部删除、恢复父目录置换、检查与 Move 间 TOCTOU、进程终止及自动恢复仍未验收，完整 F02 不关闭；新 SHA 双库/独立应用/Native 仍需 Actions 证据。

本增量架构最终检查：pnpm test:dotnet:architecture -- --selection api-native-aot 73/73、0 失败/跳过，构建 0 警告/错误；git diff --check 通过。

Host 整链恢复前置门禁增量（基线 `e2ff03256f83807e31b742ed7c6b6e097152a0a2`）：沿调用顺序确认 Composition 的待审查检查位于 Backend/Entry 之后，已有恢复现场仍可能先进入前两阶段。执行计划为建立失败回归→复用已有检查提前阻断→相关 Unit/AOT/Architecture/治理与独立复审。新增三项真实公共 Host Apply 回归，覆盖根登记、项目目录及独立 Catalog 目录遗留材料；故意缺失模块项目，使旧实现返回后端前置错误，RED 3 项全部失败，无跳过。现官方模块拒绝规则后、Backend 前 NormalizeRoot 并调用 RejectPending，受控路径/恢复冲突转换为 Host Failure；公共参数、DTO 与后续 Composition 锁内复核不变。回归确认返回待审查且文件集合/内容不变，不触发 MSBuild。相关 Unit 603/603、0 失败/跳过，Release 0 警告/错误；治理 55/55；分片发现校验 1075 项无遗漏/重复，影响集 CodeGeneration 与 integration-matrix。此增量只证明调用时已有现场会先阻断，不证明整链原子性或前置检查后并发产生现场，也不收口授权贡献者写盘、Migrator 或完整 F02。

本增量最终核验：pnpm test:aot:analyzers 退出 0、0 警告/错误；pnpm test:dotnet:architecture -- --selection api-native-aot 73/73、0 失败/跳过，构建 0 警告/错误；独立复审无阻断，git diff --check 通过。远端真实应用/双库样例/Native 仍须绑定新 SHA，前置提交运行结果不替代当前验收。

Host 授权目标静态路径增量执行计划（基线 `43772a39353303fb851f3f4d38cce35e56f7b3a5`）：基线主 CI 36253597612、API Native 36253597672、Worker Native 36253597721 全部成功，独立生成应用真实栈与双库代表性样例成功；未运行的条件作业不计为通过，不替代新提交。授权贡献者阶段仍使用普通 Path.Combine/File.ReadAllText/File.WriteAllText，且直到后端/入口/Composition/Vue 之后才发现显式目标不存在。先用真实 Host 的六种不安全或缺失目标建立失败证据，再复用工作区路径保护，于首步前验证、授权读写检查点再次解析；最后相关 Unit/AOT/Architecture/治理及独立复审。此切片不开放 CLI 尚未支持的授权目标字段，不证明授权写盘锁、内容并发保护、失败恢复、检查点之间 TOCTOU 或完整 F02。

本增量失败/通过证据：六项真实 Host 回归 RED 全部因旧实现优先返回“模块项目不存在”失败，0 通过/跳过；修复后相关 Unit 609/609、0 失败/跳过，Release 0 警告/错误。授权目标静态错误在 Backend 前转换为既有 Host Failure，读取前与写入前复用 ResolveFile/存在性检查；不新增公共 API/DTO，不吞掉取消或一般 I/O 异常。测试直接验证首步静态目标拒绝、外部文件摘要与手写 Entry/Project/Catalog 保持不变；最终授权阶段检查点目前为源码审查，不当作首次读写后并发替换或失败恢复的动态证据。治理 55/55；分片发现校验 1075 项无遗漏/重复，影响集 CodeGeneration 与 integration-matrix。

本增量最终核验：pnpm test:aot:analyzers 退出 0、0 警告/错误；pnpm test:dotnet:architecture -- --selection api-native-aot 73/73、0 失败/跳过，构建 0 警告/错误；独立复审无阻断，git diff --check 通过。当前新增行为的远端双库/独立应用/Native 验收等待新 SHA。

授权贡献者单文件提交增量计划（基线 `e70db66ab952a1ca04a1f5e37c08c45ccc0e1025`，任务快照 `f02-authorization-commit`）：开工发现两份 Markdown 治理脚本/测试无关改动，保留且不纳入本提交。将原授权写入机械提取为 internal 提交阶段（公共 Host 默认不传故障回调），用真实提交测试覆盖读取后人工漂移、暂存后漂移、四种锁占用/链接、持锁、暂存失败及成功清理；建立失败证据后加入独立授权排他锁、原内容字节复核、同目录 CreateNew/Flush 暂存与 Move 前复核，避免直接截断写入。只处理单文件提交，不扩大为全 Host 事务、自动进程恢复或新 CLI 授权契约。串行相关 Unit/AOT/Architecture，治理/分片/快照影响集与独立复审，完成后仅提交本任务文件，远端绑定新 SHA。

本增量实现与证据：新增 12 项真实单文件提交测试。机械提取后的原直接写入基线 RED 9 项为 7 失败/2 通过；审查追加暂存漂移的提交/故障清理两项 RED 全部失败，写句柄清理回归 RED 1 项失败。修复后最终相关 Unit 621/621、0 失败/跳过，Release 0 警告/错误。授权锁保持排他生命周期；原内容在锁内和暂存后按 UTF-8 字节复核，暂存 CreateNew/Flush 完成后经路径/内容复核再 Move。清理仅删除仍匹配本次内容的材料；漂移或清理 I/O 失败保留材料，受控冲突保留原提交与清理异常。未完成暂存保留且传播原 I/O/取消，不将半成品误报人工漂移；部分写入取消尚无动态故障注入，不能报告为通过。暂存副本/清理的检查与 Move/Delete 间 TOCTOU、读取租约、残留材料自动登记/重试门禁和进程终止恢复仍未关闭，完整 F02 不关闭。治理 55/55；分片发现校验 1075 项无遗漏/重复，快照影响集命中 CodeGeneration 与 integration-matrix。

执行期间，其他窗口将 Markdown 治理改动提交为 `88bb482b2f8d7ae4fa6f65ddd784da710a06c850`，本任务保留该提交，仅修改和提交本任务四文件。首次清理回归误与 AOT 重叠启动后已中止，不计为验证证据；最终 RED 与后续验证串行完成。

本增量最终核验：最终源码 pnpm test:aot:analyzers 退出 0、0 警告/错误；pnpm test:dotnet:architecture -- --selection api-native-aot 73/73、0 失败/跳过，构建 0 警告/错误。独立复审确认已知两项 P2 收口，无新增阻断；git diff --check 通过。只将新 SHA 的远端证据计为本轮真实应用/双库样例/Native 验收。

授权暂存残留重试门禁计划（基线 `b3d532b34e8c9a836ec3a6dd37546f0f0d07288b`）：先新增真实 Host/直接提交入口的六类残留条目失败回归，以及实际暂存漂移后的重试；随后只在授权目标父目录枚举 `.fullnet-authorization-*.tmp` 条目，大小写不敏感、不读取或跟随残留链接，存在即待人工审查。Host 首步、直接提交开始与持锁后检查，避免扫描自身本次暂存；现有成功与干净故障清理语义保留。相关 Unit/AOT/Architecture、治理/分片/影响集及独立复审后提交推送。本轮不新增登记协议或自动恢复，不证明材料全部被外部删除、检查之后并发现场、OS TOCTOU 或进程终止恢复，完整 F02 保持未关闭。

本增量实现与证据：新增 13 项回归 RED 全部失败，0 通过/跳过；修复后相关 Unit 634/634、0 失败/跳过，Release 0 警告/错误。授权目标父目录六类 plain/文件链接/悬空链接/大小写别名/目录/非法 UTF-8 残留分别在真实 Host 与直接 Commit 入口受控拒绝；Host 故意缺失模块项目，确认待审查优先于后端错误。真实暂存漂移失败后再次 Commit 同样待审查，原 Contributor 与人工暂存内容保留。目录只枚举名称，大小写不敏感，不读取或跟随残留；检查位于本次暂存创建之前，不误扫自己。治理 55/55；分片发现校验 1075 项无遗漏/重复，影响集 CodeGeneration 与 integration-matrix；独立复审无阻断。只证明检查时指定父目录仍存在材料，不能推及材料全部被外部删除、其他目录、检查后并发现场、自动恢复或实际杀进程验收；完整 F02 不关闭。

本增量最终核验：pnpm test:aot:analyzers 退出 0、0 警告/错误；pnpm test:dotnet:architecture -- --selection api-native-aot 73/73、0 失败/跳过，构建 0 警告/错误；独立复审无阻断，git diff --check 通过。远端独立应用/双库样例/Native 待绑定新 SHA，不以先前提交或本地选择器代替。

完整 Host CLI 接入计划（基线 `2a7d0eb2f85720e01e7bf74bc6f738c2009d6e31`，开工干净）：CLI 目前只暴露逐阶段命令，已有共享 Host 编排与授权写入保护缺少实际 CLI 消费者。先建立新命令要求显式授权目标、共享前置门禁及旧命令拒绝授权字段的失败/兼容回归；随后新增 `apply-host-integration` 模式，复用共享编排，不复制阶段实现，目标 JSON 只在该模式允许且要求非空 authorizationContributorPath，其他六命令拒绝该字段。补现有真实候选编译夹具中的完整 CLI 编排、Vue-only、授权片段及幂等验收，重型测试进入 Actions；本地只跑相关快速 Unit、构建、治理/分片/影响集与独立复审。更新教程，明确共享编排分阶段、授权候选独立编译门禁/实际权限注册/应用 Migrator/双库运行链仍须后续验收，不据此关闭 F02 或宣称完整原子接入。

本增量快速证据：新增 19 项 CLI Unit（初始 13 项 RED 为 7 失败/6 旧命令兼容通过，随后追加六项显式 null 拒绝），相关选择 653/653、0 失败/跳过，Release 0 警告/错误。新完整编排模式只调用已有 Host；授权字段按 JSON 属性存在性拒绝旧模式，Host 非空且沿既有相对路径模型验证。新增 1 项真实编译 Integration，准备独立临时模块/Composition、手写 Contributor 接口与 DI 注册，使用新 Vue-only 视图，不修改已有人工视图或冻结 Layui，重复执行比对全部仓库文件，最后实际编译已接入授权的模块。生成 Tenant 权限断言仅检查生成块（两条 Tenant、无 Host），避免手写 Tenant 权限造成假通过。该重型场景本地未运行，编译/发现不能报告为运行通过。

首次分片发现读取旧测试程序集为 1075，与 canonical 1076 不符，未计作通过；相关 Unit 结束后串行 dotnet build Integration Release 0 警告/错误，再发现并校验 1076 项无遗漏/重复。治理 55/55；API Native 架构选择 73/73、无跳过；影响集 CodeGeneration 与 integration-matrix。随后独立复审发现共享 Host 的 Entry/Composition 失败诊断可能为空，进入追加回归与修复；前述架构结果不替代修复后的最终验证。新 CLI 不改变 Native 发布状态，仍等待新 SHA 三条 Actions。本轮已补实际 CLI 入口，不认证授权预写候选编译、Vue 类型/浏览器、运行时权限与跨租户拒绝、应用 Migrator、整链原子性或完整 F02。

审查追加诊断修复：新增两项 Failure 工厂空/空白诊断回归，RED 2/2 全部失败；Entry/Composition 在自身诊断为空时转发 Compilation.Diagnostics，Failure 工厂滤掉空白并提供非空兜底。新增两项真实 CLI 后续阶段失败 Integration，断言错误原因可见及先前 Backend 清单已提交，明确不是全链零写入。首次手写 Unit 过滤运行 644 项均通过，但未达最低 655 而退出 9，不计为通过；改用 canonical code-generation-realtime 后 655/655、0 失败/跳过，Release 0 警告/错误。最终 Integration Release 构建 0 警告/错误，分片发现 1078 项无遗漏/重复，其中 Infrastructure 181；三个新增重型场景只编译/发现，实际运行待 Actions。治理 55/55；影响计划命中 CodeGeneration 与 integration-matrix。

最终源码 pnpm test:aot:analyzers 退出 0、0 警告/错误；pnpm test:dotnet:architecture -- --selection api-native-aot 73/73、0 失败/跳过，Release 0 警告/错误。新增 Host CLI 与共享诊断修复的远端独立应用、代表样例双库及 Native 验收仅绑定新 SHA，不使用之前提交的成功代替。

独立复审确认 Entry/Composition 空诊断 P2 已关闭，无剩余阻断；具体阶段错误与前序清单断言避免前置失败造成假通过。git diff --check 通过。仅提交本任务十文件，保持 Draft，不合并或发布。

授权候选编译增量计划（基线 6b27e89ea10e69c339680563bd45dd87eb25fb68，开工干净）：现有 Host 授权阶段只编辑并提交，没有与模块入口一致的候选编译门禁。先机械提取 internal 授权阶段，保持公共 Apply 契约与成功/失败语义；用内部编译委托建立失败、取消、成功及编译期间人工漂移回归。随后复用现有 Compile Remove/Include 与隔离构建，候选只替换显式 Contributor，失败不提交授权文件，前序阶段保持已提交；不新增公共测试缝、不引入 Roslyn 或扫描注册。真实不可编译候选及现有成功整链场景在 Actions 执行，Unit 不启动 MSBuild。串行快速 Unit、Integration 构建/发现、AOT/Architecture、治理及复审后提交推送，不关闭完整 F02。

失败证据与追加定位：五项授权门禁 Unit 在机械提取旧阶段后 RED 5/5 全部失败；初次接线出现方法插入位置错误造成编译失败（7 个错误），修正后相关 Unit 657 通过/3 失败，不能计作通过。两例为 Windows 测试路径未规范化；修正后聚焦 4 通过/1 失败，成功重试用例再次 RED 1/1，输出证明手写末项紧贴 ] 时逗号与块插入点相同、原稳定排序把逗号推到生成块之后。编辑器改为同位置按编辑序号倒序插入，精确断言手写末项逗号，避免依赖生成末项尾逗号。真实 CLI 缺少 Generated using 场景要求 CS0103 且 Contributor 原文不变，前序 Vue 已提交；Unit 只证明门禁顺序与提交边界，真正候选编译待远端。

最终相关 Unit 660/660、0 失败/跳过，Release 0 警告/错误；随后仅将手写逗号断言去除换行依赖以兼容 CRLF/LF，待最终验证核对。Integration Release 编译 0 警告/错误，分片发现 1079 项无遗漏/重复（Infrastructure 182）；治理 55/55。影响计划命中 CodeGeneration 与 integration-matrix，独立复审确认同位置排序修复、默认真实编译路径和固定候选名称无剩余阻断，不放宽生成块边界。真实编译场景仅编译/发现，尚未本地运行，不计作通过。

最终验证：新增默认真实编译器缺少模块项目拒绝用例（不注入委托、不启动 MSBuild），本轮合计六项快速回归；最终 pnpm test:dotnet:unit -- --selection code-generation-realtime 661/661、0 失败/跳过，Release 0 警告/错误。pnpm test:aot:analyzers 退出 0、0 警告/错误；pnpm test:dotnet:architecture -- --selection api-native-aot 73/73、0 失败/跳过，Release 0 警告/错误；最终治理 55/55。提交前 diff --check 通过，基线 Worker Native 36275971995 已成功，主 CI 36275972000/API Native 36275972004 仍执行中，仅作前置证据。本增量只提交八个任务文件，授权候选实际编译和新 SHA 真实栈/双库/Native 等待远端，不关闭完整 F02。

应用自有组合根增量计划（基线 a4c76d3ee64eaf6b279e0257775de99db428ad0c，开工干净）：模板 API 仅引用受管官方 Composition，无法向实际模块注册表与目录快照声明应用业务模块；现有 CLI 编辑器需要应用自有标准 CreateModules 清单。先为显式模块列表重载建立角色分离、依赖拓扑、目录来源及重复/缺失依赖/循环/官方键冲突的失败回归。保持三参数入口，新增四参数静态模块实例列表；先校验组合依赖图，再按 Api/Worker/Migrator 调用相应入口，API 在全部注册完成后一次物化目录，应用来源显式区分。模板新增应用自有 Composition 项目与标准 CreateModules() => []，Host 改为消费此组合根，不改变受管框架清单，不新增业务模块项目。Unit/模板结构 RED 后实现，串行快速 Unit/AOT/Architecture，模板结构/投影/治理与独立复审；独立应用实际构建与双库真实栈由 Actions 验证。应用自有 Migrator/生成业务数据库/OpenAPI/Vue/实际权限和跨租户拒绝仍后续，不关闭完整 F02。

本增量证据：新增 15 项模块组合回归 RED 全部失败；模板入口回归 RED 1 失败/5 通过。实现后最终 `pnpm test:dotnet:unit -- --filter 'FullyQualifiedName~Full.NET.UnitTests.Modularity' --minimum-expected-tests 15` 58/58、0 失败/跳过，Release 0 警告/错误。复审发现预设投影裁剪可用官方名称后，不能用它保护未安装的官方名称；新增实际 minimal 源码投影回归 RED 1 失败/3 通过，再以完整 ContractModuleNames 提供 internal 保留键判断，保持实现闭包裁剪。最终 created-app/project-preset-composition/template-options 三组 Node 结构检查 11/11、0 跳过；此处只验证投影源码、列表和接线，未执行投影后的 .NET 注册。packaged-app 新增生成应用自有 Composition 存在与命名断言，仅本地语法检查，真实创建/编译仍待新 SHA 的 Actions。

最终串行快速验证：`pnpm test:aot:analyzers` 退出 0、0 警告/错误；`pnpm test:dotnet:architecture -- --filter 'FullyQualifiedName~NativeAot|FullyQualifiedName~MemoryPackControlledProtocol|FullyQualifiedName~HostModuleProfile|FullyQualifiedName~ModuleDependency' --minimum-expected-tests 73` 76/76、0 失败/跳过，Release 0 警告/错误。Integration Release --no-restore 构建 0 警告/错误；治理 55/55。独立复审确认名称保留 P2 已收口、无新增阻断；影响计划命中 integration-matrix 与 smoke。应用模板此次只交付 API 消费自有清单，Worker/Migrator 仍需应用自行建设宿主；完整 F02 与 Capacity-not-verified 状态不变，不将结构、发现、旧提交远端成功当作新提交真实栈/双库/Native 验收。

分片核对：pnpm test:integration:partitions 退出 0，发现 1079 项无遗漏/重复（Infrastructure 182）；本地未执行数据库集成测试。

应用组合根运行时验收增量计划（基线 aecc278dc848049bf2b02ac0e2c846aa0068ff78，开工干净）：扩展已有 packaged-app 真实创建路径，在独立 Minimal 应用中新建一个验收专用业务模块项目，经应用自有 Composition 的项目引用和标准清单接入；使用独立控制台验收项目实际调用应用入口，检查三个 Profile 的唯一对应注册、API 依赖顺序与 Application/Official 目录来源。对每种角色执行未安装 Workflow/Payments 保留键拒绝及重复/缺依赖/循环的无服务污染断言。快速 Node 测试先证明编排器缺失会失败，再验证构建失败停止、运行失败停止、结果不完整拒绝及成功接线；实际临时 .NET 构建/运行仅交给现有 Actions，不将模拟子进程结果作运行时证据。日志放入已有 template-real-stack 上传目录；不新增生产模块、迁移、数据库行为或业务完整验收结论。

本增量快速证据：四项 Node 编排回归在空实现上 RED 0 通过/4 失败；实现后单独 4/4、与 created-app/project-preset-composition/template-options 联合最终 15/15，均 0 失败/跳过。构建失败不进入执行，执行失败和不完整报告拒绝，成功只证明注入 runner 的编排接线。node --check 覆盖 packaged-app 与新 helper；治理 55/55，影响计划不命中 Integration，不重复无受影响的 .NET/AOT 验证。独立复审确认 C# 夹具、项目路径与失败关闭无阻断；真实新 SHA 的编译/exec 尚待现有 template-created-app-real-stack Actions。接入前后均按真实生成清单逐文件复核受管框架摘要；日志保存 build/run 退出码、参数和输出，由已有 always artifact 上传。运行时设计 3 项角色、6 项保留键、9 项非法图，不将这些计划执行数计作已通过测试。未认证 Endpoint、整体 DI ValidateOnBuild、业务数据库、权限、应用迁移或 Native 发布，完整 F02 不关闭。

应用模块 HTTP 映射验收计划（基线 24959e80e7c848c1b0e4f5bcff19b57040495cd2，开工干净）：复用已有隔离 Demo 验收模块，拆出准备阶段供双库 created-app-real-stack 接入，再由真实生成的 API Host 经 MapFullNetModules 映射匿名、无业务数据与副作用的文本标记端点。新增 HTTP 验收 helper 的快速失败关闭回归，覆盖路由缺失、响应伪标记和成功；实际启动后、登录前请求该端点，provider 独立保存状态与响应日志。只修改测试夹具与验收编排，不新增正式业务 Endpoint，不以公开标记端点证明业务精确权限或租户隔离。Node 快速测试、语法/治理/影响集和复审完成后提交；临时应用 .NET、双库与宿主实际响应由新 SHA Actions 验证，完整 F02 不关闭。

本增量证据：HTTP 验收 helper 空实现上三项 RED 全失败；实现后追加请求故障原异常传播与日志检查，HTTP 4 项与原编排/结构/投影/选项联合 19/19、0 失败/跳过。governance 55/55，三个 helper 语法检查与 diff --check 通过；影响计划无 Integration 目标，不重复无受影响的 .NET/AOT 验证。复审确认诊断位于 probe 接入之前、Host 随后重新编译再启动且真实 MapFullNetModules 分派，官方 Migrator 不含 probe schema/seed，无新增阻断。响应或网络错误写 provider 独立日志后再拒绝，重定向禁止跟随且请求 15 秒超时。上述 Node 请求采用注入 Response，不是实际 HTTP/双库证据；真实新 SHA 的模板双库任务尚待 Actions，公开文本 marker 不能证明业务权限、租户、数据或应用 Migrator。完整 F02 与 Capacity-not-verified 不变。

应用可选契约依赖校验计划（基线 bef9b89bd4b3de66b985ef21941a34e6185baaba，开工干净）：官方 FullNetModuleSelection 会拒绝未知可选来源及必需/可选重叠，应用 ResolveHostModules 仅用 registry 校验必需图而漏过该边界。新增 Api/Worker/Migrator 六项失败回归后，在调用模块注册之前校验可选来源属于完整官方契约键或显式应用清单、且不与必需依赖重叠。补已裁剪官方来源允许和应用间可选来源允许的正向回归；可选契约不引入必需依赖闭包或顺序。更新独立生成应用控制台探针以覆盖相同拒绝及未安装官方可选契约允许；串行快速 Unit/AOT/Architecture、结构/治理/Integration 构建发现与复审，实际模板编译/双库/Native 待新 SHA Actions，不扩大为动态模块发现或新应用 Migrator。

本增量已获快速证据：六项未知/必需重叠可选契约回归 RED 全失败，Release 构建 0 警告/错误；加三项正向后，pnpm test:dotnet:unit -- --filter 'FullyQualifiedName~Full.NET.UnitTests.Modularity' --minimum-expected-tests 24 最终 67/67、0 失败/跳过，Release 0 警告/错误。Node 编排/HTTP/结构/投影/选项联合 19/19，治理 55/55；影响集命中 integration-matrix 和 smoke。复审无阻断，确认检查在全部服务回调之前，可选契约不进入拓扑；模板 runtime invalidGraphs 15 是待执行场景计数，不是已通过数，真实投影 .NET/HTTP/双库仍待新 SHA Actions。README 同步应用可选来源约束。

最终串行核验：pnpm test:aot:analyzers 退出 0、0 警告/错误；pnpm test:dotnet:architecture -- --filter 'FullyQualifiedName~NativeAot|FullyQualifiedName~MemoryPackControlledProtocol|FullyQualifiedName~HostModuleProfile|FullyQualifiedName~ModuleDependency' --minimum-expected-tests 73 为 76/76、0 失败/跳过，Release 0 警告/错误。Integration Release --no-restore 构建 0 警告/错误，pnpm test:integration:partitions 发现 1079 项无遗漏/重复，Infrastructure 182，仅发现未执行数据库测试。最终 Node 19/19；提交前治理/diff --check、分支与状态再次核对。本轮新增 Unit 9 项，canonical minimum 3330；不以原提交或模板结构成功代替新 SHA 的真实应用/双库/Native 结论，完整 F02 保持未关闭。

模块键注册前规范性门禁计划（基线 56ebb7c5bb7e1975e0f549e8ae841e005b9249c4，开工干净）：Descriptor 会拒绝路径分隔符/空字符并裁剪键空白，Registry.Add 只拒绝空白键，导致 API 在模块服务注册后才因快照非法/键不一致失败，Worker/Migrator 则可能接受非法键。新增三角色四类键共 12 项 RED，证明回调和原服务集合不能被污染；注册表复用现有 descriptor token 规范化规则，但严格拒绝规范化后发生变化的原始键，保持 Ordinal 稳定键语义。补注册表四项直接拒绝且不残留登记回归，保留 Descriptor.Create 原有裁剪行为，不新增命名正则或静默重命名。串行 Unit/AOT/Architecture/Integration 构建与分片、Node结构/治理/影响集、复审后提交，新 SHA 真实应用/双库/Native 仍待 Actions，完整 F02 不关闭。

本增量快速证据：三角色四类键 12 项 RED 全失败，注册表四项直接状态保护 RED 全失败，均 Release 0 警告/错误。修复后相关 Unit 首次 83/83；追加 Descriptor.Create 首尾空白裁剪兼容断言，最终 pnpm test:dotnet:unit -- --filter 'FullyQualifiedName~Full.NET.UnitTests.Modularity' --minimum-expected-tests 40 为 84/84、0 失败/跳过，Release 0 警告/错误。本轮新增 Unit 17 项，canonical minimum 3347。Node 编排/HTTP/结构/投影/选项 19/19，治理 55/55；影响集 integration-matrix、smoke。复审无阻断，确认 public 签名、原 Descriptor.Trim 和 Snapshot 语义保留；拒绝位于 Dependencies 读取前由源码顺序确认，未报告依赖 getter 无调用已动态验证。README 同步 exact Name 门禁。runtime invalidGraphs 27 是待执行场景计数，实际投影编译/HTTP/双库仍待新 SHA Actions，完整 F02 不关闭。

最终串行核验：pnpm test:aot:analyzers 退出 0、0 警告/错误；pnpm test:dotnet:architecture -- --filter 'FullyQualifiedName~NativeAot|FullyQualifiedName~MemoryPackControlledProtocol|FullyQualifiedName~HostModuleProfile|FullyQualifiedName~ModuleDependency' --minimum-expected-tests 73 为 76/76、0 失败/跳过，Release 0 警告/错误。Integration Release --no-restore 构建 0 警告/错误，pnpm test:integration:partitions 发现 1079 项无遗漏/重复（Infrastructure 182），仅发现未执行数据库测试。最终 Node 19/19、治理 55/55、语法检查/diff --check 与分支/status 核对通过。只提交本任务文件，新 SHA 真实应用/双库/Native 以远端终态为准，完整 F02 和 Capacity-not-verified 不变。

应用结构校验增量（基线 a42071057572b066e4c883b6c16820b9732eaff5，开工干净）：verifyCreatedApp 原先未要求 API 宿主同名的应用自有 Composition，且 existsSync 允许目录占据必需文件路径。七项结构夹具 RED 为六失败、一通过；修复后要求匹配 Composition 项目和 ApplicationModuleCatalog.cs，普通必需文件及 Host 文件也必须为文件。statSync 保留跟随链接的既有行为，本门禁仅验证结构，不声明签名、路径安全或编译成功。README 同步创建器发布前门禁；createApp 的暂存校验调用点已确认。

本增量快速验证：node --test 执行 verify-created-app、application-composition-probe、application-module-http、created-app、project-preset-composition、create-app 六组共 36/36、零失败/跳过；其中包创建与代理端口正例实际调用创建器，不含应用 .NET 构建。治理 55/55；受影响计划首次误用 --base-ref 失败，改用 --base 后退出 0，Integration 影响为 none，未重跑无关 .NET 构建/AOT。Node 语法检查与 diff --check 通过。真实应用编译、双库 HTTP 和 Native 验收仍绑定新 SHA Actions，完整 F02 和 Capacity-not-verified 不变。

应用预设一致性增量（基线 68f20311a186bbfc85d18de6633b699df64e5ca0）：开工 status 显示 eng/testing/test-matrix.json 修改、diff 无正文差异；建立 f02-created-app-preset-consistency-20260927 任务快照，未修改或纳入该文件。根配置此前只检查 FullNet:Modules 存在，API 配置只检查路径存在，二者均可能与冻结应用预设不一致而通过创建发布前校验。新增根/API 的不一致和缺失预设、API JSON 损坏五项 RED，均失败；实现同时解析两份配置并与 fullnet-app.json.preset 精确匹配，null/数组配置不能通过。补四个规范预设匹配正例及 null/数组负例；错误保留路径并返回原 ok/errors 契约，不检测或改写环境覆盖。

本增量快速证据：六组 Node 联合 47/47、零失败/跳过；最终仅测试缩进调整后再次执行 verify-created-app 18/18。任务快照的 inner 影响规划为 none，不重跑无关 .NET/AOT/双库本地构建。README 同步生成文件预设检查和运行期诊断边界。基线 a420710 的 CI 36280035991、API Native 36280035898、Worker Native 36280036490 已全部成功，其中 template-created-app-real-stack 与 build-test 明确成功；68f20311 的 API/Worker Native 36280475210/36280475224 成功，主 CI 36280475243 仍运行。基线终态不替代本轮新 SHA，F02 完整业务生成链和 Capacity-not-verified 保持未关闭。
应用数据库提供程序一致性增量（基线 02ee8c315ef0db502c34347d0e52089d61d3f893）：开工保留测试矩阵状态修改，建立 f02-created-app-provider-consistency-20260927 快照。verifyCreatedApp 只校验应用档案的 provider 合法，没有核对根/API 配置的 Database:Provider。两配置分别使用错误正式提供程序、缺失字段和未知 provider 的六项 RED 均失败；实现与冻结档案精确比较，错误包含对应配置路径，沿用 ok/errors 返回契约。新增 SQL Server/MySQL 匹配且不改写文件的两个正例；已有四预设夹具同时供给合法数据库配置，避免其他错误造成负例假通过。README 同步文件配置边界，不检测或改写环境覆盖。

本增量 Node 六组联合 55/55、零失败/跳过，包含实际创建器/代理端口正例但不含生成应用 .NET 构建。快照 inner 影响计划为 none，Node 语法检查和 diff --check 通过；未重跑无关 .NET/AOT 或本地双库。开工时 02ee8c31 三条 Actions 仍运行、无失败作业；真实应用编译、连接、迁移与 HTTP 仍以本轮新 SHA 终态为准。完整 F02 和 Capacity-not-verified 保持未关闭。
应用源码目录诊断增量（基线 041b6cdf825c510416cfb624dc0b5c93c65d420f）：保留开工测试矩阵状态，建立 f02-created-app-directory-diagnostics-20260927 快照。存在 src 路径时直接 readdirSync，普通文件占位触发 ENOTDIR，导出函数不能返回 ok/errors，CLI 输出未处理堆栈。先补函数不抛出且保留原文件、CLI 返回 1 且无未处理堆栈两项 RED，均失败；将源码目录读取失败纳入结构诊断，保留单一 API 宿主门禁，不自动修复或覆盖。另补缺失、空目录、多宿主三项回归，保留原结构错误。

本增量 Node 六组联合 60/60、治理 55/55，均零失败/跳过，包含实际创建器正例。快照 inner 影响计划为 none，Node 语法检查及 diff --check 通过；本轮仅校验脚本和测试/文档，不重跑无关 .NET/AOT 或本地双库。实际回归证明普通文件占位，未模拟 ACL 拒绝或目录读取竞态；未将异常转换声明为权限/路径安全保障。F02 完整业务生成链和 Capacity-not-verified 保持未关闭，新 SHA 真实应用编译及双库运行以 Actions 终态为准。

独立应用 CRUD CLI 再生成验收计划（基线 c4753ed2e90450ad8aa66af6179239fb48689880）：保留开工测试矩阵状态并建立 f02-created-app-crud-regeneration-20260927 快照。新增 tests/templates/support/application-crud-generation.mjs 与应用自有 tenant.required 产品 Schema 夹具；构建并执行应用包内部的 CLI，从应用根目录依次预览、生成、相同输入再生成、人工修改受管 SQL 后拒绝再生成。检查后端/双库迁移模板/OpenAPI/Vue 共十四个产物、生成清单及人工文件字节，保存每阶段命令/退出码/输出，精确冲突路径与退出 2，失败关闭。tests/templates/application-crud-generation.test.mjs 用注入 runner 先建立失败回归，只证明编排。接入 packaged-app.test.mjs 的已创建 Minimal 应用，在受管框架摘要复核前执行真实 CLI 验收；不创建第二业务模块实现、不改受管框架，不启动业务数据库。同步首个 CRUD 入口说明，快速 Node/治理/影响计划和独立只读复审后提交推送；实际 CLI 构建执行只在 Actions。模块/宿主接入、业务迁移运行、OpenAPI 运行、Vue 页面、精确权限和跨租户拒绝仍属后续 F02，本轮不关闭完整项。
本增量失败与快速证据：初始六项编排 RED 全失败；实现后五通过/一失败，定位为 Windows 路径分隔符断言，改为 join 构造精确项目路径后六通过。追加预览意外写盘、重复生成损坏人工文件、冲突后覆盖及错误冲突路径四项失败关闭回归，最终新套件 10/10；七组 Node 联合 70/70、治理 55/55，均零失败/跳过。随后补预览立即核对人工文件原文，受影响新套件再验 10/10。Node 语法检查/diff --check 与快照 inner 影响规划 none。仅为验证新 Schema 与产物路径，串行构建仓库 CLI Release --no-restore（零警告/错误）并在隔离临时工作区实际生成十四个产物；首调用因工作区不存在返回 64，建立目录后退出 0，不能把首调用计为成功。这是仓库 CLI 的夹具校验，未本地构建或运行新应用内部 CLI，也未执行业务模块或迁移模板。

独立只读复审无阻断：确认当前 Schema/CLI 参数与十四路径一致、应用根 backend 不被 src 宿主自动编译、产物/清单/人工文件字节保护与前后受管框架摘要复核保留，既有 always artifact 包含新日志。本轮仅新增生成与保护门禁，不认证业务接入；真实独立应用 CLI 阶段等待新 SHA Actions。

独立应用生成模块编译增量计划（基线 69c9c1d77ac757f4fccc3cb0b7a1331831b19a4f）：保留测试矩阵状态，建立 f02-created-app-crud-module-20260927 快照。新增 application-crud-module.mjs 与应用拥有的 CatalogModule 夹具，复用前增量 schema 和应用内部 CLI，显式目标指向 src/Demo.Modules.Catalog；项目仅相对引用应用包中已有 Abstractions/Data.Abstractions/Hosting/Modularity/Identity.Contracts。依次 apply-module-integration（候选编译）、实际模块 Release 构建、相同输入再接入、人工修改 Generated SQL 后冲突退出 2。检查六项后端产物、模块清单、项目/入口/人工文件字节；应用 API/Composition/Vue 和根生成清单保持原文。Node 注入 runner 先建立失败回归，仅证明编排；接到 packaged-app 在 CRUD 生成验收之后、受管框架摘要复核之前。真正独立应用内候选及模块编译只在 Actions，不在本地重跑重型临时应用构建；本轮不接入 API/授权/Vue、不启用业务迁移、不关闭完整 F02。快速验证、独立只读复审后提交推送并按新 SHA 核对。
本增量快速证据：初始六项 Node 编排 RED 全失败，实现后六通过；追加项目改写、模块入口漂移、冲突覆盖、应用 Composition 改写、错误冲突路径和缺失编译标记六项拒绝回归，最终新套件 12 项、八组联合 82/82，治理 55/55，均零失败/跳过。语法/diff 检查和快照 inner 影响规划 none。未在本地运行应用候选/模块 .NET 编译；injected runner 成功不作为实际编译或业务运行证据。原根生成 SQL 人工修改继续保留，与模块自己的 Generated SQL 冲突分别验收。

独立只读复审无阻断，新套件12/12无跳过；确认五个相对ProjectReference与现有真实Integration编译夹具一致，当前Schema不需要额外Organization或Dapper运行实现引用，根生成清单/人工SQL与模块产物保持分别保护。CatalogModule尚未消费生成注册桥，候选/实际模块编译也不证明Host/DI/权限/迁移运行。真实编译继续待新SHA Actions。

独立应用生成模块宿主接线计划（基线 32482b150148c838bd9535a3be925773e72fee88）：保留开工测试矩阵状态，建立 f02-created-app-crud-host-wiring-20260927 快照。新增 application-crud-host-wiring.mjs，复用前增量应用CLI/schema/目标；apply-module-entry-integration 注入生成Add/Map桥、apply-composition-integration 引用应用模块并加入已有Probe的应用清单，随后实际Release构建API，再重复两阶段要求Unchanged和所有相关源码字节不变。严格CLI候选编译标记及目标路径，保护生成产物/模块清单/人工文件、API入口、Vue、根生成清单与根人工SQL。模块冲突负例结束后，仅可显式撤销本验收自己追加在新建模块Generated SQL上的测试注释，以继续接线；先验证冲突完整保护，不恢复未知人工内容，不改根人工SQL或框架。Node先建立失败回归；真实临时应用编译交Actions，独立复审后提交推送。本轮不接入授权贡献者、业务迁移或Vue，不验证HTTP/DI/精确权限，不关闭完整F02。
本增量快速证据：宿主编排六项 RED 全失败；显式夹具清理一项 RED 失败。实现后十九项聚焦全部通过；追加报告成功未接线、丢失Probe、保护文件改写、API构建改写源码、两个重复阶段漂移及缺失候选编译标记共七项拒绝回归。九组Node联合96/96、治理55/55，均零失败/跳过；语法/diff检查与快照inner影响计划none。只运行注入runner，不声明独立应用候选/API编译通过；Native工作流针对框架宿主，亦不能替代生成应用Native发布。原模块冲突保护默认不清理，只有packaged验收显式选择撤销其新建模块SQL测试注释。

独立只读复审无阻断，实际运行宿主与模块Node两组26/26、零失败/跳过；确认真实CLI重复接线只有Unchanged、没有候选编译标记，原Probe/项目引用保留，夹具清理仅撤销本次新建模块SQL测试注释且先完成冲突字节保护。另核对69c9c1d7的template-created-app-real-stack作业108519592619日志：application template package includes framework sources and root manifest实际成功，作业121/121、零失败/跳过，确认该基线包内CLI生成/再生成已在远端执行。该证据只绑定69c9c1d7，不替代后续模块候选编译或本轮API接线验收，也不代表整个主CI已成功；完整F02仍未关闭。

独立应用生成模块运行装配计划（基线04ae98269812ffcef7870b6486a7253c6c40dc22，快照f02-created-app-crud-runtime-20260927）：新增application-crud-runtime.mjs、Node门禁测试和运行检查C#夹具。只在隔离应用verification/CrudRuntimeProbe新建验收宿主，引用实际应用API项目，复制当前应用Program启动装配与映射代码；要求唯一标准builder和app.Run锚点，插入ValidateOnBuild/ValidateScopes，替换Run为检查并异步释放，不改应用入口或受管框架。使用Development与不连接的测试数据库配置，验证Catalog与原Probe的目录来源/依赖顺序、两个生成服务Scoped注册及跨作用域实例隔离、五条生成路由的准确方法/路径/权限元数据和非匿名约束、公开DTO经实际HttpJsonOptions的长整数字符串往返。先为执行失败、报告缺失/重复/不完整、源码漂移及前置条件建立可失败Node验证；packaged验收在接线完成后运行，保留命令/退出码与结果报告。实际编译/执行只交Actions，不启动监听、后台服务、健康检查或数据库，不将路由元数据当作实际权限执行，不认证HTTP、业务双库/迁移、Vue或Native；本地快速检查、独立复审后提交推送。

本轮发现04ae9826独立应用作业108522536168失败：Composition CLI报“必须且只能存在一个可验证的ProjectReference ItemGroup”，退出2。根因是此前Composition探针额外创建第二引用组，与实际CLI结构门禁冲突，模拟执行器未暴露这个真实组合问题。追加三项结构回归RED全部失败（原四项通过），修正夹具将探针引用插入原标准组，缺失/多个引用组在写盘之前拒绝；不放宽生产编辑器。修正后Composition探针7/7。运行探针门禁初始13项RED全失败，实现后13/13；十组Node联合112/112、治理55/55，零失败/跳过，语法/diff与inner影响计划none。生成JSON上下文要求实际注册且提供ProductResponse元数据，不能依靠反射兜底通过。只运行注入执行器，新的真实探针编译/运行尚待Actions。

另核对32482b15的template-created-app-real-stack作业108520747604日志：packaged应用测试实际执行成功，作业133/133、零失败/跳过，确认生成模块候选与实际模块编译已在该基线远端通过。该证据不覆盖04ae9826新增宿主接线，不代表整个主CI终态；已知接线失败须由本轮新SHA重新验收，完整F02保持未关闭。

独立只读复审无阻断，实际运行Runtime与Composition两组20/20、零失败/跳过；确认原ItemGroup与引用保留、派生宿主保留实际模块装配/映射、DI检查在作用域内解析真实生成服务、DbSession构造不打开连接、五路由与当前硬删除生成器一致，生成JSON上下文检查避免反射兜底假绿。未进行本地临时应用.NET编译或容器，真实编译/ValidateOnBuild及运行结果仍待新SHA Actions；API/生成产物/人工文件保持字节保护，不认证监听、权限执行或数据层。

上一基线f96f0aa1远端证据：template-created-app-real-stack作业108524529860实际成功，日志163/163、零失败/跳过；上传报告确认注册桥/Composition/API编译和重复Unchanged通过，运行探针Release构建退出0、零警告/错误，实际执行退出0且完整输出两个Scoped服务、五受保护路由和JSON往返结果。只绑定该SHA，主CI与Native仍未整体结束，不据此认证实际权限执行或业务双库。

独立应用生成授权目录计划（基线f96f0aa19ae245f7d95ed54ff358491a3c028765，快照f02-created-app-crud-authorization-20260927）：新增application-crud-authorization.mjs、Node门禁与应用拥有的CatalogAuthorizationContributor夹具。接线完成后显式注册无状态Singleton贡献者（实际授权目录为Singleton工厂），新增不带clientRoute的授权目标，复用包内apply-host-integration的授权候选编译与提交；Vue阶段保持跳过。检查三个生成区块、四Tenant精确权限、原人工权限、所有其他产物/清单/宿主/路由字节，并追加本验收的人工注释后重复整链要求完整不变。运行探针在授权后执行，检查公开Contributor中的四权限/一页面/三操作准确绑定，解析实际IAuthorizationPolicyProvider以触发权威目录物化与一致性验证，要求四策略存在且必须认证、原人工策略保留、未知策略返回null。新增计数必须进入运行报告，旧报告拒绝；不扩大生产公共契约或读取内部目录，不通过反射绕过模块边界。Node先RED，快速联合/治理/影响检查与独立复审；真实CLI授权候选/API/运行探针仅由Actions。实际授权执行、监听、数据库/迁移、Vue页面与Native保持后续，不关闭F02。

本增量快速证据：授权编排15项RED全部失败；运行报告变更与旧报告拒绝RED为2失败/12既有通过。实现后授权15与Runtime14合计29/29；十一组Node联合128/128、治理55/55，零失败/跳过，语法/diff与快照inner影响计划none。来源配置和全部既有根/模块生成产物、两个清单、应用宿主/Composition、人工文件与Vue路由分别按字节保护；仅新增本验收贡献者/授权目标和显式模块注册。运行探针要求原官方Identity贡献者与identity.navigation.read策略保留，并对授权检查插入锚点要求唯一，防止漏插检查却输出静态成功计数。未本地执行临时应用.NET或容器，Node注入成功只证明门禁，实际候选编译及策略Provider解析仍待新SHA Actions。f96基线Worker Native已成功，主CI/API仍运行中，不替代本增量证据。

独立只读复审无阻断，实际运行授权与Runtime两组29/29、零失败/跳过；确认五个相对项目引用满足当前候选源码、标准贡献者集合符合Editor边界、Singleton符合权威目录根工厂、完整Host跳过客户端阶段、实际Provider两要求/未知null语义与探针一致，未见明显C#依赖或假绿问题。模拟片段写在类型之外仅供Node门禁，不作为真实编译证据；真实候选、API与目录物化继续待新SHA Actions。最终相关29/29、治理55/55再次通过。

授权范围负例隔离增量（基线b0c16d236a46a037e97bb5de3114b8c4ce428cda，快照f02-authorization-scope-negative-20260927）：复核发现Node的host-scope注入同时改写人工权限，先触发人工权限保护，未独立验证生成权限范围门禁。先给该用例加入失败原因断言，RED为1失败/14通过，实际错误为manual permission lost；再仅替换生成片段中的Tenant范围，保留人工权限，要求命中生成范围计数0而预期4。授权与Runtime29/29、治理55/55、零失败/跳过；语法/diff检查通过，inner影响计划none。只修正测试注入，不改生产实现或真实C#运行探针；独立应用编译及权威策略解析仍须远端证据。

基线b0c16d23远端授权验收证据：主CI 36286321758的template-created-app-real-stack作业108527673293成功，日志179/179、零失败/跳过，packaged应用用例实际执行。上传报告确认授权apply/repeat均退出0、精确HostIntegration标记和两个保护结果为true；运行探针Release构建退出0、零警告/错误，执行退出0，完整报告包含四权限、一页面、三操作、四策略，以及既有模块注册、两个Scoped服务、五路由和JSON往返。该证据证明此SHA独立应用的候选编译与权威策略解析，不能视为生成业务权限请求/数据库运行，也不替代范围负例修正后的新SHA验收。主CI、API与Worker Native当前仍在运行，未报告整体成功；F02和Capacity-not-verified保持原状态。

独立应用授权执行增量计划（基线d2ef1c29a81b66125b4ce140a5a89f2acf5f7231，快照f02-created-app-authorization-execution-20260927）：在已有非监听运行探针内，经公开IAuthorizationService执行实际应用策略和Handler，不替换生产授权服务、不调用内部实现。为每个生成权限检查租户精确权限允许、缺权限拒绝、携带权限及超级管理员标记的匿名主体拒绝、Host精确权限拒绝、Host超级管理员拒绝、缺失/非法作用域拒绝、租户超级管理员允许；另以四权限互相交叉执行16次，只允许相同权限。合计48次，预期12允许/36拒绝，由真实执行结果累计并写入完整报告；旧目录解析报告与缺失执行计数拒绝。仅构造测试Claims主体模拟认证后的输入，不认证JWT签名、会话、安全戳、HTTP或跨租户数据隔离；不启动监听、数据库、后台服务，不改公共契约。先建立Node门禁RED，再扩展C#夹具/报告，快速联合与治理、影响检查和独立只读复审后提交推送，实际独立应用编译和执行只交Actions。

本增量快速证据：运行门禁RED为2失败/14既有通过（新成功报告尚不匹配、旧policy-only报告被误接收），零跳过；扩展后Runtime16与授权15合计31/31、十一组Node130/130、治理55/55，零失败/跳过。源码语法/diff检查与inner影响计划none。实际执行计数在授权调用与结果断言通过后累计，不将静态报告或注入runner当作48次C#执行证据；真实独立应用的编译和运行待新SHA Actions。

独立只读复审无阻断，实际运行授权/Runtime31/31、零跳过，语法/diff通过。确认新增using/顶层异步local函数与公开接口匹配，作用域解析实际授权服务，Provider和Handler沿既有纯内存权限路径执行；匿名即使携带权限与管理员标记仍需认证，Host管理员也没有Tenant权限旁路。计数在每次真实结果断言后累计，报告使用实际变量；没有替换Handler或数据访问服务。未本地进行.NET/容器，实际生成应用48次执行继续待新SHA Actions。

基线1443cf82独立应用作业108530142733（主CI36287219999）成功181/181、零跳过。上传报告确认Release零警告/错误、实际48次授权执行为12允许/36拒绝，授权接入与重复保护成功；只认证模拟认证后Claims的规则，不认证JWT/会话/HTTP/业务双库。主CI和Native目前仍运行，未报告整体成功。

应用自有Migrator增量计划（基线1443cf822fca75a75cda28debddb52a87c978d75，快照f02-application-migrator-20260927）：新增迁移专用Full.NET.Hosting.Migrator BuildingBlock，将既有internal工作流与启动/退出处理集中复用，公开FullNetMigratorHost.CreateBuilder/RunAsync；只有框架Migrator和新模板Migrator两个实际消费者，依赖向BuildingBlocks收敛，无Composition/业务模块反向引用，API/Worker不得获得迁移或Seed执行依赖。保留稳定错误码、先迁移后显式播种、Host上下文和资源释放，新增公共入口的参数/取消/失败回归。模板增加应用拥有的Migrator项目、入口与配置，显式调用应用Composition的Migrator Profile；创建发布前强制检查同名Migrator的文件和冻结配置，独立结构校验默认兼容旧应用但对已存在Migrator进行检查。四预设构建与双库真实栈改用应用Migrator，继续跑框架迁移和Settings CRUD；业务SQL草案仍不自动注册，不声明生成业务迁移通过。先建立Node/公共入口失败证据，串行聚焦Unit/Architecture、治理、分片发现与影响计划，独立复审后提交推送，真正临时应用构建/双库交Actions。新BuildingBlock的精确消费者及API/Worker闭包由Architecture锁定，不改变1.0架构基线或Native发布承诺。

本增量失败证据：模板宿主缺失RED为1失败/6既有通过，结构校验RED为11失败/32通过；公共入口取消回归有效RED为4失败/14通过。早期夹具缺租户注册的失败、编译修正不计行为RED。独立复审发现旧失败路径只Dispose而不StopAsync，强化失败/取消断言及停机异常回归RED为7失败/13通过；部分Startup失败清理回归RED为1失败。最终入口统一finally停机，预取消在启动前拒绝，部分启动失败也尝试停机；停机使用独立令牌及宿主ShutdownTimeout，保留既有工作流错误码，单独停机失败退出1。复审终检无新增阻断，未由复审代理执行.NET/容器。

最终快速验证：`pnpm test:dotnet:unit -- --filter "FullyQualifiedName~FullNetMigratorHostTests|FullyQualifiedName~MigratorWorkflowTests" --minimum-expected-tests 21` 21/21；`pnpm test:dotnet:compatibility` 12/12，Release均零警告/错误。聚焦HostModuleProfile及两个迁移依赖规则7/7；`pnpm test:dotnet:architecture -- --no-build --selection api-native-aot` 73/73。模板/结构Node50/50、治理55/55、命名33/33，均零失败/跳过；十一组快速Node143发现/142通过/1跳过（源包输入尚未提交，完整创建用例待提交后重跑，不计通过）。`pnpm test:integration:partitions` 发现1079项，无遗漏/重复；这不是实际数据库测试。影响计划命中integration-matrix与smoke；新Unit下限按14项实际新增用例3347→3361，Architecture按两项230→232，无降低门禁。独立应用四预设真实构建、应用Migrator双库执行和Settings CRUD均待本次新SHA Actions；未在本地构建临时生成应用、运行Docker或Native发布。应用Worker、业务SQL注册/迁移和完整F02仍未交付，Capacity-not-verified不变。

fdca383fbff6ad4dd514242d13a9e8226920324e提交后快速Node143/143、源码包8/8，零失败/跳过。该SHA主CI36289242695的独立应用作业108535992634成功194/194、零跳过，SQL Server/MySQL真实栈和四预设API/Migrator构建实际执行；不将单项成功视为整体CI成功。主构建作业108535992606编译零警告/错误，但全Unit3361项中5失败/3356通过/0跳过：新宿主测试缺少显式Identity开发签名配置，在干净Linux输出目录启动时被现有Validator拒绝；本机残留Development JSON掩盖该夹具缺口。

夹具修正计划及证据（基线fdca383f，快照f02-migrator-fixture-20260927）：在真实CreateBuilder后清除配置源以隔离后续模块绑定，显式提供Development临时签名开关，保留真实Minimal Migrator Profile、启动校验和不可连接数据库。清空后的有效RED21项为5失败/16通过，与远端签名失败一致；修正后聚焦Unit21/21、零跳过，Release零警告/错误，独立复审无阻断。未改变生产Validator、宿主装配、测试计数或跳过门禁；已有CreateBuilder缓存注册发生在清空之前，不声明整个基础设施完全配置隔离。远端全量Unit仍待修正后的新SHA，不把fdca的主构建失败报告为通过。

053ec11fb7ff1733c2bce16acf242f7c75064b22主CI36289683027的主构建作业108537247288确认全Unit3361/3361、Compatibility12/12、零失败/跳过，签名夹具失败已收口。随后全Architecture232项出现1失败/231通过：TenantContextMutationBoundary精确清单仍保留旧Migrator Program路径，实际固定Host上下文写入已移至共享入口。修正快照f02-migrator-tenant-boundary-20260927，仅将旧文件一对一替换为FullNetMigratorHost.cs并保持Ordinal排序；仍全src扫描、精确路径完整相等，不增加通配或额外消费者。有效聚焦RED2项为1失败/1通过；修正后租户边界及迁移角色/依赖9/9、零跳过，Release零警告/错误。第一次最低预期3与实际2不符，不作为门禁计数证据。生产上下文行为未改，新SHA全Architecture及其他CI终态仍待重验；053独立应用作业108537247334已成功，不据此关闭整体F02。

基线5df8895a6a2f5171b978dc3ff516f65ae8752ec2远端独立应用作业108538438662成功194/194、零失败/跳过；四预设API/Migrator构建、两库应用Migrator各99项框架迁移/Development播种与Settings CRUD实际通过，既有探针48次授权仍为12允许/36拒绝。该SHA的两个企业样例和客户端作业成功，主构建Unit/Compatibility/Architecture步骤成功；本地完整Architecture232/232、零跳过。主CI36290109797的受影响Integration/Smoke、两个迁移恢复分组及API/Worker Native仍运行，不据此关闭完整F02。

独立应用诊断配置一致性修正计划（基线5df8895a，快照f02-standalone-diagnose-config-20260927）：沿生成业务迁移准备链检查发现DiagnoseCommand只对所选API JSON执行冻结档案比较，忽略根配置和新增应用Migrator，缺少API JSON时又可能回退根文件并报告一致。先在真实CLI建立根/API/同名Migrator提供程序和预设漂移、缺失/目录占位/无效JSON/字段类型的失败回归及旧应用无Migrator正向回归，要求输出不泄露凭据且全输入文件字节不变。然后在既有CheckStandaloneAppProfile集中读取根/API及已声明同名Migrator的基础JSON，保持现有错误码和大小写比较；只在全部配置匹配后输出DIAG_APP_PROFILE_OK。旧应用不强制新增Migrator，不解析或改写环境/UserSecrets运行期覆盖，不启动宿主/数据库，不扩大为完整SDK版本或迁移可执行性认证。模板真实栈增加Migrator配置字节保护，沿现有包内CLI诊断路径验收。文件范围为DiagnoseCommand.cs、新Unit夹具、模板真实栈helper、矩阵/教程与本总计划；先RED后最小实现，串行聚焦Unit、治理/命名/影响计划，复审及开发分支推送，新SHA真实包内诊断由Actions证明。业务SQL注册及应用Worker仍后续。

本增量有效RED：初始独立诊断21项为17失败/4通过。复审发现API JSON可发现但读锁占用时，第二次读取抛出IOException使已有机器诊断丢失；真实FileShare.None回归RED为1失败。重复JSON属性的延迟解析抛出ArgumentException并由通用CLI回显属性名，新增及既有字段形状联合RED为7失败/8通过，零跳过。最小修正仅在诊断JSON读取和字段解析边界捕获这些异常，保留现有DIAG_INVALID脱敏契约，不改CLI通用异常处理。最终 `pnpm test:dotnet:unit -- --filter "FullyQualifiedName~StandaloneDiagnoseConfigurationTests|FullyQualifiedName~DiagnoseCommandTests" --minimum-expected-tests 50` 为50/50、零失败/跳过，Release零警告/错误；独立夹具27项及原诊断23项，本轮实际新增29项，Unit最低门槛3361→3390。真实CLI测试逐文件核对输入字节；未执行临时应用.NET构建或数据库测试。

快速结构检查：模板/校验Node50/50、治理55/55、命名33/33，零失败/跳过；helper语法检查通过。`pnpm test:integration:partitions` 发现1079项，无遗漏/重复，仅发现不是数据库执行；快照影响计划命中CodeGeneration及integration-matrix。基线5df8895a的API Native36290109857、Worker Native36290109839已成功，迁移恢复current作业108538438675成功；主CI仍运行。这些基线证据不能替代本轮新SHA的包内诊断、Linux读锁回归和受影响双库验收，完整F02与Capacity-not-verified不变。

独立复审终检确认两项P2均收口、无新增阻断，核对读取/字段/profile/closure的脱敏异常边界、双诊断保留、文件字节保护、旧应用兼容及29项计数；复审未执行.NET/容器。最终治理重跑55/55、零跳过。仅提交本任务八个文件，保留开工object-comments状态，不合并或发布。

340e3a7c41c0e57d0f366b74d88dd41412da5ab4提交后Node143/143、源码包8/8，零失败/跳过。远端独立应用作业108542657219成功194/194、零跳过，实际执行包内CLI诊断、Migrator配置字节保护、四预设API/Migrator构建及双库真实栈；两个企业样例成功。主构建108542657170编译及分片发现成功，全Unit3390为3388通过/2失败/0跳过：新27项含真实文件锁全部通过，两条旧CodeGenerationCliTests仍使用根配置漂移正例及缺根配置的API漂移负例，不符合已加强的基础JSON契约。

旧夹具同步（基线340e3a7c，快照f02-diagnose-existing-fixtures-20260927）：本地原两项有效RED为2失败/0通过/0跳过，与Linux失败一致。正例根/API均匹配冻结清单并增强exit0断言；API漂移负例补匹配根，保留MISMATCH及凭据脱敏，并拒绝INVALID；缺模块引用负例也补匹配根并要求PROFILE_OK以隔离错误源。只修改测试与本记录，不改生产行为、计数、门禁或跳过策略。`pnpm test:dotnet:unit -- --selection code-generation-realtime` 扩大回归690/690、零失败/跳过，Release零警告/错误。独立复审无阻断；新SHA全量Unit仍需Actions确认，340e3a7c主构建失败不得报告为通过，F02仍未关闭。

生成迁移草案索引恢复增量计划（基线a98e49b9，快照f02-crud-migration-index-recovery-20260927）：显式业务迁移注册前检查发现SQL Server租户索引嵌于CREATE TABLE条件，表已创建但索引未完成时会被重跑跳过。先以生成产物的独立索引守卫Unit建立有效RED，再将CREATE CLUSTERED INDEX移到建表块之后并按sys.indexes的表ID/索引名精确探测。新增双Provider Integration：SQL Server执行精确建表前缀模拟未记账半完成，随后执行完整生成SQL及再次重跑，确认索引、原行与表结构；MySQL仍为单条原子建表含索引，验证首次/重复及原行保护，不声称其能修复外部删除索引。保持草案后缀、人工编号/恢复评审、所有权和现有MySQL SQL不变，不自动执行或注册业务迁移。同步相关生成fixture，聚焦Unit/生成器回归、Integration编译及分片发现、命名/治理/影响计划与独立复审，双库实际执行交新SHA Actions，完整F02不关闭。

基线a98e49b9远端主构建108543807573的Unit/Compatibility/Architecture步骤均成功，两个企业样例成功；独立应用108543807478实际194/194、零跳过，两库上传diagnose.log确认根/API/Migrator冻结档案一致，字节保护通过。其主CI36292021696与API/Worker Native尚在运行，不计整体通过。本轮索引守卫Unit有效RED为1失败/0通过；最小修正及fixture同步后 `pnpm test:dotnet:unit -- --selection code-generation-realtime` 691/691、零失败/跳过，Release零警告/错误。治理55/55、命名33/33、SQL安全5/5，零跳过；影响计划命中CodeGeneration及integration-matrix。新增Unit1、Integration2，矩阵Unit3390→3391、Infrastructure182→184、Full1079→1081，不降低门禁。真实双库草案DDL尚待新SHA Actions，不能用结构Unit或Integration发现代替执行证据。

最终串行快速验证：Integration Release --no-restore构建零警告/错误，首次夹具缺MySqlGuidStorageMode命名空间的编译失败已补using修正，不计行为RED；`pnpm test:integration:partitions` 实际发现1081=173+173+494+184+57，无遗漏/重复。`pnpm test:aot:analyzers` 退出0、零警告/错误，仅分析不是Native发布；最终治理55/55。独立复审终检无新增阻断，核对SQLServer守卫精确范围、MySQL未改变、完整生成SQL/半完成前缀/旧行及索引列保护、矩阵与文档边界，复审未执行.NET/容器。只提交本任务七个文件，保留object-comments开工状态；不自动采用草案，不新增公共迁移契约，应用业务注册、真实HTTP权限/租户CRUD及完整F02仍后续，Capacity-not-verified不变。

应用迁移清单失败关闭增量计划（基线dd178a0a，快照f02-migration-manifest-failclosed-20260927）：FrameworkManifestMigrationScope在清单/迁移库存/状态缺失时返回null，Runner随后选择全量嵌入脚本，可能扩大固定预设执行范围。先用内部只读解析Unit及双Provider公共Runner建立失败回归，要求独立应用的缺失/目录占位/空状态/unscoped/空预设/残缺脚本条目明确失败，原输入字节不变；通过Unit friend验证现有internal边界，不扩大public API。保持无应用标记的框架与旧工作区既有未限定兼容、显式恢复Through上界清单。再在读取器以应用fullnet-app.json标记区分严格模式，并把范围读取移至连接配置解析之前。无数据库Unit证明失败前序，现有新应用双库实际迁移及恢复分组交新SHA Actions；不新增业务脚本来源、公开迁移契约或全量F02结论。

同轮Actions缺陷：dd178a0a的MySQL企业样例作业108546674198在两个浏览器用例之后抛ERR_STREAM_WRITE_AFTER_END，bootstrap teardown发送kill后立即end日志流，未等进程close，可能让停机尾部输出写入已结束流。先提取既有顺序并建立五项纯Node回归，有效RED五项全失败；改为等待close及stdio关闭，随后unpipe和finished排空日志，已经killed仍等待，超时分两阶段强制停机并失败传播，两个宿主通过allSettled均清理后再处理依赖资源。补真实Node子进程验证；Windows不执行SIGTERM处理器但不跳过用例。现有provisioner脚本加入该回归，并作为client CI轻量步骤执行，不加重型本地栈或降低浏览器门禁。根本计划、公开业务迁移入口与生产角色不改变；数据清单缺陷和测试基础设施竞态可在同轮分别验收。

本轮快速证据：清单20项有效RED为13失败/7通过，首次原始字符串编译错误修正后才计RED；修正后 `pnpm test:dotnet:unit -- --filter "FullyQualifiedName~FrameworkManifestMigrationScopeTests|FullyQualifiedName~FullNetMigratorHostTests|FullyQualifiedName~MigratorWorkflowTests" --minimum-expected-tests 41` 为41/41、零失败/跳过，Release零警告/错误。`pnpm test:e2e:provisioner` 最终43/43、零失败/跳过（含真实Node子进程）；中间旧源码契约断言仍要求kill/end导致1失败，改为检查两个角色调用新的清理入口，未删除角色边界断言。三个Node脚本语法检查退出0。Integration Release --no-restore构建零警告/错误；分片发现1081=173+173+494+184+57，无遗漏/重复，仅发现不是数据库执行。HostModuleProfile及迁移宿主所有权/引用扫描Architecture7/7，治理55/55、命名33/33。Unit矩阵3391→3411仅对应实际20项新增，Integration数量不变。受影响计划首次缺边界参数退出1，补 `--snapshot f02-migration-manifest-failclosed-20260927` 后退出0，命中migrations和integration-matrix，实际双库执行交新SHA Actions。

dd178a0a独立应用作业108546674259日志确认194/194、零失败/跳过，SQL Server企业样例108546674244和current迁移恢复108546674270成功；MySQL样例失败与主构建/legacy迁移仍未结束，不能报告整体CI成功。当前未提交源码包检查为5通过/3因脏输入跳过，待提交后重新运行；不计8/8通过。应用严格清单保护依赖内容根声明fullnet-app.json，不声称所有发布模式或声明被移除后的保护；业务迁移显式注册、完整F02及生产容量仍未验收。

独立复审无阻断，检查清单兼容模式及连接解析前序、双Provider公共Runner与字节保护、close/stdio/log排空、已发信号/强杀/超时和双宿主allSettled。复审独立执行provisioner43/43及helper6/6、零跳过，未执行.NET/容器/浏览器；真实MySQL样例退出缺陷仍需新SHA Actions终态，不能以辅助测试代替。

f52ffc35终态核对及夹具修复切片（快照f02-generated-recovery-fixture-20260927）：独立应用194/194、双库企业浏览器各2/2、客户端及两个迁移恢复分组均成功；API/Worker Native成功。主构建108549646634的Unit3411、Compatibility12、Architecture232全通过，但受影响Integration301为299通过/2失败，主CI与汇总门禁失败，不计整体通过。两个失败都是GeneratedMigrationDraftRecoveryTests调用旧CreateProject入口时漏了必需IsActive，生成SQL前即抛异常，故此前新增索引恢复尚无实际双库通过证据。此次只修测试输入，不改变生产生成器或降低必需字段校验。把实际Schema提取为Unit/Integration显式Compile链接的同一测试夹具，以双Provider无数据库预检先复现同异常（2失败/0通过/0跳过）；补非空Boolean IsActive，真实INSERT显式true并验证重跑后仍true，列数由3调整为4，保留半完成建表、索引类型/列顺序、原行与重复执行断言。预检初次GREEN中MySQL预期误写tinyint(1)，实际生成器为boolean，修正测试断言，不改变SQL。Unit新增2项，矩阵3411→3413；Integration不增删。快速预检/Integration编译、分片发现/治理、只读复审后提交，实际DB执行交新SHA Actions；业务注册与完整F02仍不关闭。

本切片最终快速证据：`pnpm test:dotnet:unit -- --filter "FullyQualifiedName~GeneratedMigrationRecoveryFixtureTests|FullyQualifiedName~CrudArtifactGeneratorTests|FullyQualifiedName~FullNetCrudSchemaTests" --minimum-expected-tests 54` 为54/54，零失败/跳过，Release零警告/错误；Integration Release --no-restore构建零警告/错误，`pnpm test:integration:partitions` 为1081项无漏/重。治理55/55；快照slice影响计划命中CodeGeneration、integration-matrix及smoke。独立只读复审确认两测试项目精确链接同一内部夹具、未新增生产API、原恢复断言保留，未执行.NET/容器/浏览器。当前本地未执行实际DDL；双库断言待新提交Actions，不以预检/编译替代。仅提交本任务七个文件，保留object-comments既有状态。

独立应用真实栈清理收口（基线01c1a5a0，快照f02-created-app-shutdown-20260927）：该SHA双库CI尚在执行时继续检查共性问题，发现created-app-real-stack.mjs仍在kill(SIGTERM)后立即end日志，保留与此前企业MySQL失败相同的时序缺陷。只提取真实入口finally到cleanupCreatedApp，先保留旧逻辑建立无数据库回归：2失败/0通过/0跳过，停机末尾日志丢失与错误未等待均复现，并出现未等待error的异步活动。改为复用既有stopLoggedProcess，等待close及日志排空；try/finally确保停机失败仍按既有尽力清理语义停止DB/Redis并删除本次自建临时目录，停机错误继续传播。两个入口未复制新的停机算法，不改变应用或框架运行时。新2个Node用例经test:templates glob自动进入CI；.NET测试及矩阵不变。本地 `node --test tests/templates/created-app-cleanup.test.mjs tests/e2e/admin-real-stack/scripts/stop-logged-process.test.mjs` 8/8、零失败/跳过，两个脚本语法检查退出0；治理55/55，快照slice影响计划为none，不要求.NET构建或Integration重测。真实独立应用双库停机及日志仍须新SHA模板作业验收，业务SQL显式注册、真实业务HTTP CRUD与完整F02不关闭。

局部独立复审无阻断，并独立复跑Node8/8、零跳过；确认删除仅作用于调用方本次mkdtempSync目录，共享停机工具与相对导入正确，正常先日志finished、失败仍清两项依赖和目录，测试顺序断言不被容器尽力清理catch吞掉。复审未运行.NET/DB/browser。提交前核对本任务diff/check和分支，只提交四个文件，保留既有object-comments状态。

限定迁移清单与实际资源对应收口计划（基线2d03ab5a，快照f02-migration-resource-inventory-20260927）：当前长作业仍在执行，先沿显式业务迁移接入前序检查共性边界。Runner仅按资源后缀匹配allowed名称，未知项会静默忽略，截短名称也可能后缀匹配；读取器HashSet.Add会默默去重。先以双Provider公共Runner的坏连接配置建立无数据库RED，要求未知、截短、大小写漂移及带路径名称在连接解析前拒绝；显式清单重复名同样拒绝，输入字节保持不变。实现时限定清单每个名称必须精确匹配当前程序集SqlServer/MySql成对资源，保持无清单框架兼容和恢复Through子集；资源选择改为Provider片段后完整文件名相等，不扩大Public API或业务SQL自动来源。补实际资源正例与单库缺配对的纯校验，聚焦Migrator Unit、角色Architecture、Integration编译/发现、治理和只读复审；实际预设应用与迁移恢复仍交新SHA Actions。仅证明资源名称存在与配对，不证明SQL摘要、预设归属、数据库状态或任意业务迁移的正确性；完整F02仍不关闭。

本切片有效RED10/10失败、零跳过，SQLServer先落入数据库错误、MySQL先落入连接格式错误，重复项没有拒绝，与预期缺失一致。修正后 `pnpm test:dotnet:unit -- --filter "FullyQualifiedName~FrameworkManifestMigrationScopeTests|FullyQualifiedName~FullNetMigratorHostTests|FullyQualifiedName~MigratorWorkflowTests" --minimum-expected-tests 55` 为55/55、零失败/跳过、Release零警告/错误；实际新增14项（公共Runner8、重复名2、真实资源子集1、无清单1、缺单库配对2），Unit3413→3427，Integration不增删。Integration Release --no-restore构建零警告/错误，分片发现1081项无漏/重，宿主/迁移归属Architecture7/7、治理55/55。影响计划命中migrations与integration-matrix；本地未连接数据库或启动容器。独立复审无阻断，确认名称提取与Through237夹具一致、子集与无清单兼容、240对源文件精确配对；复审未运行.NET/DB/browser。提交前只暂存本任务六个文件，保留object-comments既有状态；真实预设迁移和历史恢复待新SHA Actions，不以资源存在或发现报告代替执行。

应用拥有的业务迁移验收计划（基线49e63750，快照f02-application-business-migrations-20260927）：框架接口IDatabaseMigrationRunner已允许应用替换运行入口，本轮不新增公共API或默认模板自动来源。只在独立Demo验收应用的Host.Migrator显式装配应用自有包装Runner，注入既有具体DbUpMigrationRunner，先完成冻结预设框架迁移，再执行人工编号的acme.catalog双库草案，成功后返回总脚本数以复用现有先迁移后播种工作流。业务SQL通过应用Migrator项目的两个精确EmbeddedResource登记，并使用与框架资源不碰撞的稳定记账身份；不改受管框架、API/Composition或框架清单。真实栈先运行本应用CLI的既有生成/再生成保护验收，再显式采用两个SQL草案；首跑带Development播种要求业务执行1项，第二次不播种要求框架/业务均执行0项。Node预检验证双库输入、重复接入、人工SQL保护及宿主结构冲突；链接同一C#Runner夹具到Unit，先验证取消/框架失败不得进入业务阶段，普通Unit编译证明夹具语法。真实临时应用编译/双库DbUp执行由Actions完成，保持API/Worker不依赖迁移执行。该切片只验收应用拥有的显式迁移与记账，不声明业务HTTP CRUD、租户隔离、Vue或完整F02通过。涉及tests/templates/support/application-business-migrations.mjs及其Node回归、Runner夹具与Unit链接/回归、created-app-real-stack串接、矩阵及本计划/开发指引；先RED再实现，快速验证和独立复审后推送，等待真实模板作业。

本轮快速验证与边界：Node 文件采纳/执行证据校验先7/7有效RED，实现后与既有应用CLI编排回归合计20/20、零失败/跳过，包含首次双库原文采纳、重复字节稳定、缺单库/所有权/重复资源登记拒绝、人工SQL/Program漂移与源草稿变更保护。C#包装器先6/6有效RED，修正后 `pnpm test:dotnet:unit -- --filter "FullyQualifiedName~ApplicationMigrationRunnerTests|FullyQualifiedName~FullNetMigratorHostTests|FullyQualifiedName~MigratorWorkflowTests" --minimum-expected-tests 27` 为27/27、零失败/跳过，Release零警告/错误。第一次GREEN编译因DbUp Scripts是IEnumerable而非集合属性失败，按现有Runner改为Count()后重新编译通过。新增实际6Unit并更新矩阵；Integration未增删。slice影响计划为integration-matrix；分片1081项无漏/重、治理55/55。Architecture最初宽子串仅发现6项、最低7策略拒绝；检查测试定义后追加完整HostModuleProfileTests，保留原选择，改用最低8，最终8/8、零失败/跳过。语法检查退出0。本地未构建临时生成应用、未连接数据库或启动容器；应用自有Program/资源DI及双库首次/重复DbUp结果仍待新SHA Actions，不能从Unit或摘要推断实际业务CRUD。

前序远端证据更新：01c1a5a0核心作业108559727547成功；下载其fullnet-quality-reports实际TRX确认SqlServer_generated_tenant_index_recovers_after_table_creation_and_preserves_rows与MySql_generated_atomic_table_and_index_repeat_preserves_rows均Passed，受影响Integration301/301、零跳过，企业Integration6/6、Linux Unit3413/3413、Compatibility12/12、Architecture232/232通过。此前夹具失败已有实际双库DDL修复证据，但这不证明本轮新增应用Migrator采纳路径。49e63750独立应用、MySQL/SQLServer企业样例及客户端作业已成功，核心及恢复组仍需对应SHA终态。完整F02仍不关闭，PR保持Draft，不合并或发布，Capacity-not-verified不变。

本切片独立只读复审范围内无阻断：核对框架/业务journal身份不重叠，接口最后注册且具体Runner独立注入避免递归，五参数框架构造保留既有scope/Contract配置；失败/取消禁止后续播种，人工入口/SQL/源草稿漂移拒绝覆盖。复审独立Node10/10、零跳过，两脚本语法与diff检查退出0；未运行.NET、DB或容器。只暂存本任务九个文件，原object-comments.json状态保留。双库实际应用执行与重复记账仍待提交后Actions。

生成业务 HTTP 拒绝验收计划（基线a27ecd05，快照f02-generated-http-denial-20260927）：上一业务迁移切片已推送且等待独立应用双库终态，本轮复用现有生成模块/模块入口/Composition/授权贡献者CLI验收，在真实栈应用中完成接线后再运行已登记业务迁移和API。增加固定catalog/products五路由（列表、详情、新增、更新、硬删除）的真实请求矩阵，匿名必须401，真实Host引导管理员必须403；保持先登录与既有Settings实际CRUD，拒绝用错路由404、500或重定向替代授权。HTTP工具只记录主体类别、方法、URL、响应及结果，不写入Bearer令牌；失败保留已执行请求证据，不把未执行项计入成功。无数据库Node替身先验证正确请求矩阵、失败/重定向/请求异常与脱敏证据，再接入真实栈。复用现有模块与授权编译及人工再生成保护，不新增产品API、默认模板或迁移；不把这10次拒绝请求称为允许租户CRUD或跨租户数据隔离验收。涉及新application-crud-http-denial.mjs及Node回归、created-app-real-stack串接、总计划与first-crud指引；本地只运行聚焦Node、治理、影响规划和语法检查，真实生成应用编译/双库HTTP由Actions证明，复审后提交推送，保持Draft且不合并发布。

本轮快速证据：HTTP工具6项有效RED均因能力缺失失败（0通过/跳过），实现后补重定向、Host认证失效及非ProblemDetails拒绝，新增9项；`node --test tests/templates/application-crud-http-denial.test.mjs tests/templates/application-crud-module.test.mjs tests/templates/application-crud-host-wiring.test.mjs tests/templates/application-crud-authorization.test.mjs` 为50/50、零失败/跳过。检查五路由实际生成器使用GET列表/详情、POST新增、PUT更新、POST硬删除，身份错误映射为identity.session_not_active及authorization.permission_denied。继承原框架Profile注册，Catalog默认AddMigrationServices为空，不能将API AddServices装入Migrator。两脚本语法、diff检查退出0，治理55/55；slice影响计划none（仅模板验收脚本，不修改生产.NET或Integration矩阵），未运行本地.NET/容器/浏览器。真实生成应用的新增模块装配、API请求、授权目录与双库拒绝结果仍由新SHA Actions验证。

上一切片已取得真实双库证据：a27ecd05独立应用作业108565751963成功，实际日志206/206、零失败/跳过，SQLServer/MySQL真实栈均执行。下载fullnet-created-app-real-stack-reports的两个application-migration-results.json，均为first.frameworkScripts=99、first.applicationScripts=1，repeat两项均0；显式采用业务SQL、应用自有Program/EmbeddedResource/DI编译及DbUp首次/重复记账切片可据此通过，不延伸为业务HTTP或完整F02。双库企业样例作业亦成功；主CI核心及恢复组仍需终态。原object-comments状态保持，PR不合并发布，Capacity-not-verified不变。

独立只读复审范围内无阻断，并独立复跑新HTTP9及模块/HostWiring/Authorization合计50/50、零跳过，两脚本语法及diff检查退出0。源码确认Login Handler查询HostScope且JWT保留host，既有Settings真实CRUD验证有效认证；五路由和标准授权机码一致。Migrator仅调用AddMigrationServices，Catalog空默认不会注册生成服务或授权贡献者。复审未运行.NET/容器，未修改文件。CLI/build的300s、真实栈用例15min及job90min门槛保持原值，不能无证据放宽，新增冷编译耗时交Actions观察。只提交本切片五文件，保护object-comments原状态；双库HTTP仍待新SHA，整体F02未关闭。

生成业务租户CRUD与乐观锁验收计划（基线d113f9e1，快照f02-generated-tenant-crud-20260927）：沿用fullnet-module-delivery、writing-plans与TDD，在现有生成模块/授权/迁移/匿名Host拒绝之后推进允许写入。只使用已认证Host引导管理员的真实/api/v1/tenancy/available目录，精确选择Development local租户，再PUT /tenancy/context取得新有效令牌及服务端context，拒绝无local、错租户或缺令牌；不直接写DB、不构造Claims、不在业务请求传入TenantId。生成catalog/products执行新增201、按ID/列表读取200、版本1更新200为2、旧版本更新409并再次读取保持新值、旧版本删除409并再次读取保持记录、现版本硬删除200、后续按ID404及列表不含该ID。验证UUIDv7、TenantId、Version字符串和固定机器码，报告只包含响应及结果，省略上下文签发响应正文并脱敏所有已知令牌。无DB Node替身先验证链路、错误状态、错误目录/上下文、租户/版本不匹配、冲突后数据漂移与异常证据；真实SQL/HTTP同场景双库交新SHA Actions。此切片是Host管理员经授权切入租户上下文的CRUD，不证明普通租户账号精确权限、无权限租户、跨租户隔离或完整F02。仅新增application-crud-tenant-http.mjs及Node回归、真实栈调用、first-crud说明及本计划；无产品.NET/API/Schema/迁移变化，无矩阵计数变化，本地聚焦Node/治理/语法/影响规划，复审后提交推送，不合并发布。

本轮快速证据：新工具首11项有效RED全部失败、零跳过；契约对照发现TenantContextDescriptor使用tenantId/scope，先修正Node响应替身并增加各负例预期请求数，旧实现出现8失败/3通过后再修正文读取，避免负例因前置失败而误通过。增加签发JSON异常固定消息和业务断言反射令牌脱敏两项，`node --test tests/templates/application-crud-tenant-http.test.mjs tests/templates/application-crud-http-denial.test.mjs` 为22/22（新增13、既有9）、零失败/跳过；治理55/55，两脚本语法及diff检查退出0。slice影响计划none；本轮只改验收脚本与文档，生产.NET/Schema/迁移和测试矩阵不变，本地未.NET/容器/浏览器。真实租户API切换、生成UUIDv7/Version/TenantId、双库Dapper CRUD及冲突后保持仍待新SHA Actions，不从Node替身升级为通过。上下文签发正文整体省略，所有已知Host/新租户令牌同时从持久化报告和抛出异常脱敏；签发JSON不合法时不能让JSON解析器摘录未知凭据。业务步骤只提供Name/Version及服务端返回Id。

前序d113f9e1独立应用作业108566951367成功，实际日志215/215、零失败/跳过，双库真实栈实际运行。下载fullnet-created-app-real-stack-reports，mysql/sqlserver的application-crud-http-denial.json均completed=true且responses=10，匿名5条401、Host5条403；对应脚本已逐项校验标准ProblemDetails机码。生成模块、Composition、授权Contributor及业务迁移进入真实API后的拒绝切片可据此通过，不能替代本轮正向CRUD或普通租户/跨租户隔离。

本切片只读复审范围内无阻断：源码核对TenantContextSummary.id、TokenResponse.accessToken/context.tenantId/identifier/scope及tenant:{Id:N}一致；切换要求Host Actor、活动会话、switch权限并重读权威快照，超级管理员在有效租户上下文获得Tenant权限，符合限定主体。生成Int64 WriteAsString/AllowReadingFromString、初始1/更新+1、stale写409机码、硬删除返回原existing记录均吻合。独立Node22/22、零跳过，两脚本syntax及diff退出0；复审未运行.NET/容器。只提交本任务五文件，object-comments原状态保护；真实双库正向链路待提交后Actions，完整F02未关闭，不合并发布。

生成业务双租户隔离验收计划（基线5e309f9d，快照f02-generated-tenant-isolation-20260927）：沿用模块交付/计划/TDD，从已完成正向CRUD的有效local上下文继续，不复用已因切换失效的旧Host令牌。正向工具只在内存返回最后令牌与local租户ID，持久化报告仍不含凭据；隔离工具先经context API返回Host，真实POST tenancy/tenants创建本次独立DB内的第二租户，再使用每次签发的新令牌依序进入local/第二租户。每个租户创建一条版本1产品；第二租户GET/PUT/DELETE第一租户ID均404且列表不含，第一租户GET/PUT/DELETE第二租户ID均404且列表不含。切回后按ID验证双方Name/TenantId/Version未变，只删除各自记录并验证404/空列表，不删除或修改他方表/DB。上下文签发正文全省略，全部已知轮换令牌脱敏，报告按已执行请求保存；负例检查准确请求步骤，拒绝跨租户读泄露、列表泄露、跨更新/删被接受、尝试后内容改变、错误上下文/租户及传输失败。双库实际API/Dapper行为交新SHA Actions，本地仅Node/治理/语法/影响规划；不把Host Actor真实切上下文等价为普通租户账号精确权限。新增隔离helper/Node及既有CRUD内存返回/测试、真实栈串接、first-crud指引和本计划；不改产品.NET/Schema/迁移/默认模板或矩阵，复审后推送，不合并发布，F02仍待其余权限/客户端/Worker验收。

本轮快速证据：新增隔离13项与既有CRUD内存续接1项有效RED共14失败，原CRUD12个负例仍通过，零跳过；实现后 `node --test tests/templates/application-crud-tenant-isolation.test.mjs tests/templates/application-crud-tenant-http.test.mjs` 为26/26、零失败/跳过。核对27个实际计划步骤的轮换Token使用，20次业务请求、双向6次404、双方列表过滤与Name/TenantId/Version保持，以及分别删除后双边404/空列表；负例固定预期请求数，防止前置错误掩盖跨读/写/删泄漏。治理55/55，隔离/CRUD/真实栈三脚本syntax及diff检查退出0；slice影响none，本轮不改产品.NET、SQL、Schema、迁移或测试矩阵，未本地.NET/容器/浏览器。新Tenant使用受控isolation-probe及isolation-probe.invalid，位于本次真实栈隔离DB，结束由既有清理销毁；业务验收只删除自己的两个产品。已有正向CRUD5e309f9d仍待CI终态，不提前据此计隔离通过。

前序正向CRUD切片真实验收更新：5e309f9d独立应用作业108568230606成功，实际日志228/228、零失败/跳过，SqlServer/MySql真实栈均执行。下载fullnet-created-app-real-stack-reports，双库application-crud-tenant-http.json均completed=true，responses=13（2上下文+11业务）、2次409、read-deleted为404；脚本逐项检查Create/Read/List/Update版本1→2、冲突后保持、硬删除、列表移除、UUIDv7及服务端TenantId。Host管理员授权进入local的CRUD/乐观锁切片据此通过，不延伸为普通租户精确权限或本轮双租户隔离。

本切片独立只读复审范围内无阻断：新租户允许不绑定套餐开通且有效，isolation-probe/.invalid输入合法，模块内Quota初始化无需新增业务journal或Files依赖；6次context切换使用最新Token，服务端ActiveTenantId/effectiveScope校验令旧上下文失效。生成读/列表/更新/删均有TenantId过滤，跨更新零行后的FindById也按租户过滤，跨删除先读不到即404，匹配双向6项及双方后续保持核验。调用方仅拆内存返回，不序列化凭据。复审独立Node26/26、零跳过，syntax/diff通过；未修改文件或运行.NET/容器。只提交本任务七文件，object-comments原状态保留；真实27请求/20业务/6跨拒绝待新SHA Actions，结论仍限获授权Host Actor有效租户上下文，F02未关闭，不合并发布。

普通账号只读权限增量计划（基线72f60d8844076c9078bcc83f333c1323c08a8e4a，快照f02-created-app-read-permission-20260927）：在独立应用真实栈中通过公开API创建非系统、非超级管理员自定义角色与普通Host账号，角色仅包含tenancy.tenants.switch和catalog.products.read。真实登录、携带服务端CSRF Cookie完成强制首次改密，再切入local租户，使用/api/v1/me核对有效租户、非超级管理员和精确权限集合。管理员创建一条产品，普通账号列表/读取成功，创建/更新/删除均为403 authorization.permission_denied；管理员再读确认Id/TenantId/Name/Version不变并删除自己的夹具。管理员切回Host后仅内存移交最新令牌给既有验收，不写入报告。新helper与Node负例检验请求顺序、凭据隐藏、失败即停和数据保持，真实账号/会话/双库行为只由Actions认定。不会绕过首次改密、直接SQL播种或伪造Claims，不改变生产接口、公共契约或权限规则。本切片只证明只读精确权限，完全无产品权限及各写权限独立正向仍后续。按RED→实现→聚焦Node/治理/影响检查→独立只读复审→开发分支提交推送执行。

本增量快速证据与复审修正：最小占位实现的15项RED全部失败，完整链实现后15/15。独立复审沿真实HostRoleManagementService和TenancyAuthorizationContributor定位父页面闭包缺失：switch必须同时具备tenancy.tenants.read，否则角色赋权返回identity.roles.action_requires_page。先修改预期权限建立有效RED（15失败/2通过），再补租户导航读取权限，保持catalog仅read；Host me精确read/switch两项，Tenant me精确三项。Node替身在赋权步骤直接校验该闭包，新增管理员列表确认403创建未新增行及回Host错误上下文负例，最终新17项/四组联合52项均通过、零跳过。治理55/55，语法/diff和inner影响none；无本地.NET/容器。真实账号与双库新切片仍待新SHA Actions。

72f60d88远端独立应用作业108569632291（主CI36301418990）成功，日志241/241、零失败/跳过。下载sqlserver/mysql application-crud-tenant-isolation.json均completed=true，27响应、20业务请求、双向跨租户404共6、原行保持2及自有删除2；隔离真实证据已取得。两个企业样例和客户端作业成功，主构建、两组恢复及API/Worker Native仍运行，不报告整体通过。F02仍未关闭：普通账号只读新切片待新SHA，完全无产品权限账号、各写权限独立正向、应用Worker/OpenAPI/Vue及完整人工再生成仍后续；Capacity-not-verified保持。

终检：独立只读复审确认父页面闭包已收口且无新增阻断，复跑四组Node52/52、零跳过；最终本地四组52/52、治理55/55、三脚本语法、任务diff均通过，inner影响none。仅验收脚本和直接相关文档共5文件，未改产品.NET/SQL/Schema/迁移或测试矩阵，未碰既有object-comments.json。普通只读真实22HTTP/9业务继续待新SHA Actions，未据此升级Verified。

无产品权限普通账号增量计划（基线a6d2d1a83ce2b4a560228078e58d3356a06f12d5，快照f02-created-app-no-product-permission-20260927）：复用既有真实账号/自定义角色/CSRF首次改密链，新增固定无产品权限入口，仅分配tenancy.tenants.read/switch导航闭包，使用独立账号与角色避免污染只读正例。Host及Tenant me精确集合均只有这两项，非超级管理员且改密已完成；管理员预建产品后，该账号列表/按ID读取/创建/更新/删除全部403 authorization.permission_denied。管理员再读及唯一行列表确认原数据与版本保持且没有新增，再删夹具并返回Host，最新令牌仅内存续接。共享内部验收实现不允许调用者自定义权限，两个固定入口各生成独立报告；Node先RED，再覆盖五入口误允许、错误机码/响应类型、意外产品授权、行漂移和凭据隐藏。真实两库账号/会话/业务行为由新SHA Actions证明，当前a6d2只读链远端运行中。仅调整验收脚本与直接文档，不更改生产授权/API/SQL/迁移或发现数；独立复审、快速验证后推送指定开发分支。各写权限独立正向、应用Worker等仍后续。

本增量验证：新无权限入口有效RED为1失败/17既有通过，失败原因是能力尚未实现；复用后18/18，再补12个负例（五路误放行、意外角色/有效权限、标准错误、凭据JSON、原行漂移及新增行）至本组30/30，四组联合65/65、零跳过。无权限正例同时核对五个真实路径/方法和同一普通账号Tenant令牌，负例以准确请求数防止提前失败假绿。治理55/55、三脚本语法/diff、inner影响none。独立只读复审无阻断，复跑65/65；原只读成功要求完整保留，两套账号/角色不同，两个固定入口禁止通过options扩大权限。仅5个验收/文档文件；未改生产.NET/SQL/迁移/矩阵，不执行本地.NET/容器/浏览器。当前a6d2d1a8双库作业仍运行，不将本地结果算作真实账号证明。无权限真实22HTTP/9业务/5次403仍待新SHA Actions，F02与Capacity-not-verified保持原状态。

普通账号创建权限增量计划（基线87b2eaa1e567ba832a12756d3d8a2cdbfea90b71，快照f02-created-app-create-permission-20260927）：复用固定账号权限验收内核，增加第三个固定Create入口，独立账号/角色只授予租户read/switch与产品read/create页面闭包。首次改密与Host/Tenant me精确集合仍不可跳过。普通账号列表/读取管理员预建行后，创建自己的产品201，核对不同UUIDv7、可信TenantId、Name及字符串Version1；对管理员原行更新/删除必须403。管理员原行读取及列表核对两行身份/内容/版本完整，再清理两行并确认普通创建行读取404，返回Host仅内存续接。新增固定结果与独立application-crud-create-permission.json，不能通过options增加其他权限。先成功门禁RED再实现与负例，保留Read与None现有30项，实际创建/拒绝/清理由新SHA双库Actions证明。当前a6d2只读及87b2无权限远端尚未终态；优先修复出现的真实失败。仅测试脚本/直接文档，独立复审与快速验证后推送指定分支，不改变生产权限层级、SQL、契约、迁移或计数。Update/Disable独立正向及Worker等仍后续。

创建权限增量快速证据：新入口RED1失败/30既有通过，原因是创建验收尚未实现；实现后31/31，再补16个负例（权限缺失/意外Update、创建被拒、错Tenant/版本/UUID或覆写原ID、Update/Delete误放行、原行漂移、创建未持久化/被改写、清理错行/仍可读/错误机码/错误Host上下文），最终本组47/47、四组82/82，零失败/跳过。治理55/55、三脚本语法/diff、inner影响none。独立只读复审无阻断且复跑82/82；确认Create的Read父页面闭包、三个账号/角色隔离、精确Host/Tenant权限、两行持久化/清理与旧Read/None要求保持。真实新切片24HTTP/11业务/2读允许/1创建允许/2写拒绝仍待新SHA双库报告，不将Node替身算作实际账号/数据库证据。仅验收/直接文档5文件，无本地.NET/容器/浏览器，不修改生产SQL/契约/迁移或计数。

a6d2d1a8远端独立应用作业108571405296（主CI36302058290）成功258/258、零失败/跳过。下载两库application-crud-read-permission.json均completed=true、22响应，result为businessRequests9/readAllowed2/writeDenied3/rowPreserved=true，真实普通账号登录、CSRF首次改密、me精确权限与只读HTTP链已经取得证据。87b2无权限独立应用作业108572101510已开始运行，仍未报告成功。完整主CI与Native未终态核对，不报告整体通过；F02还需无权限/创建当前远端、Update/Delete独立正向、应用Worker/OpenAPI/Vue及完整人工再生成，Capacity-not-verified不变。

普通更新权限增量计划（基线7cdcb6bfd238a5ac53356d822133285c69dfa95f，快照f02-created-app-update-permission-20260927）：新增固定Update入口，独立账号/角色仅产品read/update与租户read/switch；保留真实账号/角色赋权、CSRF首次改密、me精确权限及非超级管理员。普通账号创建403，更新管理员预建行200从字符串Version1到2、Name改变且Id/TenantId保持；旧Version1再次更新409，立即读取确认Version2及内容保持，随后删除403。管理员再读和唯一行列表确认更新真正持久化且无新增，再按Version2清理并返回Host，仅内存续接。独立application-crud-update-permission.json，24HTTP/11业务，2初始读允许、1更新允许、1冲突、2写拒绝。先RED再实现/负例/快速验证/独立复审，双库真实链交Actions。既有Read/None/Create验收必须保持，不改生产API/SQL/迁移/授权或矩阵，不本地.NET/容器。Delete独立正向及Worker等后续。已取得87b2无权限271/271与7cd创建288/288双库证据，7cd API/Worker Native工作流成功，核心作业仍运行，不计整体CI通过。

本增量快速证据：新增Update成功门禁RED1失败/47既有通过，原因是验收能力尚未实现；实现后48/48，补10项负例至本组58/58，四组93/93，零失败/跳过。负例准确请求数覆盖缺Update权限、更新版本/租户错误、陈旧更新误允许/错误机器码、冲突改写、删除越权、更新未持久化/列表旧版本及清理旧版本。治理55/55、三脚本语法/diff、inner影响none；独立只读复审复跑93/93无阻断，核对版本2删除/清理、精确权限及旧Read/None/Create要求保持。仅5文件验收与直接文档，未改生产.NET/SQL/契约/迁移/矩阵，不本地.NET/容器/浏览器；Update24HTTP/11业务真实证据继续待新SHA双库Actions。

远端收口：87b2eaa1独立应用作业108572101510（CI36302305370）271/271，7cdcb6bf作业108572915075（CI36302588033）288/288，均零失败/跳过。两库无权限报告均completed=true、22请求/9业务/readAllowed0/readDenied2/writeDenied3/rowPreserved=true；两库Create报告均completed=true、24请求/11业务/readAllowed2/createAllowed1/writeDenied2/rowPreserved=true/createdRowDeleted=true。7cd主CI及API Native36302588071/Worker Native36302588035终态均success；核心作业108572915256日志确认Unit3433、Compatibility12、Architecture232、受影响Integration301及企业Integration6全通过零跳过。两个迁移恢复分组、双企业样例和客户端成功；按分支选择跳过的全量Integration/其他E2E不计通过，不替代main完整Integration或生成应用Native认证。F02仍需Update当前真实、Delete独立正向、应用Worker/OpenAPI/Vue与完整人工再生成，Capacity-not-verified保持。

普通删除权限增量计划（基线96510a221a7185f465c68bb4a2cf1585f7b91bf8，快照f02-created-app-delete-permission-20260927）：新增固定Delete入口，独立账号/角色仅catalog.products.disable/read与tenancy.read/switch页面闭包，沿现有公开账号角色API、真实登录、CSRF首次改密和me精确权限核验。Schema hard.delete路由仍使用既有Disable权限，不重命名公共机器码。管理员预建Version1产品后，普通列表/读取成功，创建和更新403，不匹配Version2删除409且再次读取原Version1/Name保持；正确Version1删除200返回原行，然后普通GET404/空列表与管理员GET404/空列表分别核对，回Host仅内存续接。新application-crud-delete-permission.json，25HTTP/12业务/1删除成功/1冲突/2写拒绝；不再重复管理员删除已消失行。先RED再实现/负例/快速验证/独立只读复审，真实两库交新SHA Actions。旧Read/None/Create/Update行为不能降低，不改生产.NET/SQL/迁移/契约/矩阵，不本地.NET/容器/浏览器。更新当前真实证据和本轮删除双库完成前不收口权限HTTP验收；F02其余Worker/OpenAPI/Vue与完整人工再生成继续后续。

本增量快速证据：Delete成功门禁有效RED1失败/58既有通过，原因是删除验收尚未实现；实现59/59，再补12负例至本组71/71，四组106/106、零失败/跳过。负例准确请求数覆盖缺Disable/意外Update、创建更新误放行、不匹配删除误允许、冲突改变、正确删除拒绝/错行、普通及管理员读/列表仍存在。治理55/55、三脚本语法/diff、inner影响none。独立只读复审无阻断且复跑106/106，确认hard.delete实际仍用Disable及页面Read，Version2合法但不匹配，正确1删除返回原行，两主体分别读404/空列表；旧四分支精确权限和清理要求保持。五个固定权限入口皆已接入，真实Delete25HTTP/12业务与当前Update远端仍待对应SHA报告，不将Node替身作为实际数据库证明。仅5验收/直接文档文件，未改生产.NET/SQL/契约/迁移/矩阵，不本地.NET/容器/浏览器。F02还需当前权限双库证据、应用Worker/OpenAPI/Vue及完整人工再生成，Capacity-not-verified保持；未合并/发布。

独立应用OpenAPI接入增量计划（基线e0ed263e9677a5a6261a76534d5c5990999833f6，快照f02-created-app-openapi-20260927）：真实API就绪后匿名读取/openapi/v1.json，将生成的products.generated.openapi.json作为只读预期，与实际文档五个业务操作逐项比较规范化路径（只兼容尾斜杠）、operationId、成功状态、三种写请求字段以及五种成功响应字段（列表比较items）。要求真实文档每个产品操作声明Bearer安全方案，三个写请求不能出现TenantId/Id/审计字段；只比较字段集合，不虚称数字/字符串schema全部一致或HTTP错误状态全面接入。引用仅允许文档内components/schemas且拒绝外部、缺失/循环；保存逐项实际比较和失败部分证据，预期生成文件字节不变。Node先RED后实现并补遗漏路由/错误ID/无保护/成功状态或请求响应字段漂移/坏引用等负例，快速验证/独立只读复审后推送。真实文档服务由新SHA两库Actions证明，不本地临时应用构建或容器，不修改产品.NET/生成器/SQL/契约/迁移/矩阵。本轮Generated API文档接入子集完成后仍不关闭F02，完整错误契约、Vue、应用Worker与人工再生成后续。965更新作业108581146789已成功299/299，双库更新报告completed/24HTTP/11业务/1更新/1冲突/2拒绝；e0删除作业仍运行。

本增量快速证据：18项门禁中初始14项有效RED全部失败（验收尚未实现或未执行请求）；实现14/14，再补HTTP503/错误ContentType/坏JSON/期间输入字节变更4负例至18/18，五组联合124/124，零失败/跳过。治理55/55、三脚本语法/diff和inner影响none；成功和失败都证明实际请求已执行，生成输入字节保护用期间变更复现。仅验收/直接文档5文件，无产品.NET/生成器/SQL/迁移/矩阵变化，无本地.NET/容器/浏览器。真实五操作文档比较仍待新SHA双库报告，不把本地替身或字段子集视为全面OpenAPI认证。

96510a22独立应用作业108581146789（主CI36305477068）成功299/299、零失败/跳过。下载两库application-crud-update-permission.json均completed=true、24请求、businessRequests11/readAllowed2/updateAllowed1/versionConflicts1/writeDenied2/rowPreserved=true；真实普通账号更新、乐观锁与持久化已取得证据。e0ed删除独立应用作业108582017814仍运行；965主CI及两个Native工作流仍运行，不计整体通过。五固定权限场景的全部真实收口仍需Delete新SHA，F02其他OpenAPI完整错误/类型、Vue、应用Worker及人工再生成未关闭，Capacity-not-verified保持。

终检：独立只读复审无阻断，复跑五组124/124、零跳过，syntax/diff通过；核对生成器实际路由参数名由生成契约作为预期（不硬编码id）、五WithName/Produces、封闭DTO/PagedResult.Items、模板MapFullNetOpenApi与Host安全转换。appRoot预期路径、匿名文档读取时点、部分失败报告与字节保护正确。反射文档最终引用/数组形态继续由新SHA实际双库证明，不由Node合成文档推定通过。F02及容量状态保持，未合并/发布。

生成Endpoint认证错误元数据修正计划（基线5fd28f21205073173dcc9a66de4c931cc2dd7b93，快照f02-generated-auth-openapi-20260927）：定位到CrudOpenApiContractGenerator已为每操作声明401/403 ProblemDetails，而CrudBackendFeatureGenerator两种Endpoint模板仅Produces成功响应，真实文档缺少对应元数据。先为旧能力/现代生命周期/组织归属三种生成路径逐操作建立401/403失败断言，再在全部生成受保护Endpoint上增加ProducesProblem401/403，不改变运行授权、HTTP响应、DTO或SQL；同步编译链接参考夹具只限受影响生成内容。真实OpenAPI门禁检查静态预期401/403皆在运行文档以application/problem+json暴露，仍不扩展为404/409完整错误契约或类型认证。Node补缺401/403/错媒体类型负例，串行聚焦.NET Unit与相关Node/治理/命名/分片发现/影响规划，Unit下限增加实际3项而不降门禁，独立复审后提交推送。实际应用编译与两库文档服务由新SHA Actions证明；不本地生成应用或容器，F02其余范围未关闭。

认证错误修正证据：三个生成路径有效RED3/3失败，实际失败都是缺401/403元数据；Node追加4负例RED为4失败/18通过。修正生成器后完整类第一次35项34通过/1失败，精确字节golden发现旧编译夹具未同步；只补其5操作共10声明，不放宽比较。最终`pnpm test:dotnet:unit -- --filter FullyQualifiedName~CrudArtifactGeneratorTests --minimum-expected-tests 35` 35/35、零跳过，Release0警告/0错误，包含3新增行与编译夹具漂移回归。Node五组128/128、治理55/55、命名33/33、syntax/diff通过；Integration分片发现1081无遗漏/重复，仅为发现证据。影响CodeGeneration/integration-matrix实际双库验证交Actions。Unit最低3433→3436按3实际用例，Integration不变。独立只读复审无阻断，复跑128/128，确认扩展来自现有Microsoft.AspNetCore.App、仅IProducesResponseTypeMetadata不改变handler/权限/DTO/SQL，golden同步已收口。生成应用编译和实际401/403文档仍待新SHA。

5fd28f21独立应用作业108583582583（CI36306320934）成功330/330、零失败/跳过。下载两库application-crud-openapi.json均completed=true、5实际比较，operations5/bearerProtected5/requestShapes3/responseShapes5/generatedUnchanged=true，实际反射文档路由/安全/字段子集已有证据；该SHA未检查本轮新增authenticationProblems10，不能代替新SHA。e0ed263e作业108582017814已成功312/312，两库五权限共十报告均完成，Delete25HTTP/12业务/正确删除1/不匹配冲突1/写拒绝2/双方404与空列表，其他Read/None/Create/Update要求在同SHA复验通过。主CI/Native本轮未全部核对终态，不报告当前整体通过。F02仍需当前认证错误文档新证据、完整错误/类型、Vue、应用Worker及完整人工再生成，Capacity-not-verified保持。

OpenAPI参数子集增量计划与证据（基线2b6237c5feff309516ac3a52d35cefcf4eebdb72，快照f02-created-app-openapi-parameters-20260927）：在既有真实文档门禁逐操作对照生成契约的参数name/in/required/基础scalar type/format，固定五操作范围。路径UUID必填不可空，分页page/pageSize可选int32，不允许额外TenantId、参数缺失或重复；可选查询参数仅兼容nullable表示和顺序差异。保持只读生成输入、部分失败证据和旧字段/安全/401403门禁。暂不比较默认值/范围、字段类型、全错误契约，不改生产.NET/生成器/SQL/迁移/矩阵。计划为先参数漂移负例RED，再实现、联合快速验证、只读复审，真实两库由新SHA Actions执行。

新10负例实际RED为10失败/22既有通过，全部Missing expected rejection，证明旧门禁漏检参数。实现后补可空查询顺序正例及可空路径/非法required负例，本组35/35，五组Node联合141/141、治理55/55、零跳过，syntax及任务diff检查通过。inner影响none，仅验收脚本与直接文档；无本地.NET/容器/浏览器。报告增加parameterShapes5与每操作实际参数数组；参数比较仅支持当前生成器的operation内联参数，不声称覆盖任意OpenAPI参数引用/继承。2b6237c5主CI36306870608与API Native36306870584/Worker Native36306870647仍运行，独立应用作业108585134867未终态，新增authenticationProblems10尚不能计通过。当前参数子集也等待新SHA真实报告，F02及Capacity-not-verified保持，未合并/发布。

独立只读复审发现并复现路径级parameters继承导致额外TenantId漏检；新增inherited-tenant-parameter负例有效RED失败，再按当前生成器操作内联范围保守拒绝非空或非法路径级parameters，不静默忽略。最终本组36/36、五组142/142，零跳过；前述35/141为修正前证据，不作为最终验收。该边界仍不实现任意OpenAPI继承/覆盖合并。

OpenAPI认证错误基础字段增量计划与证据（基线be77b684c1eb0f1ae4502c75415fb6d96c8efc57，快照f02-created-app-openapi-problem-fields-20260927）：沿已接入401/403门禁，对五操作各两状态检查ProblemDetails标准type/title/status/detail/instance的基础类型/format，status为integer/int32，其余string；容忍nullable及非必填，允许业务扩展。逐状态保存authenticationProblemFields实际数组，生成预期仍只读，文档内字段引用复用原解析，外部/缺失/循环拒绝。不扩展机器码或404/409契约，不改变产品.NET/生成器/SQL/迁移/矩阵。先7负例RED再实现/补正负例，联合快速验证/独立只读复审后推送，实际字段形态由新SHA双库Actions验证。

快速证据：7新增负例有效RED为7失败/36既有通过，全部Missing expected rejection，证明原门禁漏检缺title、status字符串/宽整数、detail对象、instance仅null及字段外部/缺失引用。实现后补nullable/扩展/本地ref正例、循环ref负例，本组45/45、五组Node151/151、治理55/55，零失败/跳过；inner影响none，无本地.NET/容器/浏览器，仅验收和直接文档。真实schema仍待新SHA，不将合成Node文档算作ASP.NET服务证明。

远端收口：2b6237c5独立应用作业108585134867（主CI36306870608）成功334/334、零失败/跳过。下载SQL Server/MySQL application-crud-openapi.json均completed=true，五操作/三请求/五响应/十认证错误声明，authenticationProblems10、generatedUnchanged=true。该SHA证明实际401/403元数据接入，未覆盖本轮字段类型或be77参数；be77b684独立应用作业108586383839（CI36307317678）仍运行。当前整体CI与两Native未全部终态核对，不称整体通过。F02其他全错误/类型、Vue、应用Worker和完整人工再生成仍未关闭，Capacity-not-verified保持；未合并/发布。

本轮独立只读复审无阻断，复跑45/45、syntax/diff通过，确认静态生成ProblemDetails的integer/int32及四string与门禁一致、nullable/可省略/业务扩展不会误拒，旧门禁保持；实际ASP.NET字段类型仍需新SHA。任务最终联合151/151通过。

OpenAPI数字兼容CI故障修正（基线0cc374722137be43383621cad8c6b652a083fca1，快照f02-openapi-numeric-query-20260927）：be77b684主CI36307317678/作业108586383839与0cc37472主CI36307533962/作业108586990208均失败，实际两库均在列表参数检查报parameter must have one scalar type、2!=1；不能计参数或ProblemDetails类型双库通过。下载0cc日志和两库失败报告，定位第一操作catalogListProducts，文档读取200/3.1.1。对照仓库真实canonical OpenAPI发现page/pageSize与ProblemDetails.status采用integer|string、int32及整数pattern（status另含null），合成样例此前未覆盖该ASP.NET数字读取兼容，非业务数据库故障。

按systematic-debugging/TDD先从canonical直接取查询及ProblemDetails schema建立2回归，有效RED2失败/旧45通过；最小兼容只在非空类型精确integer|string、format int32、pattern ^-?(?:0|[1-9]\\d*)$ 时归一integer，查询参数/ProblemDetails.status使用，路径不可使用。新增8负例分别覆盖两位置缺pattern/任意pattern/int64/extra object，最终本组55/55、五组161/161，零失败/跳过。报告先保存原始parameterDeclarations及authenticationProblemDeclarations后再比较，保留失败证据。只改验收和直接文档，不改生产.NET/生成器/SQL/迁移/矩阵，无本地.NET/容器/浏览器，真实修正必须由新SHA双库Actions证明。

远端其他结果：2b6237c5主CI36306870608及API Native36306870584/Worker Native36306870647已终态success；其双库独立应用334/334和authenticationProblems10证据保持。be77的API Native36307317700/Worker Native36307317680，0cc的API Native36307533918/Worker Native36307533926均success，但不能抵消对应主CI失败；分支筛选跳过项不计通过。F02及Capacity-not-verified保持，未合并/发布。

终检：独立只读复审无阻断，复跑55/55、syntax/diff通过，核对数字兼容仅查询和ProblemDetails.status、路径混合类型仍拒绝；治理55/55，inner规划none。真实双库修复等待新SHA，不因本地回归通过标记故障关闭。

模块全部产物再生成冲突增量计划与证据（基线e056a683a3559f52205b546924ee52485e802ab7，快照f02-module-conflict-all-artifacts-20260927）：现有独立应用只对生成SQL做人工修改冲突。扩展MODULE_ARTIFACTS六产物逐个修改，从相同应用原始内容开始，每轮实际apply-module-integration退出2，完整6行动作仅当前Conflict、其他Unchanged；核对全部生成、manifest、模块project/entry/manual和宿主基线保持，逐轮只撤销测试注释。SQL最后，默认不清理时保留旧SQL注释行为；真实栈显式清理，最终恢复全部原始字节。记录六轮CLI及conflictArtifacts，真实CLI六产物行为等待新SHA双库，不改生产生成器或扩大为任意人工业务/Vue完整再生成认证。

有效RED新增成功断言为1失败/13旧通过，证明原门禁只执行1而非6冲突；实现后14/14，补末轮误允许/修改manual负例，最终本组16/16、四组54/54零跳过。另对照已下载真实2b6 mysql module/conflict.json发现CLI输出是其他5 Unchanged与目标Conflict，最初合成runner仅一行；将runner改真实形态后RED5失败/11通过，再修正为精确6行计划比较，最终四组54/54。无本地.NET/容器/浏览器，inner影响none；仅验收与直接文档，不改.NET/SQL/迁移/矩阵。数字兼容e056 CI36310169977/独立应用108594450730仍运行，不计通过；F02与Capacity-not-verified保持，未合并/发布。

复审修正：聚合注册桥与五实体文件并非同一CLI冲突输出。桥漂移由ModuleIntegrationBackendWorkspace.EnsureUnchangedOwnedArtifact在规划前抛错，CLI stderr为确切“工作区冲突：模块聚合注册桥缺失或被修改。 路径：Generated/FullNetGeneratedModuleFeatures.g.cs”、stdout空、退出2；实体文件才返回5 Unchanged+目标Conflict。按真实桥空stdout建立RED5失败/11通过，分别校验桥诊断与实体完整计划后四组54/54。上述初版“每轮6行计划”仅适用于五实体，最终门禁不改变生产CLI。治理/语法/diff与复审后提交，六产物真实保护仍待新SHA。

终检独立复审无阻断，四组54/54零跳过；治理55/55、syntax/diff通过，六轮字节保护与SQL默认保留行为保持。实际六轮CLI双库等待新SHA，未关闭F02。

OpenAPI数字兼容双库实测收口（核对基线1b5dab0476317a56a68a460bca9aa7341d3450b7，快照f02-openapi-real-closeout-20260927）：e056a683独立应用作业108594450730（CI36310169977）已成功367/367、零失败/跳过。下载两库application-crud-openapi.json均completed=true，operations5/parameterShapes5/bearerProtected5/authenticationProblems10/requestShapes3/responseShapes5/generatedUnchanged=true。实际原始分页schema为integer|string/int32及受限整数pattern，ProblemDetails.status为null|integer|string/int32及同pattern，确认前两提交CI失败的诊断与修正均有真实证据；十个401/403标准字段比较已执行，不能再描述为等待数字兼容修正。

此证据只关闭参数/认证错误基础字段与数字兼容故障子集，不覆盖业务404/409机器码完整schema、请求响应全类型/Vue、应用Worker及完整人工业务再生成。e056整体主CI及API/Worker Native仍运行，不称整体通过；1b5dab04六产物再生成作业108595520455（CI36310555699）尚未终态，不将367项旧验收作为六产物保护通过证据。F02与Capacity-not-verified保持未关闭；未合并/发布。

六产物再生成保护双库实测收口（基线898d1c0959ac711cfcb4e24dadbfb6dd959543de，快照f02-module-real-closeout-20260927）：1b5dab04独立应用作业108595520455（CI36310555699）成功370/370、零失败/跳过。下载SQL Server/MySQL application-crud-module/result.json均artifacts6/moduleCompiled=true/conflictRejected=true，conflictArtifacts含全部六个文件；两库共12轮CLI报告均退出2。桥轮stdout为空且stderr确切原因/路径匹配，五实体轮各一Conflict和五Unchanged；实际验收完整执行了逐轮生成/manifest/人工/宿主字节保持及仅测试注释撤销，随后迁移、API与OpenAPI/CRUD/五权限/隔离继续通过。此SHA已有同一应用两库模块保护和现有OpenAPI子集联合证据，仍不等于真实人工业务扩展或Vue的完整再生成。

当前整体CI、API Native36310555740、Worker Native36310555769仍运行，不能报告该SHA整体通过。F02剩余包括独立应用Vue实际接入/编译/使用、应用Worker宿主、完整业务错误/字段类型及教程全链路/人工业务扩展；已核对当前host-wiring只构建API，Vue文件尚停留生成目录，不将仓库客户端E2E冒充新应用Vue验收。F02与Capacity-not-verified保持，未合并/发布。

Vue接入前置客户端工具增量（基线4c171501b3d5ab9ff6d13a57bc8a149cb046160a，快照f02-client-generator-manifest-20260927）：核对发现当前真实应用仅生成Vue文件，未接入/编译；低层OpenAPI客户端生成器固定读取原仓库公开操作清单，独立应用无法指定自己的匿名操作边界。本轮仅为generateFullNetClient增加manifestPath与CLI --manifest，默认沿旧清单，指定时不隐式回退。清单省略publicOperationIds等价[]，显式非法null/非数组/非字符串/空或空白/重复名称失败，匿名操作必须逐项声明，所有验证在写盘前完成。后续仍需分发应用自有工具、生成业务客户端、Vue适配/路由/编译及真实页面验收；本轮不声称Vue接入或工具分发已完成。

首5有效RED全失败（自有清单被忽略或非法清单未拒绝），修正后补非法空白/null、未列匿名及CLI无值负例至10新项。客户端generator/evaluation/readiness联合28/28零失败/跳过；默认node scripts/openapi/generate-fullnet-client.mjs --check零漂移，syntax/diff通过，inner影响none，无本地.NET/容器/浏览器。仅生成工具、回归和直接文档，不改变服务端授权/公共schema/生成产物或矩阵。4c171501主CI36311051617/API Native36311051620/Worker Native36311051632均终态success；898d1c09三工作流也终态success。当前SHA后续Actions仍需单独核对，不把历史成功算作本轮。F02及Capacity-not-verified保持，未合并/发布。

终检独立只读复审新10/10零跳过、默认check零漂移、syntax/diff通过，无阻断；确认清单验证在写盘前、默认兼容、未声明匿名操作仍拒绝。联合28/28、治理55/55为本轮快速证据；应用工具分发及Vue实际接入未完成。

独立应用客户端工具分发计划与快速证据（基线81882184885c46d9f270d6b2cb5efed328954de2，快照f02-packaged-client-tools-20260927）：将生成脚本、唯一依赖readiness校验器、默认canonical OpenAPI和公开操作manifest四文件纳入固定源码包摘要；buildAppTemplate仅从冻结bundle复制至应用.fullnet-tools/openapi与contracts/openapi，不从当前仓库工作区补工具。脚本相对根落在应用目录，默认输入/清单/输出均应用拥有；初始化后显式工具升级，不由框架升级器自动覆盖应用副本。加入独立目录真实Node生成/check、缺源及占用目标/父目录零覆盖；在完整packaged-app及每库real-stack中执行应用自带工具默认check、记录application-client-tools.json及包摘要，实际模板创建的完整闭包由新SHA Actions证明。

前三测试先RED3/3失败（分发未实现），实现后3/3。占用contracts父目录新增RED1失败/3通过，修正预检全部目标父链后本组4/4；相关application-client-tools/client-generator-manifest/created-app联合21/21、治理55/55零跳过。隔离fixture使用真实脚本且cwd系统临时目录，不访问原仓库运行依赖；这是工具闭包实测，不是完整模板/SDK或Vue构建。syntax/diff及独立复审后提交。任务快照期间其他AI任务持续修改src/tests/matrix，inner全快照报告Ai/integration-matrix属于外部增量，未执行或作为本工具验证；本轮不改变.NET/SQL/迁移/矩阵，不本地.NET/容器/浏览器。818当前CI/Native仍运行，不报告通过。

本轮仅交付冻结包到应用的客户端工具闭包；业务OpenAPI合并或独立DTO/Operation接线、Vue适配/路由/页面编译与实际使用仍后续，F02未关闭，Capacity-not-verified保持，未合并/发布。

复审修正模板分发接线：原.template.config排除全部.fullnet-tools会丢失新工具，旧packaged-app断言也要求整个目录不存在。已仅排除直属mjs引导/升级工具，放行openapi子目录，且openapi工具/契约设copyOnly；实际模板验收逐字节比较应用四副本与冻结框架源，并确认只出现openapi、不带create/upgrade工具。最终三组21/21；完整dotnet new分发与每库报告待新SHA，不将隔离copy helper测试算作完整模板引擎通过。

终检独立复审配置阻断关闭，相关Node8/8零跳过、diff通过，无剩余阻断；本任务联合21/21、治理55/55、syntax/diff通过。实际模板引擎及每库工具检查仍待新SHA，未升级Vue或F02状态。

客户端工具分发远端收口（证据提交43bf0718cc97ecb16c22cbe82bb919ec2c15bf0a；记录基线0f9ffda9bb808ca86b83560e2a5abfbd3c0081d0，快照f02-client-tools-ci-evidence-20260927）：主CI36315494268、API Native36315494256、Worker Native36315494218均终态success。独立应用双库作业108609299088成功374/374、零失败/跳过，实际完整模板创建的四份工具/契约副本字节与冻结源码一致；下载两库application-client-tools.json均status0、stdout为客户端OpenAPI生成产物零漂移、stderr为空。默认应用工具分发与运行闭包已有真实证据，分支筛选跳过项仍不算通过，不替代main完整Integration或生成应用Native认证。

本轮仅同步上述证据及教程中过时的未分发说明；并行AI任务提交0f9ffda9保持，不纳入本切片验证结论。Vue业务契约接线/编译/页面使用、应用Worker和其他既有F02缺口仍待完成，F02及Capacity-not-verified保持，未合并/发布。

应用业务客户端独立引用增量（基线f33219b2b5c62a965c15d0cd48b49cc336d73667，快照f02-client-http-module-20260927）：生成器固定../http.js使业务独立目录无法直接复用共享HTTP契约。增加httpModuleSpecifier及CLI --http-module，默认字节保持；自定义引用按JSON字符串输出，非字符串/空/前后空白/控制字符在创建输出目录前拒绝，CLI缺值拒绝。应用可显式使用@fullnet/client-contracts，把业务产物保留在应用目录，不覆盖共享官方操作。此切片只改变类型引用，不新增运行时适配、路由、权限或页面行为。

新增8项有效RED全部失败、10旧项通过；实现后18/18，包含实际TypeScript编译四份业务生成文件并通过paths映射消费真实共享src/index.ts，及相同参数CLI --check。编译验证不等于安装应用包或Vue生产构建；仍需真实业务OpenAPI、应用适配器和页面接线验收。inner影响none，不执行本地.NET/容器/浏览器，F02与Capacity-not-verified保持。

终检相关四组Node40/40、治理55/55零跳过，默认客户端check零漂移、语法及任务diff通过。独立只读复审18/18无阻断，确认默认引用字节保持、引用字符串隔离及写盘前校验；应用包解析与Vue构建不在本轮证明范围。新提交Actions仍需独立核对，未合并/发布。

业务客户端生成验收增量（基线bcdb22735fb624fc2d9f4c70207427f8622e1db1，快照f02-business-client-generation-20260927）：bcdb主CI36322214981/API Native36322215016/Worker Native36322214980均终态success。下一步在独立应用真实双库的服务端OpenAPI比较之后，使用应用工具消费商品生成契约，专用verification/ClientGeneration输出四份TS，声明无公开操作及共享HTTP类型引用，核对五固定业务操作；实际tsc strict/noEmit通过paths映射应用拥有的http.ts，再执行相同参数check，核对业务契约/共享HTTP/四份官方基线不变。每库保存generate/compile/check/result，未通过不能写完成结果；占用验证目录拒绝，不覆盖人工内容。

初始三项有效RED3/3，实施后3/3，补编译失败、check失败及共享基线被改拒绝。本地fixture消费真实商品黄金契约、真实复制Node工具和真实共享源码并执行TypeScript编译；这不是新应用真实包解析或Vue构建，不证明业务操作运行时请求。新SHA双库实际调用仍待Actions，不本地.NET/容器/浏览器，F02及Capacity-not-verified保持，未合并/发布。

复审纠正硬删除模式：旧黄金契约为catalogDisableProduct，独立应用schema明确hard.delete，实际应为catalogDeleteProduct。测试显式转换操作名与/disable至/delete路由，先RED operation set changed后修正固定集合；不把转换fixture称为真实现代生成证明。首次并行本地验证遇到进程资源错误，未计通过；停止本轮精确测试进程后串行相关三组28/28、零失败/跳过，含六项业务客户端验证，实际编译及漂移负例通过。共享字节比较使用Buffer.equals，避免失败诊断扩展整个大型基线。新SHA仍须双库验收实际契约。

业务客户端远端验收收口（证据提交badfad6477c36e66eba256771979312af473bdbc，快照f02-business-client-ci-evidence-20260927）：主CI36325119133、API Native36325119115、Worker Native36325119184均终态success。独立应用双库作业108636301949成功380/380、零失败/跳过。下载SQL Server/MySQL application-crud-client目录，两库result均operations5/generatedFiles4、compiled/zeroDrift/inputsUnchanged为true，generate/compile/check均status0；compile stdout/stderr为空，check报告产物零漂移。该SHA证明真实应用生成的hard.delete契约由应用自带工具生成并兼容自身共享HTTP类型，不再只依赖本地转换fixture。

本轮仅同步该精确SHA的证据及教程，不新增生产行为。业务客户端运行时请求、应用包解析、Vue适配/路由/页面构建与使用、应用Worker及其他F02缺口仍待完成；分支跳过项不计通过，不能替代main完整Integration或生成应用Native认证。F02与Capacity-not-verified保持，未合并/发布。

业务客户端匿名运行增量（基线8549e1746d83f7c5b8741cb16f4e104f2d5c122f，快照f02-business-client-runtime-20260927）：编译由noEmit改为专用emitted目录并保留应用路径，局部ESM声明；加载真实应用共享http.js和五个业务生成操作，使用createHttpClient，不注入凭据或刷新，以15秒信号及不重试参数调用实际API。每操作须抛出401及identity.session_not_active，报告仅操作名/状态/机器码，finally保存未完成失败证据；接在双库生成/编译后、登录前。仅扩展验收，不改生产HTTP实现、授权、业务/数据库行为。

两项新运行测试有效RED2/2，实施后8/8；补403/200误允许负例后相关三组串行32/32、零失败/跳过。真实本地HTTP测试服务器确认五请求方法、删除路由及无Authorization，错机器码/错状态均拒绝。该fixture不是ASP.NET双库运行；新SHA实际API匿名拒绝链路仍待Actions。允许租户CRUD的生成客户端请求、应用包解析、Vue页面等仍未认证，F02及Capacity-not-verified保持，未合并/发布。

复审修正transport假绿：共享ProblemDetails读取接受正文status，HTTP403/500搭配正文401可被旧验收误通过。新增HTTP500/body401混配回归先RED（Missing expected rejection）；运行迁入隔离Worker，只在Worker观测fetch响应，不污染宿主或其他请求。逐操作严格一响应且httpStatus与解码status都为401，finally恢复Worker fetch并保留失败报告；生产解码器未修改。

终检相关三组串行33/33、治理55/55零跳过，syntax/diff通过、inner影响none。独立复审本组11/11，transport阻断关闭、Worker生命周期无新增阻断；实际API双库结果仍待新SHA。8549文档提交主CI36328023996/API Native36328024033/Worker Native36328023985均终态success，不作为本轮代码证明。

生成客户端匿名运行远端收口与Host拒绝增量（基线006ca78f1fff66a6bfe37627264ee527e3d9c673，快照f02-business-client-host-denial-20260928）：006ca主CI36331246301/API Native36331246405/Worker Native36331246267均终态success；双库独立应用108653465437成功385/385、零失败/跳过。下载两库runtime.json均completed=true，五生成操作httpStatus/status401及identity.session_not_active，真实共享HTTP拒绝链路已验证。分支跳过项不计通过。

本轮将显式Host凭据从内存传入隔离Worker，共享HTTP注入Bearer；固定期待HTTP/body403与authorization.permission_denied，每操作仅一次请求，不刷新/重试。接在现有Host登录后、租户切换前，保存host-runtime.json；失败报告和Worker错误消息均脱敏，不保存凭据/请求配置。非法空/null/非字符串凭据拒绝。只扩展验收，不改生产授权、共享HTTP、数据库或页面。

Host新增三场景先RED2失败/1通过，实现后含原场景14/14；测试服务器验证正确Bearer与五方法/删除路由、500/body403混配拒绝、错误code回显token时错误与报告脱敏，另补三非法凭据负例。新SHA真实Host授权双库结果仍待Actions，不把fixture当真实API；允许CRUD、包解析、Vue接线等F02缺口与Capacity-not-verified保持，未合并/发布。

终检相关三组串行39/39、治理55/55零跳过，语法及任务diff通过、inner影响none。独立复审14/14及新增非法凭据3/3无阻断，确认Host调用在租户切换前、凭据只在Worker内存及报告/错误脱敏；外部新增日志ADR/操作文档不纳入本任务。真实双库Host拒绝待新SHA，未本地.NET/容器/浏览器。

生成客户端Host拒绝收口与成功列表增量（基线44ad78833a80cdd4823d2863a999e18d7c94fff7，快照f02-business-client-tenant-read-20260928）：44ad主CI36335535861/API Native36335535823/Worker Native36335535907均终态success；双库独立应用108665550676成功391/391、零失败/跳过。两库host-runtime.json completed=true、subject host-admin，五操作真实HTTP/body403与authorization.permission_denied，无凭据。此精确SHA关闭Host拒绝待验收项，不证明允许业务CRUD。

下一增量复用已有tenantCRUD移交的可信token，在隔离切换前调用generated catalogListProducts，经应用共享HTTP及生成响应解析器验证单次HTTP200、page1/pageSize5及items数组。固定成功读取入口，不让调用者配置期望状态；凭据必需且仅内存，报告只操作/httpStatus/条数，失败保留。Worker启动逻辑与拒绝入口共用，不新增生产模块、数据库写入/权限或页面。

初始两测试RED2/2，实施后2/2，再补HTTP201正常结构拒绝。fixture为真实本地HTTP服务器，确认Bearer/GET与分页参数、坏响应结构拒绝及报告无凭据；不等于ASP.NET双库证明。真实成功列表仍待新SHA；完整生成客户端CRUD、非空数据隔离、包解析及Vue接线等F02缺口保持，Capacity-not-verified保持，未合并/发布。

终检相关三组串行42/42、治理55/55零跳过，补列表准确路由及pageSize断言后新三项3/3；syntax/任务diff通过、inner影响none。独立复审本组20/20无阻断，确认实际生成解析器、单fetch、可信会话接入及Worker隔离/脱敏；真实分页total等形态仍待新SHA，空列表通过不认证非空数据/隔离或普通账号权限。外部日志文档增量保留，不纳入本提交。

非空商品生成客户端读取增量（基线79da3f96b21cb50b1370d1a4e551df12c066c88a，分支codex/foundation-acceptance-20261003，快照f02-generated-client-nonempty-read-20261003）：32fc独立应用双库作业108675258575成功394/394，SQL Server/MySQL tenant-read.json均completed=true、HTTP200/items0；但主CI36338997482的build-and-module-test作业108675258857在无关Workflow Todo SQL Server测试中遭1205 deadlock，整条CI失败，不能把模板成功称整体成功。该提交已由其他任务合并main，本轮从main另开开发分支，保留并行AI及日志模块工作区改动。

针对空列表只验证结构的缺口，在已有tenantCRUD创建商品并校验Id/TenantId/Name/Version后增加可选回调，将可信租户token和最小商品身份只在内存中传给生成客户端Worker。Worker实际调用catalogGetProduct，要求单次HTTP200、生成响应解析成功、四字段完全一致；失败即停止原CRUD的后续更新/删除，product-read.json只记录操作、HTTP状态及completed。未改生产服务端、SQL、授权或Vue。四项新增可失败验证初始RED，实施后4/4；相关Node59/59、治理57/57均零跳过。新规则R-20260930允许本地验收，本轮Node夹具证明辅助验收逻辑；真实独立应用双库运行仍未验证，故不将此项标为完整应用通过。F02和Capacity-not-verified保持，未合并/发布。

独立只读复审相关两组Node37/37、任务diff通过，无阻断。复审指出本地测试成功响应同时包含旧CatalogProduct黄金契约的displayName/description/isActive与当前应用name；它证明旧夹具的生成读取器路径和明确name比对，不证明新应用真实ProductResponse schema。新SHA本地双库真实栈尚未执行，不能把该夹具通过称作真实DTO兼容认证。并行AI/日志工作区改动继续保留，不纳入本提交。

## 8. 当前剩余工作队列（2026-10-29）

### 8.1 核对基线与已交付复用清单

本次规划基线为 `main / 75be6fc0e17433f9c59c42553093dacd479ef9d1` 加当前工作区。总计划已有 F08b、F09、F12 未提交进度增量，全部保留。核对采用当前源码入口、最近专项计划与验收记录；本轮没有重新执行运行时测试。以下“复用”指不重复实现已交付能力，历史测试只证明其原输入与范围。开工前如出现更新证据，先从剩余项中扣除，不重新做一次全仓盘点。

2026-11-02 复审时分支/HEAD 未变，但工作区及专项记录已经演进，HEAD 相同不代表验收输入相同。复查了生成器当前工作区复验、10-26 模块审查的修复收口、Workflow 续办至 10-31/11-01 的记录及非 Workflow 交付至 11-02 的记录。已修复的模板审计管线、示例提交权限/恢复、支付幂等、生成恢复等不再作为新增整改任务，依据见[模块审查后续修复记录](../../verification/2026-10-26-project-module-buildingblocks-review.md)。这些记录按各自实际范围复用，本轮没有重新认证整仓行为。

| 已存在或已交付的范围 | 证据/实现入口 | 本次排期处理 |
| --- | --- | --- |
| 版本化源码分发、四预设构建、Minimal 双库字典 CRUD、升级摘要/冲突/恢复保护 | [F01 首轮交付](../../verification/2026-09-26-f01-created-app-real-stack-closeout.md)，本文 §1 | 复用；不重建模板、升级器或四预设 |
| CLI 诊断、模块/权限接入、生成所有权及恢复；独立应用客户端生成/编译/漂移、匿名及 Host 拒绝、空列表读取已有分范围证据 | 本文 §5 F02、§7 后续精确提交记录；[教程](../../development/create-first-crud.md) | 只补真实生成业务客户端写入、非空读取及独立 Vue 接线等尚未关闭边界 |
| CodeGeneration 四批整改和当前工作区控制面 HTTP 复验；树/主从/关联真实生成 SFC 夹具已存在 | [生成器整改](2026-10-24-codegeneration-remediation.md)、[页面夹具边界](../../../tests/e2e/generated-crud/README.md) | 复用生成器与夹具；受控 HTTP 页面证据不替代独立应用真实后端链路 |
| Identity 注册/恢复/邀请/成员/生命周期 API 与 Vue；OIDC、MFA、恢复码基础 | [F00 资产核对](../../verification/2026-09-17-foundation-productization-f00-baseline.md)、[OIDC 专项](2026-09-13-identity-oidc-sso-evolution.md)、当前 Identity/Tenancy Features | 不安排从零实现；只处理完整用户旅程中的缺口与缺失证据 |
| 套餐权益/配额；F08b 持久席位操作、后台恢复和定向双库证据 | 本文 2026-10-27 F08b 记录、[席位规格](../specs/2026-09-22-f08b-tenant-invitation-seat-orchestration.md) | 不再安排席位状态机、预留/确认/释放内核重建 |
| 企业样板 CRUD、审批提交/回写；附件日记、报表/打印消费方及定向验收 | [10-27 接替开发](../../verification/2026-10-27-non-workflow-delivery.md)、`samples/enterprise-request/` | 只补原 CRUD/导入与权限矩阵、真实审批页面和交付串联 |
| 版本报价、冻结账单、幂等下单、资金事实 Outbox、恢复履约、退款来源撤销/到期整理及 Vue | 同一 10-27 记录、本文 F12 | 不再安排支付驱动权益、履约 Port、Worker 从零开发；真实商户与自动续费未据此关闭 |
| 通知 N01–N08、治理工作台、回执恢复、语言快照、预算和部分原生/告警证据 | [通知唯一计划](2026-08-30-notifications-platform-extension.md) | 不重做通知中心或适配器；剩余真实渠道验证按选用渠道进行 |
| Workflow/VForm、330/333 及其后协作执行、Files、真实页面和物理 Kafka/Native 等已有持续增量与分范围证据 | [节点计划](2026-10-24-workflow-node-completion.md)、[续办记录](2026-10-28-workflow-cross-machine-handoff.md)的最新章节 | 不将 10-28 的目标执行/Files 待办原样重排；只消费专项当前未关闭差集，不按旧节点数量重建 |
| ImportExport/Reporting/Printing/Files、OpenAccess/Webhooks 与验签示例 | 对应 `src/Modules/`、`ui/admin/src/views/`、[Webhook 样例](../../../samples/webhook-consumer/README.md) | 复用执行内核、页面和验签脚本，只补接入与治理缺口 |
| 菜单搜索、Grid Preference、各模块任务列表 | [菜单搜索](../../../ui/admin/src/framework/art-design/layout/ArtGlobalSearch.vue)、`packages/client-contracts/src/grid-preferences.ts`、导入/报表/预览页面 | 作为 F17–F19 的起点，不把新增体验写成现有能力 |
| AI、Document、Messaging 最新专项增量；AI K06 及 A04 已有后续分批验收，A04 后续竞态仍按精确范围记录 | [近期交付](../../verification/2026-10-27-non-workflow-delivery.md)最新章节、[能力矩阵](../../roadmap/capability-status.md)及其专项链接 | 保持专项所有权；不重做已交付知识发布/业务助手、文档三批整改、消息恢复，不将局部协议验收升级为整个 AI 完成 |

### 8.2 优先级、依赖与批次

P0 直接决定开发者能否创建并交付首个业务；P1 完善企业使用与交付；P2 改善通用使用效率。SaaS、开放集成和移动端按选用预设进入，不阻塞未选用它们的基础预设。下面编号中的 `-R` 表示原任务的剩余增量，不创建平行计划。

| 顺序/优先级 | 当前工作包 | 前置与归属 | 完成出口 |
| --- | --- | --- | --- |
| 1 / P0 | F02-R 独立应用生成链路 | 复用 F01 和生成器已验能力；本文负责 | 新应用实际生成业务经 Vue 完成 CRUD、权限拒绝和再生成保护 |
| 已关闭 / P0 | F09–F11-R 完整业务样板 | F09/F10/F11 本轮差集已关闭，复用 §8.20–8.39 的功能及真实验收证据 | 不重复安排已验页面、授权、退回、幂等和进程恢复 |
| 3 / P1 | F03–F06-R 账号与企业自助 | 复用 Identity、C01/C04；本文负责用户旅程 | 受邀注册、恢复、交接、退出/停用可由实际用户完成 |
| 3 / P1 | C03-R Workflow 剩余专项 | 原节点计划及续办记录拥有实施细项 | 只消费专项已关闭的契约和节点，不重复执行已验基础 |
| 4 / P1 | C02-R 数据交付体验 | 样板必需能力随 F11 同批；此处只安排跨页面体验完善，避免 F11/C02 互相等待 | 可配置模板、错误反馈、输出及恢复形成一致交互 |
| 5 / P1（选装） | F13–F14-R 外部接入包 | 一条稳定业务事件、现有 OpenAccess/Webhooks | 独立消费应用完成授权调用、验签、幂等消费和撤销 |
| 5 / P1（SaaS 选装） | F12-R 真实渠道与订阅运营 | 既有履约/配额；企业生命周期适用部分 | 选定支付渠道与订阅旅程通过对应验收 |
| 6 / P2 | F18 查询方案与工作台 | 已有 Grid Preference、稳定页面/筛选契约 | 一个真实业务列表可保存、恢复、共享受控查询方案 |
| 7 / P2 | F19 统一后台任务中心 | 至少两种已稳定任务的最小只读契约；不等待 C02 全部体验完善 | 聚合本人任务并把受控操作交回原模块 |
| 8 / P2 | F17 跨业务搜索 | 稳定权限/对象契约及至少两个搜索消费者 | 菜单、文档、业务单据可统一发现且无越权泄漏 |
| 贯穿，发布前 / P1 | F15–F16-R 预设交付与恢复 | 只依赖该预设选用能力 | 可复现安装、升级及数据库/文件/密钥恢复证据 |

默认先完成第 1、2 批；企业自助与 Workflow 专项可在依赖独立时分别推进，但本计划不自动派发多代理。外部凭据缺失只阻塞对应渠道验收，其他内部实现连续推进。本轮用户已要求按本计划连续开发；后续实施授权仍以当次会话为准，计划本身不扩大授权。

F09–F11 使用受控已有成员与站内通知即可验证业务接入，不等待公开注册、真实邮件或全部 Workflow 新节点；实际触达的权限/隔离/恢复缺陷必须在该批修复。F13–F14 可以独立于真实支付渠道推进。P2 不是前一 P2 的完成依赖，按实际消费者就绪程度选择；所有预设只承担自己选用能力的门禁。

### 8.3 P0：F02-R 独立应用生成链路

**复用与范围：** 模板、诊断、生成器、权限接入及客户端生成均已有实现；不重复编写。主要入口为 `templates/fullnet-app/`、`tests/templates/support/created-app-real-stack.mjs`、`scripts/templates/`、`tests/e2e/generated-crud/` 和 [首个 CRUD 教程](../../development/create-first-crud.md)。

- [x] F02-R1：固定分发包已贯通两库的实际 DTO、非空详情/列表及生成客户端创建/更新/删除，详见 §8.48；已验匿名/Host 拒绝按实际影响复用。
- [ ] F02-R2：将应用自有业务客户端、HTTP 适配、菜单/路由、权限与 Vue SFC 实际接线；从模板分发依赖构建并启动应用页面，避免依赖原仓库绝对路径或手工修补生成文件。
- [ ] F02-R3：真实页面验证普通授权用户 CRUD、缺动作权限、其他租户对象拒绝；修改应用业务代码后再次生成，验证人工内容保留、受管漂移拒绝及直接涉及的中断恢复。复用当前生成器恢复机制，只修真实复现缺口。
- [x] F02-R4：实际分发与 `dotnet new` 入口、成功命令、模块参数、失败处理及两库实测耗时已写回原教程，详见 §8.48。

**验收：** 同一独立应用的生成产物贯通双库相应业务场景与真实 Vue；不得用原仓库字典 CRUD、受控 API 页面或 TypeScript 编译替代。先完成整条链路，再集中运行新行为及必要边界，基础生成机制不按历史数量重跑。

### 8.4 P0：F09–F11-R 可复制业务样板

**复用与范围：** 保留 10-27 已交付附件、工作簿、打印和恢复日记；修改集中在 `samples/enterprise-request/`、`ui/admin/src/views/EnterpriseRequestsView.vue`、对应客户端和真实栈用例，不向框架核心加入项目业务实体。

- [x] F09-R1：用一组明确的申请人/部门/跨租户/字段权限数据补齐原 CRUD 与旧导入的剩余矩阵，核对金额、明细、版本冲突及附件权限在各入口一致；已有附件/报表定向用例直接复用。
  草稿/并发边界见 §8.17；普通活动成员、部门读取/写入、真实第二租户、版本冲突、明细权限、打印金额脱敏和导入执行复验已完成双库专项（§8.19）。不在生成文件内手工打补丁，既有附件/报表恢复证据按原范围复用。
- [x] F10-R1：真实页面草稿→提交→批准/驳回/退回→结果回写→通知已通过；双库真实成员撤权、重复操作及独立 Worker 在启动/回写后确认前被终止、自然租约恢复已补齐。见 §8.32、§8.38–8.39，不重复安排。
- [x] F11-R1：同一业务单据的模板导入、错误行反馈、报表导出及打印已完成真实页面；普通成员及排队后撤权、当前结果下载/字段权限的双库差集已通过（§8.19、§8.25、§8.29、§8.36）。复用已有渲染 Port，不重排已验功能；大文件/容量与通用来源到期策略另归其原工作包。
- [x] F09-R2：更新样板 README，写明开发者拥有的代码、需实现的模块契约、精确权限和最短接入步骤；以相同样板覆盖以上流程，不再创建另一套示例业务。

**验收：** 选定双库业务场景及真实浏览器流程通过；不要求先完成全部 Workflow 新节点。真实跨进程中断若未测，明确保留边界，不将直接调用恢复批处理视为进程故障证明。

### 8.5 P1：F03–F06-R 账号与企业自助

**入口：** Identity 的 `AccountChallenges`、`RegisterAccount`、`RecoverAccount`、`ManageTenantMembers`、`TenantSeatOperations`，Tenancy 的 `ProvisionTenant`、`ManageTenantLifecycle`，以及现有注册/恢复/入驻/成员 Vue 页面。

- [ ] F03-R1：复用 §8.41/§8.49 已完成的三用途、中英文、未知结果、重发替换与过期适配链；余项为受控 SMTP 的真实 TLS 连接及厂商认证证据，不重写通知 N01–N08 或重排已验验证码逻辑。
- [ ] F04/F05-R1：无账号受邀注册到成员激活已完成（§8.45）；注册方式的受邀成员业务角色及普通部门/职位分配已通过功能验证（§8.52）。开放方式直注册后的本人入驻与额度失败接续已完成（§8.54）。剩余为已有账号原登录/MFA，以及撤销竞争、进程中断和原会话失效的未覆盖场景，继续复用持久席位编排。
- [ ] F06-R1：企业生命周期读取/暂停/恢复/关闭操作台及跨入口并发最后一名保护已完成（§8.56）；所有者交接、成员退出/移除和完整消费者撤权旅程仍开放。逐消费者核对会话、API 凭据、文件、待办、后台副作用和实时连接撤权，只补缺失处理。
- [ ] F06-R2：在现有企业使用文档中说明失败重试和运维接续，区分全局账号与企业成员，不以退出一家企业禁用全局账号。

**验收：** 无账号入驻、重复接受、最后一名保护、退出 A 不影响 B、停用后新副作用拒绝等实际改变边界通过；所选邮件渠道证据可追踪。业务不变量和双库变化按聚焦场景验证，不重跑全部 Identity/OIDC。

### 8.6 P1：C03-R 与 C02-R 专项接入

**Workflow 所有权：** 节点/协作执行的开发、迁移和验收仅在 [节点计划](2026-10-24-workflow-node-completion.md)及其[续办记录](2026-10-28-workflow-cross-machine-handoff.md)维护。本计划只跟踪 F10 所需契约是否就绪。330/333 及后续目标执行、实时守卫、结果/取消、Files、物理 Kafka/Native 已有持续增量，不能将这些名称整体列为尚未实现；WF-R4/R5 等只取专项当前未关闭差集。VForm 已交付完整源码和定向运行链路，不另建表单引擎。

**数据交付工作包：** 复用 `ImportExport`、`Reporting`、`Printing`、`Files` 以及已有任务页面。

本节只拥有原模块的通用能力与体验变化；F11 只拥有样板的 Schema、查询/渲染契约和页面接入。同一缺陷只登记一个任务，样板发现阻断时直接在同批完成原模块必要修复，不另等本节整体验收。进度文案和操作资格在此统一，F19 只聚合，不再次建设。

- [x] C02-R1：职位与企业申请已交付可保存/恢复的本人列映射、逐行校验、冻结输入及原会话复核；双库、Worker、真实页面聚焦验收通过，见 §8.25；保持静态 Schema，不开放任意 SQL 或脚本。
- [ ] C02-R2：统一既有导入/导出/预览任务的状态文案、进度、取消、重试资格和到期结果提示；Reporting 租户闭包及有界刷新已完成（§8.21），本人待执行导入取消已通过聚焦验收（§8.26）。报表待执行取消（§8.27）、复用既有 Document 取消/重试的页面控制（§8.28）及样板有效期/清理状态/取消确认（§8.29）均已通过各自聚焦验收，不再重排；不重新建设已存在的预览后端或样板到期机制。通用来源到期能力等差集继续开放，聚合入口交给 F19。
- [ ] C02-R3：补真实业务所需的报表/打印模板配置入口、预览及版本使用说明；Host 打印草稿/显式发布/历史版式与字段插入已完成聚焦验收（§8.30–8.31），通用租户打印授权闭包及业务模板绑定继续开放；复用现有模板/渲染器，不复制第二套执行服务。

**验收：** 一个真实业务消费者完成配置→执行→失败修正→结果获取；只补当前缺失的文件所有权、排队后撤权、取消/恢复和输出预算用例。大文件规模单独量测，不从功能通过推导容量。

### 8.7 P1 选装：F13–F14-R 外部接入

**复用：** OpenAccess/API Key/OIDC、现有 Webhooks 订阅及投递 Worker、`samples/webhook-consumer/verify-signature.mjs` 和 OpenAPI 生成工具。

- [ ] F13-R1：核对一条真实业务完成事件的发布与订阅接线；补缺失的 Vue 订阅管理、投递/尝试详情、暂停与受控重放入口，操作回到 Webhooks 所有者。
- [ ] F13-R2：在既有签名协议上核对重放时间窗、稳定事件身份、密钥轮换与重复消费边界；如契约需要演进，同步发送方、验签样例与文档。按变化补 SSRF、撤销订阅、超时未知、重试与幂等验收。
- [ ] F14-R1：把现有验签脚本扩展为可启动的接收样例，保存幂等消费结果；使用环境秘密引用，提供凭据撤销、限流及 ProblemDetails 排错指南。
- [ ] F14-R2：从现有生成工具产出可本地打包安装的 TypeScript 接入包，在干净消费项目验证授权调用、一条事件及跨租户拒绝；只对幂等操作提供自动重试。

**协议关闭条件：** 当前验签样例只覆盖载荷 HMAC，不能据此关闭带时间窗的可靠接入。实施时冻结签名规范，把时间戳、EventId/DeliveryId、KeyId 与原始请求体共同绑定，重试刷新投递时间但保留稳定业务事件身份；接收方先验签/时间窗，再将事件身份去重与业务落地原子完成。同一事件不同载荷拒绝。密钥仅引用受保护配置；轮换按真实接收方策略决定，不为框架历史版本预造兼容协议。签名演进必须同批更新发送器、接收样例、SDK 和故障测试。

**验收：** 外部项目不引用内部实现即可接入，重复投递只生效一次，撤销后不能继续调用；公共 SDK 发布与生产客户地址投递另按明确授权执行。

### 8.8 P1 SaaS 选装：F12-R 订阅运营

**复用：** 10-27 的报价/账单/订单/资金事件/权益履约/退款撤销/到期整理和 Vue 全部保留，不再安排同名内核。

- [ ] F12-R1：选一个现有支付适配器完成商户测试环境认证，验证创建/查询/回调/退款/对账；回调丢失、重复及响应不明复用现有恢复链路，明确内部履约成功与渠道扣款成功的证据区别。
- [ ] F12-R2：核对试用→付费→手动续期→取消/到期→恢复的完整运营页面与权限；套餐调整的生效时间、剩余额度和退款规则按现有政策落实，发现未定义业务政策时先完成具体方案。
- [ ] F12-R3：自动续费作为独立可选增量，先确定渠道签约/扣款授权、撤销及失败催缴政策；无适用需求或授权时保持 Deferred，不阻塞手动续期预设。
- [ ] F12-R4：选定 SaaS 预设验证成员席位、存储及实际接入的额度消费者与付费状态一致；不以已交付席位切片替代其他消费者验收，也不重复实现统一配额内核。

**验收：** 对应真实渠道和内部履约事实可对账，重复回调/退款不重复授予或撤销权益；未验证渠道保留 `External-auth-not-verified`，真实收费不因计划编写获得授权。

### 8.9 P2：F18 查询方案与个人工作台（新增）

**归属：** 优先扩展 Settings/Client Preferences 与现有管理壳；首个消费者使用企业申请单列表，不为此预建独立模块项目。

**契约约束：** 查询方案由 Settings 持有强类型偏好事实，不存入通用 `ConfigEntry`；按用户、Host/Tenant 上下文、GridKey 和方案版本隔离。业务模块拥有字段/筛选目录及执行校验，Settings 不引用具体业务实现或解释任意 SQL。共享限当前租户并使用独立权限；个人方案和共享方案的修改、撤销及默认选择须各有明确所有者，精确动作权限在实施前登记。旧 Schema 方案拒绝或提示修正，不创建旧协议兼容运行层。

- [x] F18-1：已交付业务静态 Schema、个人方案和独立默认 CAS、更新/删除及体积/字段/运算符限制，见 §8.23。
- [x] F18-2：已交付创建者控制的租户共享/撤销、来源重新授权及旧 Schema 失效/修正，见 §8.23。
- [x] F18-3：已交付企业申请方案入口与账号/上下文隔离的浏览器常用入口，按当前可信导航重验，见 §8.23；常用入口不宣称跨设备同步。

**入口/验收：** `src/Modules/Full.NET.Modules.Settings/`、`packages/client-contracts/src/grid-preferences.ts`、Vue 列表/管理壳。覆盖保存恢复、跨用户/租户隔离、撤权、并发修改和旧方案失效；涉及持久化时成对迁移和聚焦双库，页面覆盖键盘与窄屏。

### 8.10 P2：F19 统一后台任务中心（新增）

**归属：** 原模块继续拥有任务状态、执行和恢复；先聚合 ImportExport 与 Reporting 两种本人任务，再按真实需求接入文档预览/AI。复用现有 Jobs 和各任务机制，不统一改造成另一套任务引擎。

**首版组合与权限：** 使用已有 Enterprise 预设的 ImportExport/Reporting，在 Vue 通过各来源 API 的最小适配器聚合，不新增中央任务表或服务端反向依赖。现有来源限定 Tenant 场景，首版为“当前租户我的任务”；管理员可查看范围不等于本人范围，须由来源按可信 UserId 过滤后再分页，不能先取管理员列表在前端过滤。来源未提供该能力时，在其所有者内补最小查询。Host 不回退为所有租户列表；未装配或无权来源隐藏，运行故障来源标为不可用。

**分页与操作：** 首版按来源分组、各自稳定分页，不宣称全局时间排序、合计或统一游标；去重键为来源类型与任务 ID。进度未知保留未知，来源未支持取消/重试即不提供该动作。结果只保留受控下载身份，不持久化可绕过授权的直链；切换会话/租户时清空结果并取消在途请求。

- [x] F19-1：已交付 ImportExport/Reporting 本人摘要、各来源游标分页和独立失败提示，见 §8.22。
- [x] F19-2：已交付 Vue 本人任务入口、筛选和来源详情；操作回到来源重验，不提供来源未支持的取消，见 §8.22。
- [x] F19-3：已交付有界只读轮询、撤权/会话切换清理及来源权威结果反馈，见 §8.22；后续来源按实际需求接入。

**入口/验收：** `ui/admin/src/views/ImportExportTasksView.vue`、`ReportingExportTasksView.vue` 及来源查询/共享客户端契约。覆盖两来源独立分页、同租户其他用户不可见、Host/跨租户拒绝、状态竞争、撤权、已支持的取消/重试、到期下载和迟到响应；聚合页面不另授业务操作权限，不凭页面成功宣称后台恢复已验。

### 8.11 P2：F17 跨业务统一搜索（新增）

**归属：** 复用菜单搜索壳及各业务查询授权；首批只接文档和企业申请单。先实现有界静态查询适配，不预先引入外部搜索引擎、全量索引同步或通用动态 SQL。

**组合边界：** Document 是可选模块，Enterprise 默认不因此增加 Document 依赖；样板启用两来源时显式配置合法模块闭包。现有文档项为 Host 数据，企业申请为 Tenant 数据，首版分别在合法上下文显示，不跨上下文搜索或自动切换租户。业务单据搜索适配保留在应用/样板拥有的代码中，以静态客户端注册接入搜索壳，官方框架不得引用 EnterpriseRequest 实现或表。来源使用自己已授权的查询 API，确需新增查询则在数据所有者内实现并生成客户端契约。

**响应与预算：** 首版按来源分组、独立分页和稳定排序，不承诺跨来源相关度、总量或全局页码。每来源明确输入长度、最小查询长度、返回上限、超时及并发上限；禁用/未授权来源不请求也不显示数量，超时来源单独标记，不将失败伪装为空结果。缓存如确需引入必须包含主体、作用域和授权版本；首版可不缓存。

- [x] F17-1：已交付文档/申请的最小摘要、固定近期窗口与有界分页，菜单/对象分组显示，见 §8.24。
- [x] F17-2：已交付来源权威会话、文档 ACL 和申请机构/租户过滤；只投影标题/编号，固定详情重验，见 §8.24。
- [x] F17-3：已交付来源独立状态/失败、250ms 防抖、8 秒超时、取消/迟到响应保护及菜单/对象键盘导航，见 §8.24。

**验收：** 两个实际业务来源可发现并正确跳转，撤权/跨租户/敏感字段不通过结果、计数或摘要泄漏；超时与取消有界。实施新增 SQL 时覆盖双库对应搜索场景，容量另测。

### 8.12 发布前：F15–F16-R 交付与恢复

**复用：** 已交付源码升级器、manifest 冲突保护、Helm、备份执行器与运行角色；本节补交付场景和演练，不重建运维平台。

- [ ] F15-R1：以实际选用预设和固定候选版本生成可复现分发清单、配置/秘密清单及安装说明；冻结实际支持组合，不虚构历史发布版或兼容窗口。
- [ ] F15-R2：扩展现有升级夹具，核对应用自有定制、业务数据、文件引用、任务及权限在升级/迁移中断恢复后保持正确。当前开发底座按直接演进规则处理，不为假定旧用户新增兼容层或强制混合版本运行。
- [ ] F15-R3：在 [应用恢复](../../operations/application-recovery.md) 中补数据库、对象文件、Data Protection/签名密钥及运行状态的联合恢复顺序；恢复对外流量和 Worker 副作用前检查依赖、会话与授权，避免恢复已撤销权限或重复外发。
- [ ] F16-R1：按所选预设实际演练备份恢复、Migrator→API/Worker 启动、排空/故障接管及受影响原生路径，预先确定并测量 RPO/RTO；只验证实际改变或尚无证据的交付场景。
- [ ] F16-R2：汇总有效证据和已知限制；未选模块不阻塞发布候选，外部渠道、生产启用与容量分别判定。容量未实测继续 `Capacity-not-verified`。

**出口：** 开发者可仅凭交付包复现选定预设，运维可按文档完成数据/文件/密钥恢复。不得把备份命令成功当作恢复成功，也不以本任务触发全量验收。

### 8.13 执行与去重规则

1. 每批开工只核对其条目对应的最新源码与专项证据，将已兑现增量从待办扣除；有实现但证据不足标为“补验收”，不重写实现。新的具体缺陷回到原所有者修复。
2. 一个功能批次连续完成后端、Vue、权限、配置和使用说明，再集中验证；按“改变的行为→精确用例→实际命令/结果”记录，数据库行为变化验证同场景双库，页面及 AOT 按实际可达影响验证。
3. 只运行修改或新增代码及直接必要边界；本地实际通过即可关闭相应条目，CI 可选。无本次影响的已通过用例不重复运行；全量测试须用户明确要求。
4. 新能力 F17–F19 在本文维护实施进度；涉及公共契约、迁移和授权时，在编码前补齐必要设计决定及文件落点，不预占迁移编号，不自动拆项目。
5. 本次为计划合并，核对基线与结构不构成功能验收。后续记录任务编号、实际输入、改变/复用范围、验证、未验证项和下一消费者；专项进度只维护在原入口，本文只更新依赖是否解除。

### 8.14 2026-11-02 复审结论与首批执行基线

本次修正了五类计划问题：历史与当前指令冲突、F11/C02 等待关系、F17/F19 聚合语义及模块边界缺失、F18 偏好所有权与共享约束不清、路线图优先级不一致。复审仅修改计划与路线图，没有实施或运行新功能。

**当前状态口径：** §8.1 是已交付/已有实现的复用清单；F09/F10/F11 本轮差集已关闭；F02-R、F03–F06-R、C02-R、F12-R、F13–F14-R、F15–F16-R 为“已有实现上的待补验收/必要增量”，不能按整项未开发处理。F17 文档/申请搜索、F18 企业申请方案与 F19 两来源本人任务已实现并通过聚焦功能验收，整体门禁保留差集（§8.22–8.24）。Workflow/通知/AI/Messaging 只消费各专项最新明确未关闭项，不复制其待办。复选框代表本轮增量是否关闭，不否定既有实现。

**第一开发批次固定为 F02-R：** 从现有独立应用夹具及应用真实 OpenAPI/DTO 开始，先核对各 R 子项证据，扣除已通过且输入适用部分；连续完成真实生成客户端写入/非空读取、应用 Vue 接线、权限拒绝与再生成保护及教程更新。首个落点是 `tests/templates/support/created-app-real-stack.mjs` 与 `docs/development/create-first-crud.md`，缺失实现回到实际生成器/模板所有者修改。不要先重写生成内核、重做 F01 或扩张 P2。

**首批交付检查表：**

- [ ] 执行记录列出实际基线/工作区摘要、复用证据与 F02-R1–R4 的真实剩余差集；检查实际输出目录和进程归属，沿用正式资源入口。
- [ ] 成功路径覆盖可信普通租户用户、非空业务对象、真实 Vue 到生成 API 的读写；负例覆盖缺精确权限、其他租户对象和人工修改保护，必要恢复用例与改变路径一致。
- [ ] 集中运行本批精确用例；SQL/事务/迁移改变按同场景双库，独立应用跨提供程序支持尚未验证的场景补对应证据；不因文档提到双库自动重跑全部矩阵。AOT 可达变更按规则覆盖分析及必要原生行为，分析不替代发布/运行。
- [ ] 将 F02-R 子项逐项标为“已交付/待补验收/阻塞”并附精确结果，教程能在声明环境复现；无失败、跳过、零发现冒充通过。按当次修改完成差异与分支终检，不自动提交、推送或全量测试。

只有 F02-R 的适用出口满足后才转入 F09–F11-R；若只是外部依赖或环境阻塞，可推进同批独立实现并准确记录阻塞范围。实现发现受影响的安全、隔离或数据缺陷在该批处理，不等待 P1。涉及新业务政策时先形成可审查的具体方案，不将不确定政策默认为已批准；普通工程选择不逐项等待用户“继续”。

### 8.15 F02-R 应用开发命令交付增量（2026-11-02）

用户要求以功能开发为主加快交付。基线仍为 `main / 75be6fc0e17433f9c59c42553093dacd479ef9d1`，任务快照 `foundation-f02-functional-delivery-20261102`；保留共享工作区现有业务客户端/Vue/浏览器实现，不重复编写这些夹具。

本批修复模板组装覆盖应用脚本的问题，提供 API/Worker/Vue 启动、三个宿主构建、Vue 构建/类型检查、只迁移/Baseline 播种、CRUD 及 OpenAPI 客户端生成/校验，共 15 个应用命令。根脚本从冻结模板清单投影，依赖和 pnpm 策略保持不变；项目名由模板引擎替换。补齐分发中缺失的 `packages/braces` 和 `patches`，应用脚本投影、workspace 包及补丁均保留摘要校验。使用说明已写回原教程及模板 README。

实际验证：新增应用命令/依赖保留/篡改拒绝及真实 pnpm 客户端生成、参数透传、零漂移与漂移失败 4/4 通过；直接相关的创建输入/前端完整性/迁移清单拒绝 3/3，以及既有诊断脚本/模板参数/脏源码分发拒绝 3/3 通过，合计 10 个唯一用例、零跳过。另以真实 `dotnet new` 在独立系统临时目录实例化最小模板，检查实际宿主命名替换、15 个命令、依赖配置及补丁原文字节保留；该检查只使用模板配置、包清单和小型补丁，不冒充完整应用创建或构建。首轮完整性测试因预期错误文本过严失败，已修正为模板引擎前边界检查，原失败不计通过。

状态：应用开发命令和依赖支持分发增量已实现并完成上述聚焦验证；F02-R4 仅教程/命令子集已推进，F02-R1–R3 及完整 F02 保持未关闭。没有运行数据库、全量测试或重新认证当前应用 Vue/原生闭包；完整源码包仍要求固定且干净的分发输入，本轮不通过绕过该门禁或自动提交制造通过结果。

### 8.16 连续功能开发增量（2026-11-02）

用户授权完成一项自动进入下一项，以功能开发为主，不逐项等待“继续”。本批基线仍为 `main / 75be6fc0e17433f9c59c42553093dacd479ef9d1`，快照 `foundation-continuous-delivery-20261102`；共享工作区既有改动继续保留，没有自动提交或发布。

已完成的增量不再重排：

- F02-R2 子集：CLI 模块接入目标支持显式 `clientModuleSpecifier`，业务 Vue 适配器从应用自有客户端模块导入操作和模型，HTTP 类型复用共享契约。默认生成结果不变；非法模块路径拒绝，接入绑定含该配置且二次规划不重复写入。该配置目前仅 CLI/core 支持，HTTP 工作台未扩大契约。实际聚焦测试 12/12 通过。
- F11-R1 导入子集：替换伪 XLSX/空预校验，实现真实模板、受限 ZIP/XML 读取、逐行错误及按有效行检查点执行；新增样例自有双库迁移 003，将行幂等账本和业务插入绑定同一事务。重复执行返回原 ID，变更源文件或内容拒绝；执行前重新验证组织归属。导入头单固定草稿，主从导入未声称完成。
- 后台运行闭包：ImportExport 的执行配置/校验/Schema 目录由 API 和 Worker 共用；Organization 为 Worker 提供职位导入和真实组织写入授权，样例 Worker 注册申请单导入处理器。未把整个 API 注册入口作为 Worker 的前置。
- Vue 导入入口：将禁用占位按钮替换为模板选择/下载、XLSX 上传、错误恢复和预校验结果展示；创建、目录及业务权限分别检查，会话/租户切换取消在途请求并忽略迟到结果。执行仍为单独操作。
- F10-R1 入口子集：审批按钮与调用改用独立提交权限；提交响应复用完整生成校验。样例页面在会话切换时清空旧数据及操作状态，取消请求并忽略旧结果。

验证边界：真实 XLSX 导入 SQL Server/MySQL 集成 2/2 通过；模板/预校验/行写入/Worker 独立注册聚焦 .NET 13/13 通过，零跳过；Vue 相关四文件 16/16 通过；导入页 Mock Playwright 3/3 通过（系统 Edge，1440×1000 和 390×1000，包含键盘、失败恢复、模板下载和 axe），Vue 类型检查已通过。完整 Unit 入口曾因 Roslyn/CLR 环境错误在执行前失败，改用系统临时项目链接同一源码完成上述聚焦测试，没有复制整仓或将环境失败记为通过。真实栈导入 UI 场景已替换占位断言，但本轮尚未执行。

F02-R1–R4 和 F09–F11-R 总项仍未关闭；完整应用分发仍受固定干净源码输入约束。下一批首先处理 F09-R1 的业务状态写入边界及对应主从场景，再衔接真实审批/通知页面；不重复开发本节已验证的模板、导入及 Worker 注册子集。Native AOT 分析和完整业务页面链路按实际结果另记，不由 JIT/Mock 结果推导通过。

### 8.17 主从业务写入与 Vue 编辑增量（2026-11-02）

已接入显式 `entityCapabilities.hasMutationPolicy`：要求确定的数据作用域和 Version，为生成的创建/更新/删除服务增加必需的强类型应用策略。策略运行于本模块命令事务；不注册即不能构造写入服务。样例头/明细均启用，应用自有 `Features/DraftMutations` 保存业务规则，`.g.cs` 由真实 CLI 重新生成，未在生成文件中手工打业务补丁。普通创建固定草稿，已提交主单不能更新/删除，明细写入锁定主单、拒绝改挂；Workflow 提交/回写继续走原业务操作。

聚合 GET/PUT 追加明细读取权限，避免只持主单权限就读取聚合响应中的明细；聚合写入仍归主单业务操作所有，不增加独立明细 HTTP 写入口。生成器组织授权将 `Success(false)` 同样视为拒绝，失败后不调用业务策略或执行写 SQL。此默认行为修正随生成器版本 `2026.11.02.1` 交付；应用按新模板重新生成，不能把先前默认字节保持的结论外推到此版本。

Vue 新增“创建主从单据”和“主从明细”弹窗，复用已有聚合接口；新增/修改/移除明细与主单一次保存，主从版本均使用加载快照，金额和 Int64 不经 JS Number。版本冲突保留编辑内容，关闭重开读取最新快照；已提交或无更新权限只读，缺明细读取权限不显示现有明细入口，会话切换取消在途读写及清空数据。业务 OpenAPI 和客户端属于样例自身，HTTP 运行时复用共享契约；冻结快照与四个生成客户端文件可确定性校验。保留原简单头单表单，不重复创建另一条业务样板。

当前证据：相关 .NET 聚焦 23/23 通过（含生成器开关、作用域/版本拒绝、草稿策略、组织授权 false、导入和 Worker 注册），SQL Server/MySQL 的真实聚合创建→审批提交→CRUD/内部明细/聚合冻结及端点权限元数据 2/2 通过；Vue 六文件 25/25 通过，主从 Mock Playwright 4/4 通过（Edge，1440×1000 / 390×1000，冲突恢复、增改删、键盘、只读、权限隐藏、axe），导入 Mock 3/3 保留其已验范围；业务 OpenAPI/客户端及 Schema 检查 2/2 通过。真实栈 Vue 主从交互尚未执行，不能据此标记完整业务流程 Verified。

补验结果：真实数据库主单锁与提交交错 SQL Server/MySQL 各 1 项通过，均观察到数据库锁等待；MySQL 的诊断读取使用测试夹具管理连接，未扩大应用账号权限。SQL Server + Kestrel + Vue 的真实页面 2/2 通过（1 个 .NET 夹具，主从创建/增改删/提交/冻结与模板下载/无效行反馈/有效导入），零跳过、零 flaky；该切片尚未包含审批待办处理及通知。生产 Vue 构建和最新类型检查通过；业务客户端生成选择器按生成器新版本重新验证 12/12 通过。样例项目及 CodeGeneration 项目的聚焦 AOT 分析均零警告/错误，使用独占系统临时输出后清理；这不等于完整宿主 Native 发布/运行认证。

其余申请人/部门/字段权限、审批/通知页面及文档完整链路继续按 §8.4 处理。F09-R1、F10-R1、F11-R1 总项保持未关闭，已交付子集不再重复排期。下一开发落点为 F10 的受控业务详情导航、审批待办处理到回写/通知，复用现有 Outbox 和 Workflow，不重建工作流内核。

### 8.18 审批详情导航与后台交付体验增量（2026-11-03）

工作流待办、实例和抄送的业务详情映射增加 `demo.enterprise_request`；采用静态 Map 白名单并同时检查主单、明细读取权限，点击时重新判断当前会话。申请页面只接收非空 UUID 深链，关闭后清除查询参数，离开页面或切换会话即关闭旧单据；实际对象授权仍由业务 API 执行。原数据审批导航同时补精确读取权限检查，不接受任意 URL 或原型属性键。

导入详情针对 `queued/executing` 每两秒读取权威状态并同步当前列表行；终态停止，网络失败或五分钟预算耗尽后暂停并提供手动刷新。单次读取三十秒取消，隐藏页面暂停，关闭、离开页面、更换详情和会话/权限变化取消在途读取，不重放执行操作。该增量属于原 ImportExport 页面，未建设或提前关闭 F19 统一任务中心。

附件与文档页面将权限集合及切换状态纳入同步失效边界，撤销金额或操作权限立即关闭并清除旧预览；迟到的附件下载不得生成浏览器链接，旧主单读取不得继续发起新上下文的附件/报表查询。保留现有服务端会话、源版本和字段权限复验。

真实链路发现并已修复两处功能问题：Identity 后台补数据范围解析器/SQL 过滤构建器，Organization 后台补机构范围投影，避免独立 Worker 的文档授权闭包缺失；通知系统分组使用导航权限并通过目录同步修复既有系统目录，防止 Host 公告权限隐藏租户收件箱，叶子权限仍独立检查。文档弹窗提升为页面级受控实例，附件更新刷新列表不再销毁弹窗。测试夹具显式断言实际 Outbox 循环保留，复用 API 的实际 Data Protection 保护器，使用异步 Scope 释放。

当前聚焦证据：导航及工作流页面 73 项、导入轮询/页面 13 项、附件与文档组件 4 项、列表刷新保留文档弹窗 2 项，共 92 个唯一前端用例通过；后台数据范围 2/2 通过，保留宿主覆盖且不引入 HTTP 授权注册。通知目录修复 SQL Server/MySQL 2/2 通过，覆盖已有错误系统目录、重复同步幂等和租户显示收件箱/隐藏 Host 公告。最新 Vue 类型检查、生产构建和 .NET 集成项目定向构建通过；生产构建保留原有大块提示，未提升容量状态。

SQL Server + Kestrel + Vue + 独立 Worker 的完整页面 2/2 通过（1 个 .NET 夹具，零跳过/零 flaky）：主从增改删→提交→待办业务详情往返→审批→业务回写 Approved→查看完成消息；同一导入单据完成模板下载/错误反馈→真实异步执行→附件上传/下载原文→后台报表下载→同单据打印。仅用生产 Outbox、通知、导入和样例交付循环，不直接派发 Handler。附件与文档 Mock Playwright 在页面级弹窗改造后重新通过 3/3（Edge 桌面/390 窄屏、上传重试、下载、打印净化及 axe）。早期配置/装配失败及错误的页面刷新断言均已定位，不计入通过数量。Identity/Organization 受影响项目的聚焦 AOT 分析零警告/错误，独立临时构建输出已按批次清理；未运行全量测试或完整 Native 发布。

下一批为 F09 剩余普通成员/部门和字段权限矩阵；F10 仍保留退回/拒绝、中断及重复事件与处理中撤权的剩余边界，不以正常完成链路关闭总项。F11 仍保留普通成员字段权限及排队后撤权的剩余差集，不重复已交付子集。F09–F11-R 总项保持未关闭。

### 8.19 普通成员边界与报表状态交付（2026-11-03）

F09-R1/R2 已关闭其本轮增量。通过真实邀请接受的普通 Member、显式业务角色及部门分配验证原 CRUD、主从和旧导入；遵守默认 `SingleSessionPerClient`，管理准备阶段重新取得当前作用域令牌，不关闭会话校验。不存在预设第二租户的夹具通过正式开通 API 创建实际隔离租户，未伪造 TenantId 或放宽权限。

真实普通成员查询暴露计数 SQL 仍使用旧 `WHERE TenantId` 锚点，现行过滤计数模板实际为 `WHERE 1=1 AND TenantId`，带数据范围时抛错。生成器改为按固定 `AND TenantId = @TenantId` 条件追加计数和分页过滤，保留失败关闭及租户边界。版本更新为 `2026.11.03.1`，真实 CLI 再生成主单/明细，仅复制生成结果，不在 `.g.cs` 内手工修补。

报表弹窗对当前页 `Queued/Running` 每五秒刷新，只更新任务状态，保留主单版本快照；终态停止、隐藏页面暂停，三十秒单次超时或五分钟预算耗尽后暂停，可手动刷新恢复。关闭、写操作、权限/会话变化取消旧读取，不重放创建。Rejected 中文明确为“已驳回”，与流程内退回动作区分。

实际结果：SQL Server/MySQL 普通成员场景 2/2 通过，包括限定部门列表/详情、无权部门写入拒绝、版本冲突、明细缺权拒绝、打印金额脱敏、真实第二租户隔离，以及真实 XLSX 导入成功/无权部门行失败且不落业务数据。导入在该 API 边界专项使用测试同步执行，独立 Worker 另有真实 UI 证据。生成器客户端 12 项及数据范围/后台注册 4 项共 16/16 通过；业务 Schema/应用客户端快照 2/2 通过。样例和 CodeGeneration 项目聚焦 AOT 分析零警告/错误，临时二进制已清理。

SQL Server + Kestrel + Vue + Worker 的真实浏览器扩为 3/3 通过（1 个 .NET 夹具，零跳过/零 flaky）：通过审批、驳回审批和导入文档交付；驳回回写 Rejected 并通知，报表明确从 Queued 自动刷新到可下载后取得真实 XLSX，包含同单据附件和打印。文档组件本批 10/10 通过，Vue 类型检查和生产构建通过。F10 的流程内退回、真实跨进程中断及处理中撤权，F11 的排队后撤权剩余差集仍保持未关闭；不将该证据外推为 MySQL 浏览器、完整 Native 或容量认证。

下一功能批次为 C02-R2 的通用 Reporting 导出页面：先补现有列表/定义/创建/下载的会话失效与取消边界，再完善任务状态刷新和到期结果反馈；保持数据/执行所有权在 Reporting，不提前建设 F19 中央任务表。

### 8.20 C02 Reporting 租户导出闭包复审与实施边界（2026-11-03）

源码核对发现通用 Reporting 尚未形成可用的租户闭包：定义、版本和数据源属于 Host，权限也是 Host；导出任务及资源文件却要求 Tenant。当前 Query Port 是受控外部数据库结构查询，不能直接把 Host SQL 降为 Global 或把外部数据源目录开放给所有租户。F11 样例通过授权投影消费 Workbook Renderer 的已验能力不受此缺口影响，不重写样例交付链路。

本批依以下具体设计连续实施，统一归 Reporting 所有：

1. **发布授权：** Host 保留数据源、定义编辑与发布。新增定义到租户的显式启用授权，默认不共享；授权台账只保存本模块定义 ID、目标 TenantId、启用状态、版本和审计时间，不跨模块写租户表或建立跨模块外键。Host 授权读取/管理使用独立精确权限；目标租户通过现有 Contract 验证，跨模块验证在本模块事务之外。
2. **租户目录：** 新增已发布报表最小只读目录（参数 Schema 随目录项返回，不另建重复详情接口），仅返回定义 ID、键、名称、固定发布版本及参数 Schema。由当前可信 TenantId、有效授权、定义启用及已发布版本过滤后再计数/分页；不返回数据源身份、连接信息、密码、布局内部配置或 SQL。租户目录使用独立读取权限，不复用 Host 定义管理权限。
3. **执行读取：** 本模块新增受授权台账约束的 TenantRequired 只读查询，读取固定发布版本及其内部数据源元数据，保留当前租户上下文；Host 管理查询维持 HostOnly。外部目标批准、只读静态 Query Port、输出预算与字段权限继续复用。明确授予的定义才可由对应租户执行，不新增任意 SQL 或配置脚本。
4. **任务权限与目录：** 导出创建/读取/下载归 Tenant；导航分组只承载导航资格，叶子独立授权。Vue 使用租户已发布目录选择报表，不请求 Host 定义编辑目录。只读任务账号可独立加载历史任务，定义目录故障不阻断历史任务读取。
5. **会话与撤权：** 创建冻结可信原会话绑定、发布版本及实际输出所需权限；Worker 在外部查询、文件副作用及完成阶段重新验证原会话、成员、发布授权和原字段权限，新增权限不扩大旧任务。下载要求本人任务、当前下载权限及原会话/授权仍有效；来源或原输出字段撤权后拒绝交付。既有缺少可信绑定的任务不补造会话，后续执行/下载失败关闭。
6. **页面体验：** 统一复用有界只读刷新，取消旧列表、目录搜索、定义选择及下载请求并拒绝切换后的迟到响应；创建失败保留输入，结果失效按服务端实际错误反馈。现行任务摘要没有结果有效期，页面不自行推算过期时间或未知进度。取消/重试仅在所有者提供真实操作时接入。

实施落点：Reporting Contracts/授权贡献者、`Features/ManageDefinitionGrants`、受授权执行目录、ExportTasks 管理/Worker/文件所有者与配对迁移，Vue 定义授权入口和导出页面、共享生成客户端及 OpenAPI。不提前预占迁移编号；新增 DTO/绑定进入静态 JSON 与 Dapper AOT 闭包。

验收固定为 Host 发布→显式租户授权→普通租户账号目录选择→排队导出→真实 Worker→下载 XLSX；双库覆盖授权分页/计数、无授权/其他租户、停用/撤销授权、排队后会话或字段撤权、下载复验及并发版本。Vue 覆盖历史只读权限、独立目录故障、搜索竞争、创建/下载及会话失效；原 ImportExport 与样例轮询只验直接受影响边界，不执行全量测试。外部数据库厂商链路、完整 Native 和容量状态分别留证。

### 8.21 Reporting 租户发布与普通成员交付（2026-11-03）

C02-R2 的 Reporting 子批已实现并通过本地聚焦验收。新增 Host 显式租户授权、最小发布目录、配对迁移 353 与私有原会话绑定；任务由 API 排队、真实 Worker 执行。目录在计数和分页前过滤授权、启用状态及数据源归属，固定发布版本与实际可见列权限；查询、上传、完成、恢复绑定和本人下载逐阶段复核。API Key/签名不替代用户会话，旧任务不补造绑定。共享客户端和 OpenAPI 已同步，Vue 只读取租户发布目录并复用有界刷新，保留输入与只读历史任务的独立读取。

普通成员实际流程发现并修复两层导航依赖：模块及领域系统目录只要求导航资格；导航读取不再作为工作台页面动作。Baseline 目录版本推进至 3，修复仅限已知系统行并按更新计数，移除旧系统按钮；自定义目录及叶子权限保留。登录/切租户的根路径在没有工作台权限时选择已授权、本地登记的页面；无可用页面仍返回 403。

实际证据：SQL Server/MySQL 普通 Member 与真实 Worker 场景 2/2，通过授权计数/分页、未授权/另一租户、数据源归属和停用、授权版本冲突、普通成员文件下载、同租户超级管理员拒绝下载他人结果、原会话及角色字段撤权、已有系统目录修复与重复同步幂等。角色权限更新按现行规则轮换安全戳并撤销会话，未伪造继续有效的绑定；另用真实后台授权器验证原会话仍有效时单独失去原列权限也被拒绝。

真实 Edge + Kestrel + SQL Server + 独立 Worker 宿主 1/1 通过：Host 页面授权→受邀普通 Member 登录/进入租户→发布版本选择→排队→自动更新成功→下载 XLSX；桌面与 390 窄屏、键盘关闭、授权弹窗 axe 均通过，零跳过/零 flaky。普通成员未授予工作台、Host 定义或数据源管理权限。外部数据库端口采用受控连接替身，未将此结果计作厂商网络/TLS 认证；Worker 为独立 IHost，不计为跨进程强杀证明。

定向单测 13/13、Vue 相关 50 个唯一用例、类型检查及生产构建通过；生产构建保留既有大块提示。真实 OpenAPI 契约检查 1/1、离线快照及生成产物零漂移；Reporting/Identity 聚焦 AOT 分析均零警告/错误，未运行全量测试、完整 Native 发布或容量实测。早期夹具状态码、角色层级和默认首页假设失败已定位，不计入通过数量。运维接入见 [报表连接与发布](../../operations/reporting-external-database-access.md)。

C02-R2 总项仍保留其他来源预览/过期结果等差集，不重复安排本批 Reporting。下一功能为 §8.10 F19 的本人任务聚合，沿用本批状态读取和原模块执行所有权。

### 8.22 F19 本人任务中心实施批次（2026-11-03）

本批连续交付 F19-1 至 F19-3 的首版关联功能：Platform 贡献 Tenant 范围的独立页面资格；Vue 通过固定 ImportExport/Reporting 适配器读取当前租户本人任务，不新增中央任务表、不引入服务端反向模块依赖。在来源新增本人游标与状态筛选端点，UserId 仅取可信认证主体，Tenant/User 在源 SQL 分页前固定过滤；原管理员列表语义保留。游标按 CreatedAtUtc/Id 稳定降序，绑定来源、租户、用户及筛选上下文，不承担授权。按新查询实际所需补配对索引，不预占迁移编号。

两来源独立游标分页和筛选，最多读取页大小加一行摘要，不读取执行计划、预览正文或原会话 JSON；提供各自上一页/下一页，不宣称列表总数。缺权/未装配隐藏，单来源失败单独提示。摘要只表达已知进度和来源实际结果信息，不推算到期或全局总数。详情跳回来源页面，下载/执行/恢复/重试只调用来源已有操作并重新检查状态和权限，不提供来源未支持的取消。切换身份/租户、撤权或离开页面时清空并取消旧请求；复用五分钟预算、三十秒请求上限的只读轮询，不自动重放写操作。

一次完成来源 API/SQL、共享生成契约、来源详情深链、Platform 导航和 Vue 页面后集中验收。限定双库验证本人计数/分页、同租户他人隔离、Host/跨租户和状态筛选；Vue 验证来源独立失败、分页、真实操作、迟到响应与撤权；复用原执行/恢复证据，不重跑已验执行引擎或全量测试。

本批查询资源契约：两种来源各一次有界 SQL 往返，复用认证/可信租户/精确权限管线，聚合页不添加目录或文件逐行查询、不添加缓存或外部 I/O。API 默认页大小 20、最大 100，游标查询最多读取 101 行。新增索引以相同 SQL、数据和环境比较无候选索引/有索引两组；合成数据限定每来源 2,000 行、2 租户、每租户 10 用户，单并发，预热 5 次、每组采样 30 次；记录双库计划、扫描/排序、P50/P95/P99 与索引写入成本。该规模只验证本批查询，不提升容量状态。Vue 页面保持路由延迟加载，记录构建包体及首屏预算。先完成关联功能，再集中采集上述新增查询证据。

本批交付记录：`foundation-f19-my-tasks-20261103` 快照，基线仍为 `main / 75be6fc0e17433f9c59c42553093dacd479ef9d1`。已交付 `platform.my_tasks.read` 独立 Tenant 页面资格、ImportExport/Reporting 本人游标端点、默认 20/最大 100 的摘要投影、配对迁移 354、两个来源详情深链、精确动作派发及有界刷新。导航 Baseline 升至第 4 版，只修复 Platform 系统容器的旧页面权限；不扩大页面或操作权限。

集中验证：3/3 游标单测、34/34 直接相关 Vue 用例通过，类型检查与 Release Vite 构建通过。本人查询在真实 SQL Server/MySQL 中覆盖同租户他人、第二租户、Host 拒绝、状态筛选、相同时间的稳定翻页、跨来源/主体/筛选游标拒绝、旧管理员列表和导航修复幂等；真实 Edge/Kestrel/SQL Server/后台循环走通“创建报表导出→我的任务→下载→来源详情”，桌面/390 窄屏及当前页 axe 无 critical/serious。原始两库结果分别通过；首轮 MySQL 迁移连接会话变量和密码复用错误只属于测试夹具，修正后重跑通过。Reporting、ImportExport、Platform 的模块 AOT 分析均 0 警告/0 错误，不等同完整 Native 发布。真实运行时 OpenAPI 导出 1 项通过，规范快照及四份生成客户端已更新、离线校验零漂移。

查询实测与环境见 [F19 查询与页面验证](../../verification/2026-11-03-my-tasks-query-and-ui.md)。两库每来源 2,000 行、2 租户/各 10 用户、单并发；MySQL 首轮统计信息陈旧导致未选索引，基线/候选均 ANALYZE 后重测，候选断言检查实际 key 而非 possible_keys，三个形状无 filesort。SQL Server 候选消除显式排序；不从该规模推导容量。当前全站首屏 JS 超出既定预算，新任务页为延迟 chunk 且不进入首屏静态图；命名结构检查 36/37，唯一余项是本批未修改的 352 MySQL 心跳主键名称。以上两项保留失败事实，不改预算或豁免规则制造通过。已补齐本批 353 表/列注释清单与固定 DDL 的精确审查记录。

F19 三项按 ImportExport/Reporting 首版关闭；文档预览/AI 来源继续按实际需求增量接入，不重复建立执行引擎。未运行全量测试、完整 Native 发布或更大规模容量测试，容量状态保持 `Capacity-not-verified`。本批完成后继续 §8.9 F18 的查询方案与个人入口功能。

### 8.23 F18 查询方案实施契约（2026-11-03）

快照 `foundation-f18-query-schemes-20261103`，基线仍为 main / 75be6fc0e17433f9c59c42553093dacd479ef9d1。复用企业申请已生成且带严格参数白名单的列表查询，不手改生成文件。Settings.Contracts 只发布静态 Schema 贡献契约，企业样例声明自己的 Grid、读取权限、包含筛选字段、排序与展示列；Settings 不引用或解释业务 SQL，不建立反向模块依赖。首个消费者只支持租户，框架按实际 Schema 声明约束 Host/Tenant，未知 Schema 失败关闭。

新增强类型方案与个人默认记录，脱离 ConfigEntry；可信会话决定 UserId/上下文。ScopeKey 为可信 host 或 tenant:UUID(N) 字符串，仅作唯一作用域键，TenantId 同步保留并由 SQL Scope 绑定；不使用被持久化边界拒绝的空 GUID；所有正式主键仍是应用端 UUID v7。个人作用域行锁串行化同用户/Grid 写入，每上下文/Grid 最多 32 个个人方案；JSON 最大 16 KiB、筛选最多 8、列最多 32，字段/运算符/宽度/顺序/字符串上限由注册 Schema 校验。来源元数据在本地静态闭包聚合，不执行跨模块事务。无缓存、事件或可靠后台推进需求。

精确权限为 settings.query_schemes.read/create/update/delete/set_default（Host|Tenant）及 share（Tenant）；每个端点还以当前会话权威确认业务 Grid 的读取权限。只有创建者修改、删除、共享或撤销自己的方案。共享仅在当前租户可读，其他人只能选用条件；不能编辑他人方案、取得结果或额外权限。默认选择是每用户/上下文/Grid 的独立 CAS 记录，撤销共享后旧默认显示失效，不泄漏新内容或替别人改偏好。更新、删除、共享及默认设置均显式携带版本，409 后刷新，不盲目重放。

列表按本人/共享分别用稳定时间/Id 游标，默认 20、最大 100，单次来源查询最多 101 行，只返回摘要，详情按需读取配置；默认与可见方案在 Settings 本地查询，不逐条回源。Schema 变化保留历史元数据并明确 isCompatible=false，拒绝应用及旧 Schema 更新，允许本人删除或用新 Schema 修正。原有列偏好类型复用，业务列表每次执行仍经过原权限、组织与租户过滤。

Vue 提供筛选/排序/列、保存/更新/删除、设默认、共享/撤销和失效反馈；详情及共享应用先取当前权威配置。个人常用入口只保存稳定路由名，按用户/Host或租户上下文限定、有界保存，使用现有导航白名单和当前可见导航重验，不持久化任意 URL。一个完整关联批次后集中验证保存恢复、本人/他人/Host/跨租户、撤权、CAS、并发上限、共享撤销/默认失效及新旧 Schema；双库仅覆盖实际新增数据场景，页面覆盖键盘、窄屏与真实列表查询。

本批首个消费者与三个功能子项已完成。13/13 聚焦 .NET 单测、35/35 Vue 用例、实际 OpenAPI 导出与生成零漂移、双库 API、真实 Edge/Kestrel/SQL Server 页面均通过；双库新查询计划实际使用本人/共享索引且无显式排序。检查发现的测试角色旧数据范围值、普通成员列表漏验、按钮对比度及 SQL Server 游标时间参数舍入已处理，未放宽权限、主键或限流门禁。详见 [F18 验证记录](../../verification/2026-11-03-query-schemes.md) 与 [使用及接入说明](../../development/query-schemes.md)。全站首屏包体与既有 352 MySQL 命名余项仍保留失败，不以功能通过替代整体发布门禁。继续 F17 跨业务搜索，不重复安排本批已验功能。

### 8.24 F17 统一搜索实施契约（2026-11-03）

快照 `foundation-f17-object-search-20261103`，基线仍为 main / 75be6fc0e17433f9c59c42553093dacd479ef9d1。搜索壳只接收应用静态注册的来源，不引用业务模块或表。Document 的 Host 文档与样例的 Tenant 企业申请分别提供所有者内摘要查询；不会为了同时展示两组结果跨上下文读取或改变 Enterprise 依赖。来源在精确权限、可信会话及原有 ACL/数据范围过滤后返回最小 Id/标题/编号；不返回正文、金额、内部会话或结果总数，点击详情重新授权。

新增专用近期搜索，避免复用无时间范围的 contains 列表扫描。服务端固定最近 90 个 UTC 自然日，关键词去首尾空白后 2–64 字符，转义 LIKE 通配符并拒绝控制字符；固定每页 5 项、最多 20 页，每次只取 6 项探测后续，以 CreatedAtUtc/Id 稳定排序。越界、重复或未知参数拒绝。达到上限明确提示转到来源页面检索，不把截断伪装成全量结果。新增索引按来源所有权配对迁移，在双库检查计划及部分完成重入；不新增搜索引擎、缓存、中央索引、事件或动态业务 SQL。

客户端输入 250ms 防抖，各来源独立分页/错误/重试；单来源 8 秒超时、每源最多一个在途请求、最多两个来源并发，取消与上下文版本共同拒绝旧响应。未授权、未装配或不支持当前上下文的来源不请求也不显示数量。对象结果与菜单明确分组，保留方向键、Enter、Escape 和常用入口的原生键盘行为。详情使用固定来源路由与 UUID 参数，禁止服务端 URL 直跳；增加文档只读详情深链，企业申请复用已有申请详情。

关联 API/SQL/契约、搜索壳、来源详情和输入取消边界一次完成后集中验收：双库文字转义/时间窗/分页、对象 ACL 与机构隔离、Host/Tenant/撤权及最小摘要；Vue 独立故障、迟到响应、键盘与真实浏览器来源跳转。资源证据限定各来源 2,000 行、单并发、5 次预热/20 次采样，记录计划和查询尾延迟，不提升容量状态，不运行全量测试。

本批三个功能子项已完成。双库搜索与普通成员矩阵 4/4、真实浏览器 1/1、运行时 OpenAPI 导出 1 项，以及 61 个唯一前端用例通过；最新类型检查、生产构建及两个模块 AOT 分析通过。真实双库计划保留，按所有权的两对迁移覆盖部分完成重放。补齐主单只读详情、深链取消和详情期间恢复默认方案的边界。详见 [F17 验证记录](../../verification/2026-11-03-object-search.md) 与 [业务对象搜索](../../development/object-search.md)。全站首屏包体和既有 MySQL 352 命名问题仍保留差集，不重复安排已验的 F17–F19 功能；下一功能批次回到 C02-R1 的可保存导入列映射配置。

### 8.25 C02-R1 可保存导入列映射实施契约（2026-11-03）

快照 `foundation-c02-import-profiles-20261103`，基线仍为 main / 75be6fc0e17433f9c59c42553093dacd479ef9d1。ImportExport 持有个人导入配置及任务输入快照，不能写入通用 Settings ConfigEntry。首批为企业申请和 Organization 职位两个真实静态 Schema；业务仍拥有类型转换、行校验、机构授权、命令与幂等账本，不开放动态字段、SQL、表达式、脚本或第二套执行器。

个人配置按可信 Tenant/User/Schema/Worksheet 隔离，Host 不回退读取租户数据；每个作用域最多 32 条，名称 64 字符、配置 16 KiB。权限为 import_export.import_profiles.read/create/update/delete（Tenant），各请求另外检查静态目录与来源精确导入权限。只有本人可读取或修改，超管不绕过个人所有权；更新/删除使用 CAS，未知写入结果先刷新，不盲目重放。来源授权在本模块事务前完成，用户作用域行锁串行化数量上限与修改；列表最多 32 条摘要，不读取配置正文或逐行回源。无缓存/事件需求。

映射只把注册的目标表头绑定到文件首行的来源表头；目标列完整且唯一、来源名称非空且不重复，不允许漏填后回退猜测。最多 32 个来源列，来源名称最长 128 字符；首尾空白规范化，按区分大小写的字面名称匹配，重复表头失败关闭。标准模板仍下载既有静态文件；自定义模式允许别名、重排和忽略未映射列，保留原始行号。两个 XLSX 读取器共同使用纯表头策略，保持文件/ZIP/XML/行数上界及公式、外部关系拒绝；仅将明确映射模式的列数/共享字符串上界调整到相应有限预算。

以静态目标表头的有序指纹判断配置是否仍兼容，明确它只表示表头结构，不冒充整套业务规则版本。旧配置不自动应用，允许本人按当前表头修正或删除。任务创建前验证客户端看到的目标表头与当前目录一致，规范化映射并固化到任务私有 ColumnMappingJson；预校验、Worker、恢复/重试使用同一快照，修改/删除个人配置不影响既有任务。任务不能在执行请求中覆盖映射；快照损坏或当前表头不兼容时拒绝执行，不静默使用标准模板。任务详情仅按原权限展示可读映射，不输出私有快照 JSON。

新增个人配置/作用域锁表及任务输入列的配对迁移，实际编码前确认编号；表与列按命名/注释规则登记，DDL 部分完成可重入。导入创建弹窗提供配置选择、映射编辑、另存/更新/删除和失效提示；切换 Schema/Worksheet、账号、会话、租户或撤权时取消读取并清空文件/旧配置。完成关联功能后集中验证双库本人/他人/Host/第二租户、CAS/上限、表头变更、映射及原始行号、修改配置后已排队任务仍按冻结输入执行；复用原执行引擎与行幂等证据，只补直接受影响的真实 Worker 与页面场景。不执行全量测试或重复原正常模板功能。

接线审查发现原导入创建/排队只检查通用操作权限，Worker 仅使用排队时复制的能力标记，且恢复会丢失已准备的纯数据计划。本批补齐这些直接影响映射任务的 F11-R1 边界：创建前权威检查来源权限；仅原上传者可显式排队/恢复/重试，并绑定该次有效交互会话；Worker 每批和实际行写入前复核该绑定与来源/字段权限。后台不自行采用新会话，旧无绑定队列失败关闭。明确的本人恢复操作可以重新绑定当前有效会话，仍保留原任务输入、能力上限与纯数据计划，不借恢复扩大原字段含义。业务授权 Port 在本模块写事务之前执行。ImportExport.Contracts 为真实工作簿消费者复用 Identity 的会话 DTO；共享声明提取器不承担权威授权。专项验证缺来源权限、排队后撤权/失效会话、他人接管拒绝及恢复计划保留，不重复已验执行内核。

本批已实现并通过聚焦验收：45 个 .NET 单元、21 个 Vue 用例、双库 API/真实 Worker 2 项、真实浏览器 1 项及运行时 OpenAPI 导出 1 项通过；三个受影响模块 AOT 分析、类型检查与生产构建通过。已覆盖同一用户第二租户、32 条并发上限、部分迁移重放、无绑定队列及会话撤销。详情见[验证记录](../../verification/2026-11-03-import-profiles.md)与[使用说明](../../development/import-profiles.md)。首屏包体与既有 352 命名门禁差集保留，未认证容量或完整 Native 发布。不再重复安排已验的个人映射与导入权限子集，下一批推进 C02-R2 尚缺的任务取消操作资格。

### 8.26 C02-R2 本人待执行导入取消（2026-11-03）

快照 `foundation-c02-task-cancel-20261103`。本批补当前 ImportExport 缺失的取消功能，不重建已有恢复、重试、本人入口或映射配置。首版仅允许本人预校验成功、排队中、等待恢复的部分成功任务取消，终态为 `canceled`；已经写入的业务行、进度、冻结映射、纯数据计划及错误回执保留。取消后不能继续排队或重试，不承诺撤销既有业务数据。

执行中的批次可能已经提交业务行而尚未记录检查点，不能直接把其状态改成已取消。此时明确返回状态冲突，页面提示等待批次结束后刷新。对排队任务，取消与 Worker 领取通过同一任务行的状态/版本 CAS 竞争；取消成功后不得再领取，Worker 先领取则取消返回 409。不得按过期 UI 快照覆盖正在执行的状态，也不从本模块跨写业务表。

使用独立 Tenant 权限 `import_export.import_tasks.cancel`，再次权威校验当前交互会话和原上传者身份；管理员也不能替他人取消。停止本人待执行任务不要求仍具有来源写权限，避免已失去业务写资格的用户无法停止自己的排队工作。请求携带详情版本，重复取消已经取消的本人任务可返回当前详情。Host、其他租户/用户、缺少或撤销取消权限均拒绝。SQL Server/MySQL 同步扩展状态约束，并验证部分 DDL 重放。

原任务详情提供确认、资格说明和取消后的准确状态；执行中不显示可用取消按钮。本人任务中心识别来源新增状态，取消仍回到来源详情确认，不另写执行器。保留所有权、会话和来源响应边界，未知写入结果显式重读，不自动重放。集中验证双库取消/领取竞争、陈旧版本、进度与输入保留、取消后无 Worker 写入、授权隔离，以及真实页面确认/状态刷新。C02-R2 总项保留其他来源取消、结果过期等差集。

本批已完成：16 个 .NET 单元、31 个前端用例、双库真实迁移/API/Worker 2 项、真实浏览器 1 项及运行时 OpenAPI 导出 1 项通过；ImportExport AOT 分析、类型检查和生产构建通过。修复新终态标签对比度，并按真实迁移验证 MySQL CHECK 恢复，未放宽权限或结构门槛。见[验证记录](../../verification/2026-11-03-import-task-cancellation.md)与[使用说明](../../development/import-task-cancellation.md)。全站包体与既有 352 命名差集继续保留；下一批推进本人待执行报表导出取消。

### 8.27 C02-R2 本人待执行报表取消（2026-11-03）

快照 `foundation-c02-report-cancel-20261103`。延续现有租户报表导出闭包，只补 `queued` 任务的原发起人取消；不新增 Host 导出路径，不重建执行器或下载授权。新增 Tenant 精确权限 `reporting.export_tasks.cancel`，权威检查当前交互会话和本人/租户。停止排队工作不依赖仍具有发布授权或来源列权限，不能由聚合页直接改状态。

详情补充存储中已有的任务版本，取消请求携带最近读取的版本。只在 queued 且无租约时以状态/版本 CAS 改为 `canceled`，保留原参数、发布版本、权限上限、原会话和文件引用。Worker 先领取为 processing 时返回 409，不能覆盖在途生成或撤销未知上传结果；取消先成功则不得再领取或生成文件。重复取消本人已取消任务安全返回当前详情；他人、其他租户、Host、失效会话或撤销取消权限均拒绝。配对扩展状态约束，约束恢复使用真实迁移库验证。

Vue 原详情提供确认、处理中资格说明、未知结果显式重读和准确终态；本人任务中心新增报表 canceled 筛选与回来源详情的取消入口。使用真实发布定义验证双库排队、领取竞争、取消后不生成、快照保留及来源授权撤销后仍可停止；补直接受影响的 DTO/权限/状态、前端、真实浏览器与 AOT 分析，不扩成 Reporting 全量验收。结果过期、预览及中断在途生成仍保留为 C02-R2 后续差集。

本批聚焦验收已通过，最终证据统一见 §8.29 末段与增量验证记录；不重复安排已经完成的本人待执行取消。资源等待未降低 4 GiB 系统保留门槛。

### 8.28 C02-R2 文档预览控制页面（2026-11-03）

快照 `foundation-c02-preview-task-ui-20261103`，基线仍为 main / 75be6fc0e17433f9c59c42553093dacd479ef9d1。核对现有实现后确认：Document 已有 Host 预览任务取消、独立人工重试、版本竞争、在途结果提交围栏及 Files 引用释放。本批不重新安排这些后端功能，也不把 Document 的 `cancelled` 改成其他来源的状态键。

只补真实页面缺口：取消前展示文档标题和点击时版本确认；五秒读取仍在 preparing/pending/processing 的列表，复用五分钟预算及三十秒请求上限，终态停止、错误暂停、手动恢复；转换能力关闭时保留已有读取和取消。取消/创建/重试遇到未知结果或版本竞争，停止自动读取与写入重放，显式刷新后才能继续。身份、租户、会话、权限、切换和保活状态变化立即清空并中止旧请求，迟到 PDF 不再打开窗口。后台继续以现有 ACL、精确权限、根锁及任务版本裁定，不把前端隐藏当作授权。

验收限定页面和适配器直接受影响的场景：确认退出、处理中取消、点击版本冻结、未知结果、撤权/切租户迟到响应、有界只读刷新与终态停止、响应目标/版本校验、独立重试标识及下载取消。真实浏览器覆盖确认取消、刷新后终态、人工重试新任务、桌面/390px 与当前页可访问性。没有生产 C#/SQL/契约变化，不重跑转换器全量或完整 Native 发布。

直接受影响用例补至 18 项并通过，另外覆盖取消前读取的迟到结果不得解除未知写入锁、失效身份的迟到取消响应不得重新加载数据，以及自动读取故障暂停/显式恢复。真实浏览器和类型/生产构建已通过，证据见 §8.29 末段。

### 8.29 F11/C02-R2 样板报表有效期和取消确认（2026-11-03）

快照 `foundation-f11-report-expiry-ui-20261103`。已有样板报表的服务端 24 小时有效期、下载前后复核和清理推进保持原所有权；不另建到期策略、清理 Worker 或通用 Reporting 有效期。页面展示原 `ExpiresAtUtc` 与时区，明确到期后重新生成、下载时复核；不根据浏览器时钟伪造服务端过期状态。

修正 Failed/Cancelled/Expired/DeletedCleanup 的展示：业务终态已经生效，但文件仍待清理，不能显示成继续生成或宣称已物理删除。清理中的报表沿用五秒/五分钟有界读取，清理完成停止；状态读取不刷新原主单版本。异常时间和未知状态保持待确认，不推断执行成功或过期。取消现有 Queued/Running/Ready 报表先确认点击时版本，未知结果或 409 后锁定新的写操作，仅手动刷新状态解除；切会话清空确认，不重放取消。

本批功能及 20 项直接相关组件测试已通过。真实浏览器仅选择已有“导入→附件→业务报表→打印”一条流程，增加服务端有效期展示、桌面/390px 与当前弹窗可访问性，不重跑审批或整个业务矩阵。浏览器、类型检查与生产构建已通过。C02-R2 总项继续保留通用来源到期策略等实际差集，不把样板已有有效期外推至所有任务。

§8.27–§8.29 已完成各自聚焦验收：10 项 .NET 单元、67 项前端、双库报表取消 API 2 项、真实浏览器 3 项、受控 HTTP 浏览器 5 项、运行时 OpenAPI 导出 1 项均通过；确定性 SDK/离线契约、类型、生产构建及 Reporting AOT 分析通过。预览夹具补传当前上传契约的 RequestKey；样板新增提示的 axe 在生成禁脚本打印 iframe 之前执行，原打印内容和隔离校验保留。详见[增量验证](../../verification/2026-11-03-reporting-and-preview-task-controls.md)。本轮首屏为 72 个静态块，1,816,778 / gzip 447,490 字节，仍超旧预算；既有 MySQL 352 命名差集保留。后续 Printing 改动继续独立验收，不把本批结果当作整个工作树完成。

### 8.30 C02-R3 Host 打印模板草稿配置（2026-11-03）

快照 `foundation-c02-printing-template-ui-20261103`。复用已有 Host 模板 CRUD/发布/版本目录，不增加模板引擎或租户数据权限。已有模板表、权限和维护 API 均为 Host 范围；本批页面按此约束，提供保存草稿、独立编辑、显式确认发布、每页 20 条的模板/版本摘要、按选定发布版本读取版式。示例在禁脚本 iframe 内净化展示，清楚标注占位字段尚未绑定业务数据；不把此示例当作通用租户数据绑定预览。样板的真实业务打印继续由其已有授权渲染入口拥有。

页面资格改为 Host 模板读取，创建/编辑/发布/版式查看分别检查已有精确权限。旧系统菜单和 Printing 模块容器只在原 preview 权限、精确系统路径/组件条件下修为 read，Baseline 递增后由 Migrator 执行；自定义菜单和其他权限不改。权限、会话、上下文或保活变化清空并中止读取/写入/预览；选择模板或版本后不接纳旧正文。保存不自动发布，发布使用详情中已确认的模板并发版本，历史内容不覆盖。未知结果和 409 先读后操作；编辑保留本地输入与原并发版本，必须明确确认重新载入，不能用后台取得的新版本悄悄覆盖他人修改。

补齐真实维护入口发现的输入边界：机器码为至多 128 个 ASCII 字母/数字/点/下划线/短横线，名称非空且至多 128 字符，版本说明至多 512，布局沿用 65536 限制。两库使用同一校验。草稿与发布在各提供程序的模板根锁内串行化，结果感知事务保证返回失败不提交已插入版本；本模块不跨表所有权写入、不使用 Outbox 或缓存。新锁定查询每次只读一行，不增加原操作的数据库往返；目录/版本摘要仍独立计数与分页，正文按需读取。无容量结论。

集中验收限定双库草稿保存、发布内容不可变、并发相同版本仅成功一次、输入拒绝、Host/租户与独立动作权限、旧菜单修复幂等和自定义项保留；前端确认、冲突输入、未知结果、选择/会话迟到响应、历史分页与 iframe 净化；真实页面完成新建草稿→编辑→显式发布→选版式预览，覆盖桌面/390px 和相关区域可访问性。影响 Printing/Identity 的实际路径做局部 AOT 分析，不重跑全量模块或完整 Native 发布。通用租户打印授权闭包及业务模板绑定仍按 C02-R3 实际差集开放。

本批上述子集已通过：17 个前端、1 个权限单元、双库 API 2 项、真实页面 1 项；受影响 Release 构建与 Printing/Identity AOT 分析零警告/错误，类型和生产构建通过。共享 UI 冻结后报表/文档 2 项通过，样板下拉选择竞争修正后单项通过。详见[配置说明](../../development/printing-template-configuration.md)和[验证记录](../../verification/2026-11-03-printing-template-configuration.md)。首屏 1,826,292 / gzip 448,873 字节仍超旧预算，既有命名门禁差集保留；临时缓存清理被自动审批拒绝，记录为未清理。下一增量补模板字段提示及光标插入，复用现有静态表单目录，不重复草稿/发布功能或增加执行引擎。

### 8.31 C02-R3 模板字段提示与插入（2026-11-03）

快照 `foundation-c02-printing-fields-20261103`。在现有草稿编辑器读取草稿所属固定 Schema，单独检查 `printing.form_schemas.read`，提供字段说明、稳定占位语法及光标/选区插入。仅修改本地布局，不自动保存、发布或读取业务数据；插入后保持 65536 字符上限。无目录权限时保留原草稿编辑。目录请求十秒有界、失败人工重试；关闭、撤权、换会话和 Schema 切换取消请求，迟到结果不重新显示。生成适配器校验目标 Schema、字段数量、重复键与静态占位语法。无新 API、权限、SQL 或抽象目录。

验收限定字段组件/编辑器/适配器、真实 Host 光标插入与保存发布原流程、桌面/窄屏相关区域可访问性，以及类型与生产构建。28 个前端、真实浏览器 1 项、类型与生产构建已通过；窄屏表单挤压已修复并复验，详见[字段提示增量](../../verification/2026-11-03-printing-template-configuration.md#字段提示增量)。沿用 §8.30 后端与 AOT 证据，不重复双库维护场景。首屏包体仍超原门槛，未放宽预算。下一批回到 P0 F10 的审批退回旅程。

### 8.32 F10-R1 业务审批退回轮次（2026-11-03）

快照 `foundation-f10-return-journey-20261103`。优先补 P0 样板尚缺的真实退回旅程，沿用现有 Workflow 退回、待办、因果轮次和业务终态回写，不新增工作流节点或另一套审批服务。夹具仅在该场景发布两级人工审批，并保留生产 Workflow 恢复后台循环、Outbox 和通知循环；不直接调用活动处理器。

通过真实页面提交→一级通过→二级退回一级→退回期间业务仍 Submitted 且只读→原轮次重办→二级通过→Approved 回写及通知。相同退回 HTTP 请求重投返回相同固定回执，不能重复激活；真实 Edge + SQL Server + Kestrel + 独立生产后台循环 1/1 通过，桌面/390px 与当前区域 axe 通过。复现并修复流程编号、签名提示、驳回及退回按钮的对比度，复用已有设计令牌，没有修改执行内核。集成项目定向构建零警告/错误；最终生产 Vue 构建与关联报表批集中。跨进程强杀、中断回写和处理中撤权仍按其原差集保留，不把 HTTP 重投等同进程故障证明。F10-R1 总项保持开放。

### 8.33 C02-R3 报表定义草稿与发布控制（2026-11-03）

快照 `foundation-c02-report-definition-config-20261103`。保留现有分组、定义、版本及租户发布授权；本批补 Host 配置页会话/权限/保活失效、中止在途读写与迟到响应围栏，辅助目录按独立读取权限加载，目录局部失败不阻断有权限的定义列表。只读角色不请求无权限的分组、Query Port 或数据源。

编辑读取详情版本，保留已保存的自定义参数名称和默认值，不用目录默认值覆盖；保存不发布。冲突或未知写入后保留本地输入并停止新写入，只有确认丢弃并重读草稿才能继续，列表刷新不静默提升编辑版本。发布单独读取新鲜详情并确认版本与说明；未知结果不重放，刷新后需重新确认。查询/版本仍每页 20 条摘要，不加载全部正文或执行报表。无新 API、SQL、权限或引擎。

集中验证限定页面状态/精确权限/读写迟到/并发恢复/参数保留、真实草稿 CAS 冲突→显式重新载入→保存→独立发布→版本摘要、桌面/窄屏及当前区域可访问性、类型和生产构建。17 个前端、最终类型和生产构建通过；包含新建草稿且未自动发布的最终真实闭包 1 项通过，截图已复核。发现并修复新建入口用了不存在的工具栏插槽；新建分组被冻结，目录部分失败不能解除未知写入锁。后端维护语义不变，复用原双库证据，不重跑整个 Reporting。说明及最终结果见[配置说明](../../development/reporting-template-configuration.md)与[验证记录](../../verification/2026-11-03-reporting-template-configuration.md)。下一项补其实际依赖的数据源配置入口和同样的上下文失效边界。

### 8.34 C02-R3 报表数据源配置闭包（2026-11-03）

快照 `foundation-c02-report-source-config-20261103`。修复数据源页被无效工具栏插槽丢弃的新建和搜索入口，复用现有 Host 数据源 CRUD/禁用/连接测试。页面和操作分别检查现有精确权限；上下文、会话、撤权和保活变化取消旧读写并清空列表、连接元数据及本次输入密码。

编辑读取详情并发版本，密码留空保留已保存凭据；关闭或明确重载立即清除本次输入密码，不从服务端取回密码。CAS 冲突/未知结果保留其余本地输入并锁定保存和连接测试，确认丢弃并重读后再写；列表刷新不能提升编辑版本。连接测试会写测试元数据，也不自动重放。禁用/删除确认绑定发起时上下文，取消或切会话后不向新上下文提交旧操作。无新数据库、权限、SQL、动态连接方式或放宽 TLS/SSRF 策略。

验收限定页面可见性、参数/版本/密码边界、迟到读写及确认、未知结果、真实新建→编辑留空密码→禁用、桌面/390px 与当前编辑区域可访问性、类型和生产构建。13 个前端与真实浏览器 1 项首轮通过，类型、生产构建及集成项目定向构建通过；截图已复核，见[验证记录](../../verification/2026-11-03-reporting-data-source-configuration.md)。真实厂商连接不在受控测试端口中冒充通过。下一项补报表执行页的已发布版本绑定，不能把草稿参数与后来发布的版本混用。

### 8.35 C02-R3 已发布版本执行页面（2026-11-03）

快照 `foundation-c02-report-published-execute-20261103`。执行页目前用草稿参数，却省略请求版本而执行服务端最新发布，可能在配置变更时混用不同契约；本批复用现有已发布版本 GET 和执行请求 `VersionNumber`，加载明确发布版本的参数并固定执行，显示实际版本。Host 执行及定义读取权限独立检查；不请求无权目录，不把 Host 执行迁移到 Tenant。

首轮执行冻结参数、版本和页大小，翻页沿用该快照；修改输入不暗中改变已有结果集，重新执行才采用新输入并回第一页。切定义、权限、会话、租户或保活状态取消旧请求、清空参数和结果，迟到响应失败关闭；远程目录搜索有上界，不遍历所有定义。失败可手动重试，只读执行不自动重放。无新 API、SQL、引擎或任意查询输入。

集中验证已发布参数与草稿漂移、版本/分页输入冻结、目标响应检查、权限及读写取消，真实已发布执行、桌面/390px 和当前区域可访问性，以及类型和生产构建。原模块读取/执行安全和查询预算保持，只有实际复现后端缺陷时才扩围。该子集已通过下面的聚焦验收，不再重复安排草稿/发布/执行页面功能。

真实执行复现列契约缺陷：`Columns[].ColumnKey` 保留 `EngineVersion`，全局字典命名却把 `Rows[].Values` 改成 `engineVersion`，导致成功响应显示空白单元格。扩围仅修 ReportingExecutionRow.Values 的静态类型转换器，直接写原始机器码，不改变全站 DictionaryKeyPolicy，也不由客户端猜测大小写。转换器不使用反射，读取只接受字符串/null、拒绝重复键和结构化单元格；以当前源生成上下文验证。7 项单元、实际 HTTP 列键/值对齐的真实页面 1 项、Reporting 局部 AOT 分析、运行时 OpenAPI 1 项均通过；运行时快照无变化，SDK/离线契约零漂移。16 项前端、类型、生产及受影响 .NET 构建通过，截图已复核，见[验证记录](../../verification/2026-11-03-reporting-published-execution.md)。数据库实现未改不扩成双库全量；首屏包体仍超原门槛。C02-R3 继续保留通用租户打印绑定差集。

### 8.36 F11-R1 当前授权与历史报表下载（2026-11-03）

快照 `foundation-f11-live-delivery-revocation-20261103`。补真实普通成员角色变更矩阵，不用注入字段授权替代权威角色/会话链路。范围为排队后撤销金额权限不得继续上传，已就绪结果下载必须按当前会话、当前对象/明细/金额读取权限判断；只移除创建资格不能剥夺仍合法的读取。低权限时新建的文件权限上限保持冻结，后来提升权限也不能把金额补进旧文件。

真实 SQL Server 最小复现确认：成员重新登录后合法就绪下载被旧执行会话拒绝 403。只修下载前后两次权威读取，使用当前调用会话及对象/明细/报表读取、当前金额资格与原任务金额上限；不再混入创建权限。Worker 的原会话/创建资格与冻结输入不变，不能借新登录恢复旧队列。不增加字段政策、持久表或执行器。

SQL Server/MySQL 两项均通过，覆盖真实成员邀请、部门范围、角色撤销/恢复、重新登录、文件打开期间撤权、旧结果读取与排队无上传；两库各仅产生原完整/低权限两份文件，撤权后失败队列无 FileId、无新增上传，后来提权不改变旧文件字节。样例及集成项目定向构建、样例 AOT 分析零警告/错误；既有前端和文件恢复按原范围复用，未跑全量。详见[验证记录](../../verification/2026-11-03-enterprise-request-live-delivery-authorization.md)。F11-R1 本轮差集关闭，下一项继续 F10 真实中断及处理中撤权，不重建工作流内核。

### 8.37 后台操作入口集中修复（2026-11-03）

快照 `foundation-admin-toolbar-actions-20261103`。在报表入口缺陷后以 Vue 模板 AST 核对同一工具栏契约，确认另外 9 个页面把操作放入被丢弃的默认插槽或不存在的 actions 插槽：AI 模型检索、大屏项目新建/编辑保存/发布/预览重载、K3Cloud 连接/同步创建、OAuth Provider 创建、OCR 上传和注册方式创建。此类缺陷直接阻断现有功能使用，先集中修复，再继续 F10 余项；不新增这些模块后端、不重做已交付执行服务。

调用方统一使用现行 left/right 插槽，AI 检索栏独立成行，不为旧页面增加共享兼容层；保留精确 PermissionGate、原接口和状态/版本校验。OAuth 被忽略的错误展示放回页面。真实验收扩围修复两处直接阻断：分页补标与高度测量共用原生根节点，避免 ElCard ref 引发运行时异常；大屏带项目 ID 的编辑/预览路由与壳层持续校验共用可信父菜单＋读取＋精确操作权限判断，撤权离开详情，不把详情列入自助豁免。发布按钮使用现有深绿色令牌满足对比度。

33 项聚焦前端测试、真实 Host/SQL Server 旅程 1 项、模板 AST、类型检查和生产构建通过，桌面/390px 截图已复核，页面无运行时异常；无新 API、SQL、资源或权限。首屏 72 块 1,832,049 / gzip 449,807 字节仍超过原预算，未调整门槛。详见[验收记录](../../verification/2026-11-03-admin-toolbar-actions.md)。当前入口差集关闭，外部供应商认证与实际 OCR/K3Cloud 调用不冒充通过。按用户 2026-11-03 要求在此节点统一提交整个当前工作区代码，再继续 F10 余项，不重复安排本批页面实现。

### 8.38 F10-R1 运行中审批撤权（2026-11-03）

统一提交基线 `c6c5f4ad48530f9ab6a4954b395831040cdfe1b9`，快照 `foundation-f10-live-approval-revocation-20261103`。使用真实普通成员邀请、部门范围、角色权限接口，在业务已提交、本人待办详情已读取后撤销审批权限；旧会话不能批准，业务保持 Submitted、待办未完成且不产生批准记录。恢复角色后重新登录，原待办继续办理并经生产 Outbox 循环回写原申请；重复批准不重复推进。

实现复用现有样例、Workflow/Identity 契约与 Worker 装配，默认只补 SQL Server/MySQL 两项集成矩阵，实际复现缺陷才修改运行时代码。当前进程内独立 Worker 仅用于业务授权与回写验证，不能证明 OS 进程中断恢复；真实中断余项保持未完成。不重复执行已通过的真实页面审批/退回/通知旅程。

该撤权差集已通过双库 2 项（23.1 秒）、集成项目定向构建 0 警告/错误。现行协议在角色更新后立即注销旧会话，验证为旧令牌 401、新登录缺动作权限 403；恢复角色与登录后原待办继续，重复回执不重复推进，业务终态由生产 Outbox 回写，实例和批准审计各一条。未发现需修改运行时实现的缺陷，详见[验证记录](../../verification/2026-11-03-enterprise-request-approval-revocation.md)。下一项只补真实进程中断，不重做已通过授权、页面和幂等子集。

### 8.39 F10-R1 独立 Worker 进程中断恢复（2026-11-03）

基线 `4078eab73ad83294f8443b1244edf667e2cebc50`，快照 `foundation-f10-worker-process-recovery-20261103`。复用已有 `FULLNET_APP_WORKER` 静态应用装配入口，与模板相同直接链接官方 Worker 源码，不复制整仓、不把业务样例依赖加入官方 Host。只增加测试专用进程宿主，静态装配真实样例模块，运行生产 Outbox 管线。

测试装饰器只在独立测试项目中，在真实提交处理器/终态接收点完成本地持久提交后、Outbox 确认前挂起；父测试只终止自己持有句柄的子进程，再启动新进程等待真实租约恢复，不改数据库租约时间。双库分别验证提交恢复不重复实例/待办、批准结果回写恢复不重复推进业务版本，最终原消息被确认。生产入口、事件和持久化语义不变，JIT 进程验证不冒充 Native 发布验收。

SQL Server/MySQL 两项首轮通过（63.8 秒），测试宿主及集成项目构建零警告/错误。每库三个独立进程、两次受控强制终止；启动意图与终态原消息最终确认，流程/待办/批准事实不重复，业务版本均保持 3。F10-R1 当前差集至此关闭，已有真实页面/通知与本次恢复证据共同复用，不运行全量或扩大为 Native/容量认证。详见[验证记录](../../verification/2026-11-03-enterprise-request-worker-process-recovery.md)。下一项回到 §8.3 F02-R 的明确未关闭差集。

### 8.40 F02-R 生成客户端成功链路与教程收口（2026-11-03）

基线 `b18d4a7bdb82515f9318c7f16ae821590c99328d`，快照 `foundation-f02-generated-client-closure-20261103`。现有教程已记录独立应用双库 Vue 接线、五类普通账号、自动可访问性/删除确认与同输入再生成保护的精确提交证据；这些能力不重新开发。生成客户端成功 CRUD/非空读取和冲突虽有实现，当前 R1 缺少本批明确的完整结果与教程耗时。本批在现有独立应用夹具增加有限的 generated-client 验收范围，只运行实际分发、诊断、必要生成装配、迁移及业务生成客户端读写，不借普通修复运行所有模板测试或五类浏览器矩阵。

本地临时报告改用独立系统临时目录，CI 保留既有明确上传位置；分发包只包含正式产品清单，不复制整仓供冻结或构建。必要时实际运行双库成功链路复现问题，缺陷回到模板/生成器/客户端所有者修复。R2/R3 仅按已证明的子集复用，不能将同输入再生成当作 schema 升级，不能以原仓库 API 或受控 HTTP 测试代替独立应用。实施完成后集中验证并更新 R1–R4 状态及现有教程。

首轮两库均在正式分发阶段复现清单阻断：Identity 的非种子 AOT 注册条件被误判为条件种子。清单生成改为有界枚举编译分支，只有每条路径的登记序列完全一致才接受；真正条件种子、跨分支泛型参数、缺失/孤立指令及超过预算仍拒绝。13 项新回归和 1 项既有种子清单边界通过，尚待独立应用成功链复验。聚焦入口先持编译预算完成 CLI/API/Migrator 构建，再转换到真实栈互斥阶段；后端子进程继承实际准入环境，不降低 4 GiB 系统保留量。

第二轮预检定位 OIDC 种子在持久化辅助方法中登记，迁移清单无法确认归属。把同一闭合泛型登记显式移至 Identity 的 AddMigrationServices，保持启用条件、顺序和 Scoped/去重语义，不由清单工具推测辅助调用图。四个含种子模块共九个贡献者预检通过；OIDC 启用/禁用及重复迁移注册 2 项通过，Identity JIT 构建及局部 AOT 分析零警告/错误。此处为修复检查点，独立应用双库完整成功链仍待执行，不提前关闭 R1。

第三轮实际分发继续复现迁移预设归属缺口：351、354、356、357、358 的新名称尚未登记。按实际表归属补充 Calendar、Platform、ImportExport，跨导入/报表的 354 保持 Shared 并显式要求两个模块同时就绪；它的 SQL 没有缺表守卫，不能流入 minimal/platform/saas。未改变 SQL 或放宽未知脚本拒绝。5 项迁移清单测试通过，含所有现行脚本识别、双提供程序及四个预设闭包、单模块缺席拒绝组合脚本；独立应用运行结果仍待复验。

第四轮已通过实际分发、隔离 dotnet new 安装/创建、诊断和业务产物生成，在候选模块编译复现 CS0234：未选 AI 的预设仍遗留 Agents.Runtime 导入。投影器一并移除仅服务被裁剪 AI Provider 方法的 Agents 与 OpenTelemetry 导入，不改变框架原始 AI 装配。4 项预设投影测试通过并覆盖四预设无残留；业务运行继续复验，不能以这次编译修复宣称完整 CRUD 已通过。

2026-11-04 续行：第五轮被 11-03 23:25 的系统重启中断，无完成记录，不计通过。核对 19 个后续改动源码与提交一致；仅清理已退出 PID 41960 拥有的两个资源锁，Docker 恢复。损坏临时应用清理被自动审批以“blocked by policy”拒绝，用户回复已手动清理；同级正式分发包 6,065 个受管文件完整。入口允许复用现有包，但先核对框架源码提交差异、外层创建工具原文和包内摘要，不另堆框架分发副本。

第六轮模块首次接入及模块构建通过，重复编译遇到 CLR/MSBuild 子节点异常；另一库的清理 EPERM 曾掩盖原始错误。清理逐项执行、聚合保留原始与清理错误，Windows 文件占用有限重试，仍存活的 API 保留工作区；6 项直接回归通过。接下来先用两核编译预算定位 SQL Server 路径，保留分析器和原验收标准，再补 MySQL，不将环境异常写成业务通过。

第七轮生成与重复接入通过，API 直接构建发生 MSBuild 内部空引用异常，日志出现至少 26 个工作节点，超出申请的两核预算。验收入口为直接 `dotnet build` 显式传入 `-maxcpucount`，3 项参数边界回归通过；不关闭分析器、不改变业务断言。CLR/MSBuild 异常的根因尚未确认，后续实际运行结果单独登记。

第八轮仍在候选重复编译遇到 CLR `0x80131506`，Windows WHEA 日志同期报告处理器 APIC 16 的已更正 `Internal parity error`（00:14:45）；这不足以直接认定编译器异常的根因。出错 Validation 项目在主工作区两节点独立 Rebuild 通过，零警告/错误。停止无新诊断依据的整链重复，F02-R1/R4 保持待验；保留最小失败报告，继续 F03 的验证码重发/失败竞争实际缺陷，不阻塞独立开发。

### 8.41 F03-R1 验证码重发与旧投递失败竞争（2026-11-04）

基线 `d1df0c9754a82fb158d8976ca5707392927db295`，快照 `f03-challenge-resend-race`。账号挑战先落库再投递，旧请求的失败回调原先按用途/邮箱撤销全部有效挑战，会误伤已经成功重发的新验证码。改为仅撤销该请求的 ChallengeId，并保留重发时按用途/邮箱替换旧码的规则；不改变渠道和公开响应契约。真实双库用例覆盖注册、找回密码及邀请三种用途的延迟失败、新码一次消费和无重发时失败挑战撤销。聚焦结果见下；F03-R1 的受控邮件渠道和送达未知等其他差集仍开放。

同批补齐注册/找回密码页面：发送加载与防重入、失败提示和显式重发、邮箱变化后清除旧挑战并拒绝旧响应、提交资格及中英文可访问名称；公开认证请求使用当前语言，表单复用登录页现有布局。集成构建零警告/错误，双库 2 项通过（每库三用途，92.6 秒），前端 11 项与类型检查通过；Edge 1440×900 / 390×900 的两个页面共 4 项通过（17.0 秒），含受控 503、重发、键盘提交、无横向溢出和表单 axe 零违规。Identity 局部 AOT 分析零警告/错误，未发布原生宿主。本节缺陷与页面增量关闭；浏览器 HTTP 为受控响应，不冒充真实 SMTP 或 F04/F05 全旅程。F03-R1 其余边界继续开放，不重排已验重发竞争。

### 8.42 F04/F05-R1 开放注册中的邀请目标与原子绑定（2026-11-04）

基线 `8a186c123a21a19a1cf1443d640135dae910c625`，快照 `f04-invitation-open-registration`。开放模式原先忽略邀请目标并按普通注册验证码核销，与邀请页面的用途不一致；携带邀请时统一先校验完整凭据，以有效邀请确定目标租户和用途，保留事务内快照复核。邀请参数不完整时拒绝回落普通路径；账号创建后的邀请绑定失败返回失败结果，使本地账号、邮箱、验证码及邀请核销一并回滚。同步修正 RegistrationWayId 的说明，普通开放注册仍须显式路径。

聚焦单元覆盖开放模式成功、目标变更/撤销、不完整凭据及两模式绑定失败；双库 HTTP 覆盖开放/仅邀请模式的邮件挑战→注册→待入驻绑定与重复提交拒绝。只使用本次场景，不重跑全部 Identity 或账号/成员全旅程；F04/F05 的激活、已有账号 MFA 入驻及其他竞争差集保留。

聚焦结果：16 项单元通过；SQL Server/MySQL × 开放/仅邀请共 4 项真实 HTTP 通过（42.1 秒），核对用户租户、邀请绑定、待入驻状态、凭据核销及重放拒绝。集成构建及 Identity 局部 AOT 分析均零警告/错误，本节增量关闭；未覆盖范围仍按上文保留。

### 8.43 F03/F04 注册策略三态配置入口（2026-11-04）

后台仍为布尔开关，无法选择服务端已支持的仅邀请模式；共享客户端也未声明或验证 RegistrationMode。本批补齐三态显示、显式版本保存、冲突/结果未知后重新读取、只读/无策略读取权限及错误状态，列表加载不被策略读取失败阻塞。共享客户端严格核对 0/1/2 与公开注册标记一致，更新请求按服务端已有可选字段表达显式模式；不改服务端协议或数据库。复用 §8.42 的模式链路证据，不重新开发邀请内核。

聚焦结果：客户端契约 9 项、页面 6 项通过，两个项目的类型检查通过；Edge 桌面 1440×1000、移动 390×1000 共 2 项通过（12.0 秒），覆盖三态切换、409 后冻结写入、重新读取版本再保存、卡片无横向溢出及 axe 零违规。首轮使用组件内部输入作为点击目标被占位层拦截，改用实际控件外框后完成，没有强制点击或放松业务断言。本节增量关闭；旧真实栈用例同步为三态控件定位，未额外执行其整套矩阵。

### 8.44 F04/F05 开放注册入口链接（2026-11-04）

基线 `0e9ff19ae130bc2fc65ca2b6f7eb955d16d988f9`，快照 `f04-registration-entry-links`。后台已能配置开放模式，但普通注册页未读取注册路径，无法提交服务端要求的 RegistrationWayId。本批为有注册方式读取权限的用户提供启用方式的只读链接及复制/手动复制入口；保留站点来源和部署路径，不带内部 API 地址、角色或租户覆写参数。注册页只接受单一有效非空 GUID 路径；缺失、非法或未核实邀请时提示并阻止发送，邀请核实后仍以邀请路径为准。

后端继续权威核对方式、租户、角色/部门及策略，本批不改变 API 或数据库。聚焦验证涵盖链接到公开表单提交、路径输入失败关闭、复制失败回退与直接受影响的验证码/策略页面；真实邮件及完整入驻仍不由受控 HTTP 推导。聚焦结果见下。

聚焦结果：19 项相关单元通过，4 项未改动的找回密码测试按筛选排除，不计通过；Vue 类型检查通过。Edge 桌面/移动 1440/390×1000 共 4 项通过（23.4 秒），含原注册发送/重发、读取者生成链接、手动选择及受控剪贴板复制、打开公开表单并提交正确路径，链接弹窗 axe 零违规且无横向溢出。首轮测试在行尚未加载时误选更多操作入口，等待目标行可见后完成。本节增量关闭；显式新链接不复用旧邀请、不完整邀请拒绝回落也已覆盖。下一项核对待入驻到激活的实际服务端接续，不提前关闭 F04/F05 总项。

### 8.45 F04/F05 无账号邀请的注册与成员激活接续（2026-11-04）

基线 `1c5aced74470522b206cf1b6e7d321611af7cf59`，快照 `f05-invited-account-onboarding`。现行注册邀请只写到 AccountCreatedPendingOnboarding，无 Activated 消费者；正式成员邀请另有成熟的 Pending 成员/席位恢复。依照 §8.5、总体规格 §24.1 和既有席位编排，采用以下范围连续实施，不复制成员事实或配额内核：

1. 正式成员邀请可显式携带注册方式；成员邀请为签发根，注册邀请作为同一 UUID 的账号创建资格扩展，两者在 Identity 本地事务签发并共同撤销，凭据仅在一次响应边界返回。方式须匹配当前受信租户且启用；不因邮箱文本相同自动绑定已有账号。
2. 受邀注册创建统一 Host 账号，核销邮箱证明后原子绑定邀请目标用户；新账号在成员激活前不获得企业授权。注册后经原登录进入本人企业上下文，再显式接受同一邀请；已有账号始终走其原登录/MFA 并证明邀请令牌，不借邮箱文本绑定或重置账号。
3. 按 ID 接受及本人列表仅处理已绑定本人的邀请；未绑定邀请走认证后的令牌接受。补齐已接受但成员待激活的可见状态，防止以未验证邮箱枚举或接受其他人的邀请。
4. 继续使用现有 TenantSeatOperation 的持久化阶段、幂等和 Worker 恢复；成员激活与注册资格终态在 Identity 同一事务收口，撤销后的原意图不能继续激活。核对签发者授权失效边界，不在本地事务调用 Tenancy/Organization。
5. Vue 串联签发含注册方式邀请、无账号验证/注册、登录后的本人邀请接受和待恢复反馈；已有账号保留安全登录接续。注册方式的完整自定义业务角色/部门自动分配另保留差集，本批成员权限以正式邀请的受控 MemberRole 为准，不宣称完整 F04/F05 已关闭。
6. 统一账号仅默认获得本人导航与上下文自助能力；显式平台角色授权与本人活动成员范围分别读取。可用企业列表、Legacy/OIDC 上下文切换、身份中心租户同意及发行均核对权威范围；成员方式发行持锁至提交，不因共享自助权限扩大为平台目录或其他企业准入。

聚焦结果：48 项后端单元通过；SQL Server/MySQL × 开放/仅邀请共 4 项真实数据库与 HTTP 用例通过（28.2 秒），覆盖正式签发、错误方式整笔回滚、父子撤销、账号绑定与重放拒绝、未绑定邀请不因邮箱相同而可见/可按 ID 接受、签发者权限及成员归属撤销、Legacy 登录和身份中心在激活前拒绝/激活后准入、成员及注册资格同事务激活。邮箱投递使用受控 Port，身份中心验证到真实登录服务与授权主体发行，未宣称外部邮件或完整 SSO 浏览器旅程。

前端最终聚焦 16 项通过，9 项无关用例按筛选排除；新增客户端邀请状态契约 5 项通过，Vue 与共享客户端类型检查通过。Edge 1440/390×1000 共 2 项通过（27.9 秒），覆盖生成邀请链接、URL 凭据剥离、已有账号接续、显式接受、Pending 反馈及重试、清除凭据，邀请卡片 axe 零违规；核对截图后修复手机卡片压缩与当前上下文横向裁切，并加入实际输入区可见性检查。受限标签页存储保留当前应用内存接续，写入/删除失败不会带回旧邀请。

集成构建及 Identity、Tenancy 局部 AOT 分析均零警告/错误；未发布原生宿主，不变更容量状态。SQL Server 首轮发现跨邮箱列排序规则冲突，改由同事务签发的 UUID/租户关联，邮箱在注册绑定时核对，双库已复验。DI 聚焦检查同步补齐既有 MCP 专用认证方案断言。本节增量关闭，F04 自定义业务角色/部门完整分配等总项差集继续保留。命名门禁当前 36/37，通过项不掩盖既有 MySQL CDC 心跳主键未命名缺口，下一批处理该单点。

### 8.46 CDC 心跳主键命名门禁修正（2026-11-04）

基线 `58667150a79fa303ed856a4b657061979e1c88ed`，快照 `cdc-heartbeat-key-name`。MySQL 352 心跳建表的主键补齐与 SQL Server 对应脚本一致的 `PK_fn_messaging_cdc_heartbeat` 显式名称；不改变列、约束条件或心跳数据逻辑。命名及 UUID 存储结构门禁 37 项全部通过，§8.45 记录的该单点已关闭。本批为命名声明修正，未追加无关构建或数据库全量验证；不据此更新 CDC/Kafka 能力状态。

### 8.47 F02 内部候选编译资源预算（2026-11-04）

基线 `e2333445b31866876cc6f7a67fa68ce67b85db98`，快照 `f02-candidate-build-budget`。外层 Harness 已限制直接构建，但模块、Composition 及整链接入的内部 dotnet 子进程没有显式 MSBuild 节点限制，不能由外层 DOTNET_PROCESSOR_COUNT 推导节点已受限。统一为内部候选构建设置 1～2 个节点、相同托管处理器预算、禁用共享编译及节点复用；保留原候选投影、隔离输出、编译门禁、诊断、取消和超时，不影响 SDK 探测命令。

3 项聚焦验证通过（35.9 秒），包含预算边界及真实合法候选编译、非法候选拒绝且业务文件未写入；构建与 CodeGeneration 局部 AOT 分析零警告/错误。本实现增量已验收，下一步使用本次固定提交重新构建正式分发包，复验 F02-R1/R4 的生成客户端实际 HTTP 链；旧包含更早身份实现，不修改其受管摘要以绕过过期检查。CLR 异常和 WHEA 的因果关系仍未确认，不提前宣称整链已恢复。

### 8.48 F02-R1/R4 生成客户端真实双库链收口（2026-11-04）

正式包固定在 `7d2b8880aef29b3e902da1ff3d86310efd8b351c`，受管文件 6078 个。SQL Server 第九轮、MySQL 第十轮均完成独立 `Demo/acme/minimal/catalog/product` 应用的安装创建、诊断、生成/接入、构建、首次业务迁移和 API 启动；两库的生成客户端写入流程各 10 次请求通过，含创建、详情/列表、更新、两次旧版本 409、删除及删除后 404，另有非空详情/列表回调各 1 次，业务写入/删除计数均为 1。业务阶段耗时分别 363.9 / 383.0 秒；Node 的父/子测试不重复计为数据库业务场景。

原始结果位于本机系统临时目录的 `fullnet-f02-generated-client-evidence-6f7bce1d16b340ed948c1afe199b9d0d/{ninth-sqlserver/sqlserver,tenth-mysql/mysql}`，`focused-result.json` 均 completed=true。命令、参数、修复方式、硬件/预算与实际范围已写回原教程。MySQL 开始并完成包摘要校验及独立应用创建后，主工作区开始不参与该固定包运行的 F03 邮件增量；本证据始终归属上述包提交，不外推为后续源码全量认证。F02-R1/R4 关闭，R2/R3 的当前页面及再生成差集保持独立，未运行浏览器全矩阵或原生发布，不改变容量状态。

### 8.49 F03 账号邮件语言与受控 SMTP 适配链（2026-11-04）

基线 `7d2b8880aef29b3e902da1ff3d86310efd8b351c`，快照 `f03-localized-smtp-challenges`。复用现有渠道发布配置、SMTP 适配器及挑战内核：把协商的规范语言传入进程内意图，为注册、邀请验证及找回密码提供中英文内容，保留 UTC 截止时刻和独立用途；诊断文本不输出邮箱或验证码。投递 Port 返回 false 同样按未发送处理，撤销本次挑战。

24 项聚焦单元通过，覆盖三用途的双语内容、敏感诊断文本、false 投递及直接受影响的注册事务；双库 2 项真实 HTTP/发布配置/SMTP 适配器用例通过（59.2 秒），覆盖配置缺失、中文三用途、Accept-Language 英文请求、未知结果撤销、显式重发、一次性消费和过期。仅替换网络传输，不向外部邮箱发信；未将适配链结果称为真实 SMTP TLS、厂商认证或送达率证明。初轮单元替身不能代理模块 internal 接口，改为手写受控替身后通过，未扩大生产可见性。

集成构建及 Identity、Notifications 局部 AOT 分析均零警告/错误。本批增量关闭；未改变数据库结构、HTTP DTO 或前端页面，未追加无关浏览器/全量测试或原生发布。F03-R1 只保留真实渠道证据差集。

### 8.50 HTTP 代码生成业务写入策略（2026-11-04）

基线 `37d0829202447d4b306a5b00f0e4a5c6634e15f3`，快照 `codegen-mutation-policy-workbench`。HTTP 能力契约与规范化映射接入已有 core `hasMutationPolicy`，Vue 工作台增加显式开关，新示例使用显式能力及审计列。启用时生成必需的模块内策略接口和创建/更新/删除调用；需要 Version，修改会撤销旧应用计划。缺省 false 不进入规范 JSON，保留未启用模型的摘要。

后端聚焦单元 6 项、双库真实 HTTP 2 项（14.8 秒）通过；SQL Server/MySQL 的实际 OpenAPI 导出各 1 项通过，规范文档一致，客户端重新生成后零漂移。前端页面 15 项、客户端预览契约 7 项及两处类型检查通过，命名门禁 37 项通过。集成构建和 CodeGeneration 局部 AOT 分析零警告/错误。

Edge 1440/390×1000 共 2 项通过（10.8 秒），验证空格切换、请求值、无版本禁用、无横向溢出及开关区域 axe 零违规；已查看两种宽度截图。首轮发现组件空格切换后原生勾选与 ARIA 不一致，显式处理空格默认行为；截图检查补齐局部开关样式后复验通过。本批增量关闭，未宣称完整 Native AOT 发布或容量验证。

### 8.51 F02 分发包前端共享约束修复（2026-11-04，验收中）

固定 `37d0829` 包的 SQL Server 第十一轮、MySQL 第十二轮独立应用验收均到真实 Vue 构建，发现 `contracts/workflow/gateway-limits.json` 未纳入分发，类型检查 TS2307；本轮不关闭 F02-R2/R3。补齐源码包、模板根骨架、copyOnly 与创建器哈希检查，并新增正式创建应用后的字节/摘要用例。打包用例要求干净的固定提交，先提交本批全部代码后执行；修复后独立页面证据仍待运行，既有失败记录保留。

`ec4105674` 的正式包通过聚焦创建/摘要用例（1 项，19.7 秒）；随后两库第十三/十四轮均越过类型检查，到生产 Vite/VForm 阶段发现另缺 `vform3-runtime-release.json`。沿源码读取路径核对后补齐该发布清单，并把同一聚焦打包用例扩展为新应用自身依赖安装和生产构建，保留运行时摘要门禁。新版待干净提交执行；不能用文件存在或前一轮类型检查关闭 F02。

### 8.52 F04 受邀成员的业务角色与组织分配（功能验证完成，架构差集保留）

基线 `ec410567461d189dc5585446494e706e1f6cda55`，快照 `registration-way-member-provisioning`。复用现有邀请、成员和席位阶段；激活时使用当下有效的注册方式配置，Identity 在本地激活事务内检查租户角色并授予，同时以 Outbox 固定部门/职位分配意图。已有账号及普通邀请不重建账号，不调整 Host 角色。

跨模块边界：Identity 只写自己的成员、角色链接和 Outbox；Organization 消费方只写本地持久分配意图及组织关系。事件为 Identity 拥有的入驻事实，MemoryPack 具体 DTO；复用现有事件传递入口，以操作 UUID 在本地意图事务去重，不自动新增 CDC/Kafka 订阅。Organization 后台在事务外通过活动成员目录复核原激活版本，在本地事务检查机构/职位并创建缺失的普通隶属，不覆盖人工已有或已停用关系，也不移动主部门/主职位。成员/角色同步激活，组织关系最终一致，目标在 Worker 正常运行下约 5 秒内处理；失败保留错误与重试时间，不将目标当作容量实测。

验证限定为本批角色原子授予、错误/停用/其他租户配置拒绝、组织分配、重复投递、撤销及恢复的双库场景；复用现有页面查看分配结果，补直接边界单元和局部 AOT，不运行全量 Identity/Organization。F04/F05 的原账号 MFA 及其他进程中断差集仍独立保留。

最终 20 项单元、6 项双库用例通过（双库复验 29.9 秒）；双库为开放/仅邀请各 2 库的 4 项真实注册链路，加新迁移的 2 项未记账恢复/重复部署。覆盖业务角色只在租户生效、真实 Outbox 载荷、组织本地持久意图与 Worker 批处理、重复投递、原激活版本失效、人工停用保留、引用失效后持久重试及再次接续；配置在授予时变化返回版本冲突，成员/角色先回滚再走席位补偿。Identity/Organization 局部 AOT 分析、集成构建零警告/错误，命名门禁 37 项通过。未运行全量测试、外部邮件、完整 Native 发布或真实 Worker 进程终止场景；已有账号完整 MFA 旅程继续保留差集。

架构聚焦 4 项中 3 项通过，覆盖表归属、Identity 契约 hub 和具体租户存储依赖；全局上下文写入清单仍失败。对固定基线逐文件比较：原有 27 处漏登记，本批未引入新缺口；补齐相关两个席位文件后剩 25 处，新增组织 Worker 已精确登记。该全局门禁未通过，后续按模块审查可信来源、恢复清理及事务边界后逐项登记，不能自动复制扫描结果放行；不扩写其他模块实现或声称其已获审查。

### 8.53 F02 分发运行时依赖确定性（2026-11-04，分发验收中）

基线 `849586219b5b021eff71c8f23c76a8022e429123`，快照 `f02-packaged-frontend-release`。新增发布文件后，实际新应用生产构建进入 VForm 摘要校验，暴露运行时资产差异。构建模块图确认 Element Plus 2.14.3 的五处工具入口使用未声明的 `@vue/shared`：原仓库取到 3.4.21，新应用只有 3.5.42。以精确 pnpm 包扩展补齐依赖，并冻结锁文件；未改动第三方源码。

修正后从原仓库构建的全部运行时资产，与此前新应用真实产生的资产逐字节一致。审查差异为一份 JS 中重复的旧共享工具代码及对应许可、两个引用文件名和 HTML 引用；通过原发布工具重新生成运行时清单与前端摘要常量，桥接/编译/绑定协议版本不变。4 项依赖解析、摘要失配拒绝与许可稳定性用例通过；受控 HTTP 的真实 VForm 设计器草稿回显/保存及待办输入/意见/签名共 2 项浏览器通过（17.5 秒），管理端及设计器类型检查通过。

分发测试同时修正自身的换行预期：Git archive 在 Windows 可导出 CRLF，源提交对照只统一换行，冻结分发包的摘要及模板/应用副本仍按原始字节精确核对。首次新断言因换行失败后，保留真实生产校验继续发现上述依赖缺口。源码包实导出聚焦用例已通过；新版完整独立应用生产构建要求先固定干净提交，随后执行，不提前关闭 F02-R2/R3。

`ee533e13e9e7334522ad07b31fcb3b758ba800c3` 干净提交的正式分发复验 2 项通过（65.8 秒）：包含真实 dotnet new 创建、冻结依赖安装、管理端类型检查、Vue/VForm 生产构建及源码包摘要核验。该提交的独立双库完整页面差集仍未通过：SQL Server 第十五轮完成生产构建后因缺少 Playwright Chromium 未进入页面；安装匹配依赖后，MySQL 第十六轮进入登录/企业切换，浏览器目标崩溃。保留失败记录，不计为功能通过，后续使用新干净提交的正式包重跑。结果归属固定包，不随主工作区后续开发外推。

### 8.54 F04 开放注册后的本人入驻接续（2026-11-04）

基线 `ee533e13e9e7334522ad07b31fcb3b758ba800c3`，快照 `public-registration-membership-continuation`。当前开放方式只创建账号，没有成员/席位意图，不能实际使用配置的角色与机构；本批复用 §8.52 及既有席位阶段补齐接续。

1. 新的开放方式注册创建统一 Host 账号；账号、邮箱证明消费、Pending 成员及带注册方式来源的席位意图在 Identity 同一本地事务提交。旧的管理账号域保持其原有能力。增加双库可重放迁移，为席位意图保存可空注册方式 UUID；邀请方式仍使用已有资格关联。
2. Worker 继续按已有阶段推进。本人可查询自己的注册入驻进度，在 API 单独启动或暂时失败时显式继续；额度不足后的重试复用原账号/成员并增加 Attempt，原管理员移除或改动过的成员不能借旧意图恢复。
3. 本人读取与继续使用独立精确权限和可信用户 ID，不接收目标租户/账号；本地事务只操作 Identity 表，租户权威和配额检查保持事务外。注册方式和业务角色在激活前重新校验，组织分配沿用本地 Outbox 意图。
4. TenantContext 展示本人处理状态和继续/重试入口；注册后登录接续到该页。公开 DTO 不返回凭据、请求摘要或数据库租约。按实际处理状态有界刷新，成功后刷新可用企业。
5. 同批完成 HTTP/OpenAPI/客户端生成、两种语言、Native AOT 行映射及双库迁移恢复。验证限定为开放注册的账号/意图原子性、本人隔离、版本冲突、额度失败重试及管理员变更拒绝、页面成功/失败/处理中状态；不重跑既有邀请、支付或外部邮件全矩阵。

本批验收：42 项针对性后端单元通过，包含两处 DI 注册及成员/工作项 CAS 失败回滚；4 项双库场景通过（86.2 秒），覆盖开放注册、本人隔离、陈旧版本、原账号/成员复用、Attempt 递增、真实业务角色授予、管理员变更拒绝及迁移未记账重入/索引补齐。MySQL 迁移改用固定 SQL 恢复过程，未新增动态 SQL 债务。Identity 局部 AOT 分析与集成构建零警告/错误，37 项命名门禁通过。

Vue 24 项、共享进度契约 8 项通过；注册成功接续、精确读取/继续权限、未完成不报成功、上下文变化丢弃旧响应及写入 401 不重放已覆盖。管理端和共享客户端类型检查通过；Edge 1440/390 两项真实交互及 axe 零违规，修复窄屏 flex 压缩遮挡和说明/标签对比度，并查看截图。浏览器使用受控 HTTP，不代替双库服务端结果。两库各一次真实 OpenAPI 导出一致，生成客户端与离线快照零漂移；新增适配器契约/消费者登记专项两项通过。

全局 Vue 契约/调用点门禁发现既有目录缺口，未自动登记其他模块；本批适配器已补齐，其他缺口保留。上下文写入清单逐文件比较仍为原有 25 处缺口，新增两个文件已按来源及恢复范围精确登记，未引入新缺口。未执行全量测试、完整 Native 发布、外部邮件或额外 Worker 进程故障矩阵。下一批继续 F02-R2/R3 的正式包页面差集；已有账号完整 MFA 旅程仍独立保留。

### 8.55 F02 新应用浏览器差集继续（实施中）

基线 `1d0113d680d9787565a51336b56a769155aab7bb`，快照 `f02-created-app-browser-stability`。第十五/十六轮均已越过独立应用真实生产构建，但本地 Chromium 缺失或目标崩溃，仍不关闭 R2/R3。本地 Windows 的模板浏览器控制器改为项目既有系统 Edge，CI/Linux 保留锁定 Chromium；结果显式记录通道、版本及目标崩溃标志，不削弱任何业务、键盘或 axe 断言。下一步固定干净提交，导出一次正式包，再依次运行两库 complete 差集；主工作区可继续不影响该包的功能开发。

### 8.56 F06 企业生命周期操作台（2026-11-04）

基线 `4516c5866abbe714254239099b6b95e30593a5b9`，快照 `f06-tenant-lifecycle-console`。交付 Host 企业页面的权威状态读取、暂停、恢复与关闭；所有者交接和完整消费者撤权矩阵保留原差集，不重复建设开通或成员流程。

1. 复用 Tenancy 现有状态、商业恢复门槛与 CAS 契约，补齐精确生命周期读取端点。页面显式确认，冲突刷新后由用户重试，不自动重放写入。
2. 修复状态边界：Closed/Closing 不得经旧启用入口恢复；暂停与关闭均保留最后一家活动企业；关闭已停用企业不错误阻止其他唯一活动企业。暂停/关闭/旧禁用共享 Tenancy 本地事务门闩，覆盖并发跨入口停止。
3. 门闩由双库可重放迁移提供，失败结果回滚本地写入。更新生成客户端、权限、两种语言、模块注册和真实受影响文档。
4. 集中验证状态转换、版本冲突、终态拒绝、并发最后一名保护及迁移重入的双库场景，页面验证读取者/操作者、取消、冲突、桌面/窄屏与键盘。只执行这些变化及必要直接边界，不运行全量测试。

本批结果：28 项后端定向单元通过；双库 4 个唯一场景通过，包含迁移两项半完成/缺行/重入保留修订，以及两项 HTTP 权限、CAS、暂停/恢复、终态拒绝、暂停与旧禁用并发只允许停止一家、关闭已停用企业仍保留其他唯一活动企业。首轮夹具错误依赖第二个 Overlay 活动企业，改为显式开通独立企业；随后真实读取暴露已有 Read 常量未入权限目录的 500，已补齐 Host 精确登记和回归断言，双库功能复验 11.7 秒通过。

Vue 12 项与类型检查通过，4 项 Edge 1440/390 的只读/冲突/取消/编码关闭/键盘/Escape/axe 通过（24.5 秒），已查看截图；只读路径等待淡入结束后量测真实对比度。两库各一次真实 OpenAPI 导出一致，生成客户端/离线快照零漂移，新适配器契约与调用点专项通过；Tenancy 局部 AOT 分析、集成构建零警告/错误，命名 37 项通过。未执行全量测试、完整 Native 发布或完整 F06 消费者故障矩阵。

F02 第十七/十八轮的固定 `4516c5866` 包均完成独立前端生产构建，但普通 Host 账号的精确权限断言漏同步 §8.54 新增的两项本人权限，尚未完成浏览器链。已同步夹具并保持业务权限的精确断言，相关普通账号五种权限及拒绝/脱敏专项 75 项通过；待分支合并后使用最终干净来源继续真实双库差集，不提前关闭 R2/R3。

### 8.57 全部分支汇入 main（执行中）

项目所有者于 2026-11-04 要求下一次提交后合并所有分支，成功后清理分支，以后直接在 main 开发。本批提交位于 `codex/f06-tenant-lifecycle-console`；审查分支 `77af180c8` 和远端验收分支 20260926 已可从原 main 到达。远端 `origin/codex/foundation-acceptance-20261003` 的固定头 `9473603d1` 仍有独立代码：与原 main 的共同祖先为 `1760610dd`，来源改变 1340 个文件，初步预览 313 个冲突。

先提交本批已验功能，切回 main 合并本批；再对独立远端分支进行真实三方合并，保留两边有效实现并处理公共契约/源生成/权限/双库/依赖及页面冲突，生成物从合并后的权威输入重建。用户最新规则优先，不恢复旧分支的冻结/全量测试流程；验证只覆盖冲突及直接受影响边界。所有来源提交可从最终 main 到达后再清理已合并分支和检查工作区；后续不再创建任务分支或工作树。当前已将长期约束同步 AGENTS 与 Git 规则。
