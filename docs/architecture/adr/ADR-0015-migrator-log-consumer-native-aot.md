# ADR-0015：Migrator 与 LogConsumer 独立 Native AOT 角色

- 状态：已实施并完成本机定向验收；用户于 2026-10-31 明确要求补齐实现，运行范围按[本机报告](../../verification/2026-10-31-native-remaining-hosts.md)登记，不扩大到外部容量或未执行机制。
- 范围：`Full.NET.Host.Migrator` 与 `Full.NET.Host.LogConsumer` 的 linux-x64 发布和对应外部进程场景。
- 关联：[ADR-0008](ADR-0008-api-native-aot-runtime-boundary.md)、[ADR-0010](ADR-0010-worker-native-aot-analysis-boundary.md)。原 Phase 1 历史排除保持为历史范围，本决策扩展两个独立角色。

## 决策

两个宿主继续默认 JIT，只有 `FullNetPublishMode=NativeAot` 显式启用 SDK `PublishAot`。发布复用真实 SDK、源码与托管输入冻结、实际 ILC 参数及发布清单、ELF 校验和精确告警门禁；每个角色使用自己的输出目录、指针和身份，不借用 API/Worker 的成功证明。

Migrator 仍为一次性独立进程，通过 Composition 的 Migrator Profile 装配 Migration/Seed 最小闭包。它执行现有 DbUp 嵌入 SQL、journal 与显式播种命令，不装配 API 认证中间件、HTTP 端点或 Worker 消费循环。迁移专属物化与命令参数必须静态注册；播种审计及会话锁也不能依赖匿名类型反射。Production 只允许 Baseline，API/Worker 不执行迁移和播种。

LogConsumer 保持独立日志角色及显式实验启用门禁，使用原有 TLS Kafka、HTTPS Elasticsearch、逐项 bulk 结果与 DLQ 确认后提交位点的语义。固定 bulk action 使用显式 JSON 写入。Confluent.Kafka 的 Linux native binding 只保留当前实际候选的三个精确类型；这不扩大到整个程序集，也不授予明文传输或跳过证书校验。

## 告警边界

自有代码不得增加 IL 抑制、通配 root 或关闭验证。LogConsumer 初始仅允许 `Confluent.Kafka.dll + IL2104` 的既有 native binding 边界；Migrator 初始仅继承 ADR-0008 已解释的非 Kafka 第三方程序集级告警。DbUp 或其他新增告警须先定位到实际机制，再按程序集和告警码精确登记，不能因属于第三方而自动放行。

## 验收

- Migrator：真实 ELF 在 SQL Server/MySQL 独享库执行迁移与 Baseline，检查 journal、代表性结构、播种审计及管理员；同包幂等重跑；Production Overlay 拒绝，迁移失败时不执行播种。普通 JIT 数据库前置不算该角色 Native 证据。
- LogConsumer：真实 ELF 对 TLS Kafka 和 HTTPS Elasticsearch 消费；覆盖单条/批次成功、ES 拒绝经 DLQ 确认后提交、DLQ 不可达不提交及重启重放、固定事件 ID 和 SIGTERM 正常停机。
- 定向分析、真实 publish 和对应运行均完成后，才按实际范围标记 `Aot-published`。不自动运行全仓或完整 Native 矩阵，不改写此前冻结后端 133 项。

桌面手动、生产容量和独立物理故障域不由本决策的本机后端结果代替。
