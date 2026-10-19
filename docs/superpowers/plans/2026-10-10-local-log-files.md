# Local 日志文件交付计划

用户已批准 Local 可配置结构化 txt 落盘及具备文件存储时的生产准入。基线 `db17f85d2cbf11c496370d604b354e2a8e47debd`；快照 `logging-local-file-20261010`，保留现有无关改动。

## 设计与验证

- Hosting 内扩展 LocalFile 强类型配置；默认关闭，仅显式 Local 可启用。相对目录基于 ContentRoot，按宿主应用隔离，目录持有排他进程锁；多实例必须配置不同目录/挂载。启动检查读写能力及预算，禁止重解析链接清理。
- 复用受限 UTF-8 LogEnvelope；两个现有后台线程各写一条文件流，不在请求上增加磁盘 I/O。按 UTC 日期/大小滚动，只清理严格匹配的自有文件，目录总量预算含缓冲中的逻辑字节。
- 定时刷新通过后台消费线程的有界轮询执行，不增设无界队列。流关闭由消费线程完成，保持宿主共享退出预算；满盘/故障计数与限频安全诊断不包含正文或路径。
- 应用/Helm 允许启用文件的生产 Local；生产 Console-only Local 仍拒绝。Chart 提供明确挂载及预算配置，运行时开关不热切换。
- 文档同步 Local 存储位置、进程互斥、容量及故障边界，不宣称跨崩溃零丢失。

## 执行

- [x] 先验证生产 Local 文件配置因当前拒绝而失败。
- [x] 实现配置、文件存储与消费生命周期；覆盖正常写入、滚动、保留/预算、外部文件保护、故障及停机。
- [x] 接通 Host/Helm，补启动拒绝及模板正反例。
- [x] 同步知识库/运维及准入说明。
- [x] 集中验证日志 Unit、Helm、AOT 分析/架构、共享宿主双库 Smoke；原生运行范围如实记录，完成独立审查。

不自动提交、推送或生产切流。没有新增依赖、数据库结构或审计语义变化。

## 验证记录（持续更新）

- 初始生产 Local 文件宿主用例在实现前按预期失败：旧准入规则拒绝 Local。
- Helm：`node --test tests/deployment/helm-contract.test.mjs`，28/28 通过，0 跳过；覆盖 PVC、API/Worker HPA、单副本、Recreate 与非 Local 副本基线。
- 治理：`pnpm test:governance`，59/59 通过，0 跳过。
- 独立审查已修复文件数量上限后的滚动、维护清理时的目录链接检查、Schema 条件位置和 Worker HPA 限制；原生用例补显式优雅退出码与分通道事件断言。
- 原生 API 链接执行会话中断后保留原 Docker 容器，通过恢复入口等待真实退出、校验既有告警 allowlist 和 ELF，再执行日志 Unit、AOT 分析/架构、双库 Smoke 与原生 API 用例。未完成项不能记为通过。
- 集中恢复入口：原 Docker publish 退出 0，ELF 140,571,648 字节，17 条既有允许告警；日志 Unit 328/328、AOT Architecture 73/73，均 0 跳过；API AOT 分析成功。
- 首次双库 Smoke 未通过：复用 SQL Server 容器在历史测试库恢复期间 OOMKilled=true、退出 137，导致迁移 TCP 连接被重置；确认环境根因后终止本次失败测试进程，改为 FULLNET_TESTCONTAINERS_REUSE=0 的独立临时容器重跑。保留其他批次容器及锁。

## 最终本地验收

2026-10-10 完成本轮 Local 文件模块批次；环境为 Windows + Docker Desktop/WSL2，原生 API 为 SDK 10.0.400 构建的 linux-x64 ELF。测试使用独立临时数据库，未更改实际部署的交付模式，也未进行容量/P99 或 Worker 原生发布验收。

- `node .tmp/local-file-recover.mjs`：恢复原成功 Docker publish 的证据；日志 Unit 328/328（含 LocalFile 10 个用例）、API AOT 分析成功、AOT Architecture 73/73，均无跳过。随后 Smoke 因上述旧容器 OOM 失败，保留失败日志，不计为通过。
- `node .tmp/local-file-integration-final.mjs`：校验并复用本轮集成程序集，运行 `node scripts/testing/run-integration-shard.mjs smoke`，FULLNET_TESTCONTAINERS_REUSE=0，双库 8/8、0 跳过；Linux SDK 容器执行 NativeApiLocalFileMySqlE2ETests / NativeApiMySqlE2ETests / NativeApiSqlServerE2ETests，3/3、0 跳过。Local 用例实际验证 Production 配置、general 资源事件、priority 404 事件、受限 JSON、SIGTERM 退出码 0 与目录锁释放。
- `pnpm test:governance`：最终 59/59、0 跳过。
- `node --test tests/testing/test-matrix.test.mjs`：最终 7/7、0 跳过。
- `node --test tests/deployment/helm-contract.test.mjs`：28/28、0 跳过。
- `git diff --check`：通过。当前分支 main，HEAD 仍为上述基线；无关修改保留，未提交或推送。

证据：`.tmp/local-file-validation.log`、`.tmp/local-file-integration-final.log`、`.tmp/local-file-governance-final.log`、`.tmp/local-file-matrix-final.log`、`.tmp/local-file-helm.log` 及对应 TRX。恢复入口为本轮忽略的临时工具，不新增长期测试/发布入口。
