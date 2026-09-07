# Full-project Review Fixes Implementation Plan

> 执行方式：当前会话逐项实现；遵循根 AGENTS 的选择性工作流，不自动创建任务、worktree 或提交。

**执行状态（2026-09-07）：** 用户要求当前 AI 生成所有权问题收尾后更新处理记录并停止；不再启动剩余问题，未勾选项不视为完成。

**Goal:** 修复 2026-09-07 全项目审查的 18 组问题，并恢复本地可执行的质量门禁。
**Architecture:** 遵守既定 ADR-0002/0008/0009；保持模块化单体、模块所有权、租户隔离、短事务和双库约束。用户已明确授权执行修复；迁移、合同或运行语义的具体变化在本计划与交付中记录。
**Tech Stack:** .NET 10、Dapper、MSTest、SQL Server/MySQL、Vue、Vitest、Node 治理测试。
**Baseline:** main，1af03bc0b2cf5ee10b47129ffd59ebc797e73272；snapshot full-project-review-fixes-20260907。
**输入报告:** docs/verification/2026-09-07-full-project-architecture-code-review.md。

## 约束与验证

- 每项先复现失败，再最小修复；沿用本次审查的新鲜失败，新增行为使用回归测试。
- 不关闭 SQL 守卫、不抹去授权、不用 any/NoWarn/宽泛豁免换绿。
- 所有修改的后端声明补中文 summary/param，数据库变更成对维护。
- 重型环境验证遵循 GitHub Actions 优先规则；未执行项如实报告，不标记 Verified。
- 本次不自动提交、推送或改写已应用的迁移历史。

## 执行清单

- [x] R01：NotificationsModule 的钉钉服务与 Endpoint 装配闭包一致。通过默认配置、开关组合及实际 Endpoint 元数据构建测试。
- [x] R04/R06：PaymentOrderManagementService、PaymentRefundManagementService 编号使用完整唯一标识；PaymentWeChatNotifyService 绑定商户/订单及单向终态。增加固定时钟不同 UUID 编号不相等、合法签名跨商户回调不更新订单的测试。
- [x] R07：Printing 布局在预览消费端进入受限隔离文档或完整白名单净化；测试攻击布局不能进入管理端 DOM，保留合法打印样式。
- [ ] R08：通知凭据引用限制、失败计数提交、验证码发送事务、投递有效租约及历史模板重放；逐一建立实际服务回归。
- [ ] R02/R03：ImportExport/Reporting 的 SQL 绑定与租户谓词一致，Files 合同的所有者作用域明确；用真实 ScopeGuard 与 A/B 租户测试验证。
- [ ] R05：Payments/K3Cloud/Ocr 外部调用移出本地事务，保存稳定意图和结果；测试外部成功后本地失败、未知状态与重试所有权。
- [ ] R09/R10/R11/R16：AI 的租户读取、配额原子预留与结算、generation 所有权、取消和响应预算；测试并发争抢、旧生成清理及有界缓冲。
- [ ] R12：Mqtt 持久化消息内容摘要，比较完整幂等语义；同长度不同正文必须冲突。
- [ ] R13：按迁移应用边界修正 UUID 存储，提供双库迁移/升级回归，不盲改历史。
- [ ] R14：修复静态 AOT 参数/物化/JSON 闭包，运行 pnpm test:aot:analyzers 与 api-native-aot 架构选择器。
- [ ] R15：修复 workflow-form-attachments 的上传参数与 requestBlob 调用，恢复 Vue/共享客户端合同、类型和测试夹具。
- [ ] R17：导入/报表任务持久化执行所有权、期限和恢复状态；测试崩溃后重领与重复完成。
- [ ] R18：修复模块声明、提供程序层边界、治理与生成目录漂移；同步现有架构事实，不降低门禁。
- [ ] 运行影响集规划、Unit/Architecture、Vue、OpenAPI、命名及必要治理；检查 git diff --check 和工作区状态，进行资金/安全变更复核并记录剩余验证。

## 命令

聚焦 .NET：使用 tests/Full.NET.UnitTests 项目的 MSTest --filter 对应命名空间/类；命令与红绿结果保留在 artifacts/reviews/full-project-fixes-20260907。
前端：pnpm --filter @fullnet/admin exec vitest run <受影响测试>；pnpm --filter @fullnet/admin build。
影响集：pnpm test:integration:affected:plan -- --snapshot full-project-review-fixes-20260907 --phase inner。
全局已受影响的本地门禁：pnpm test:dotnet:architecture、pnpm test:dotnet:unit、pnpm test:aot:analyzers、pnpm test:naming、pnpm test:openapi、pnpm test:governance。

## 进行中的验证记录

- R01：EndpointAuthorizationTests 28/28；R04/R06：支付单元测试 29/29，包含真实 RSA/AES-GCM 回调及失败 HTTP 500。
- R07：打印预览 DOMPurify 攻击回归 2/2；依赖已登记许可。依赖审计仍有既存 uni-app/Vite 高危，不将其标为全绿。
- R14：AOT 分析从 214 个错误恢复到 0；后续变更后仍须重跑，Linux 原生发布未执行。
- R15：Vue 生产构建成功，全量 213 文件/722 测试通过；后加请求传输测试仍待补跑。
- R08：通知子集 143/143；重放修复及独立复核发现的验证码并发消费、内部超时预算正在补回归。不得将 R08 整组标为完成。
- 其他未勾选发现仍待实施；本地完整治理及最终影响集未收敛。原始审查报告保留为历史证据。

- R08 后续：历史模板重放、挑战原子消费和内部超时预算补齐，通知 147/147；秘密引用登记方式已更新既有通知 Spec。
- R12：完整正文摘要与 SQL/AOT 映射已补齐；MQTT 9/9，新增 201 双库前向迁移，旧无摘要记录失败关闭。迁移数据库运行尚未执行。
- R02：17 条任务 SQL 真实守卫红 17 → 绿 17；Worker 改为租户目录发现、活动上下文建立、租户内领取。领取/恢复的真实双库验证仍待补齐。
- R03：202 新增 Files 租户资源所有权表与合同，导入/报表调用点完成替换；已加基于所属模块引用端口的旧文件兼容读取。文件上传/归属/事务测试及任务 SQL 共 20/20，后补旧文件与读取事务测试仍待运行。R17 需继续收敛 ready 孤儿、pending 对账和任务崩溃窗口。
- 集成测试项目本地编译成功，0 警告/0 错误（后续 Files/任务变更后需重跑）；新增前端 JSON 传输测试 2/2。

- R04/R06 追加：回调凭据插入唯一冲突由真实事务回滚后返回失败重试，不再吞掉 DataCommandException 并盲目确认成功；支付与地区 39/39。
- R09：会话创建与生成读取独立的租户/Host 可用模型 SQL，租户配额读写绑定 CurrentTenantId；真实服务调用与 ScopeGuard 回归红 2 → 绿 2。AI 配额原子预留和 generation 所有权仍属待修复。
- R16：报表收集阶段 16 MiB 文本预算、行列数限制和流式 worksheet XML；ZIP 写入期间限制 10 MiB。AI 使用 64 Ki 字符帧、8 Mi 字符协议与 1 Mi 字符正文上限，移除重复累积缓冲；复核发现的 CR 换行回归已修正，包含跨缓冲区 CRLF。AI/报表聚焦 12/12；不是容量认证。
- R13：精确匹配 16 份历史脚本的 UUID 执行兼容，4/4 单元测试；新增 203 双库前向迁移覆盖 68 列，升级/非法值/中间态数据库测试已编写，环境运行未执行。

- R10：配额改为原子预留请求/Token、204 持久化凭据与原月份幂等结算；取消/未知用量保留预算，Guard 改为 Scoped。真实双库并发与跨月计账测试尚未完成，未关闭整组。
- R11 当前问题收尾：205 持久化代次/租约/取消标志，启动短事务、独立作用域续租、本地期限、远程取消及旧代清理隔离已实现；补 HTTP 启动失败、事务回滚、旧代取消、卡住续租和过期显示测试。AI 全模块 33/33，集成测试项目编译 0 警告/0 错误；双库持久化竞争测试已编写但未执行。
- 停止前验证与未处理事项统一见 [修复状态记录](../../verification/2026-09-07-full-project-review-fixes-status.md)，本计划未勾选项仍保持待处理/待验证。
