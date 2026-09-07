# 2026-09-08 复审恢复问题修复

基线：`020ba27a8fa6c5145208f41f7cadd9c6d89ba789`，分支 `main`。用户授权继续处理上一轮复审发现的四项缺陷；任务快照为 `review-recovery-fixes-20260908`。修复完成后用户授权本地提交；未推送、未发布、未向业务库应用迁移。保留工作期间出现的无关 `Full.NET.slnx` 改动。

## 修复结果与边界

1. **K3Cloud 重复 Save：** 拆分保存和提交，外部单据 ID/编号持久化之后才执行 Submit。确定的提交失败仅提交原单据；未知结果及过期 pending 禁止重发，需先运维对账。畸形/缺失响应不再当作确定失败。只返回编号的 Submit 失败保持正确状态。当前没有新增自动对账入口，不宣称未知调用自动恢复。
2. **岗位导入重放：** Organization 自有回执以可信任务标识、原始行号和行载荷摘要防止重复执行。占位、岗位创建、机构/职级关联及回执完成同事务，失败整行回滚；已提交行返回原岗位标识，载荷改变则拒绝。新增 209 双库迁移，无跨模块事务或外键。
3. **文件删除恢复：** released 继续参与扫描，物理删除失败保留墓碑供后续重试，成功后才移除元数据。单个对象的删除失败不阻塞后续条目。
4. **文件扫描公平性：** Worker 共享互斥游标，跨 DI 作用域继续上轮位置；到尾部回绕，重启从头安全扫描。测试覆盖有限批次前部长期被引用时后续 pending 仍可被处理。

升级前须停止并排空旧导入 Worker。旧版本已写入但没有检查点的任务没有新回执，不能根据同编码岗位自动推断归属，应先对账后恢复。209 只增加新回执表，不修改历史迁移或补造历史成功凭据。

## 验证记录

- 先观察失败：未知 K3Cloud 调用被重放、文件 released 不被选择、有限批次第二轮未继续、岗位关联失败后仍提交，以及仅单据编号时状态误判；随后最小修复并复跑。
- `dotnet run --project tests/Full.NET.UnitTests -c Release --no-restore -p:BuildInParallel=false -- --filter 'FullyQualifiedName~Full.NET.UnitTests.K3Cloud|FullyQualifiedName~Full.NET.UnitTests.Files|FullyQualifiedName~Full.NET.UnitTests.ImportExport|FullyQualifiedName~Full.NET.UnitTests.Organization'`：155 通过，0 失败，0 跳过。
- `dotnet run --project tests/Full.NET.ArchitectureTests -c Release --no-restore -p:BuildInParallel=false -- --filter 'FullyQualifiedName~NativeAot|FullyQualifiedName~ModuleLocalTransaction|FullyQualifiedName~GlobalSqlStatement|FullyQualifiedName~NamingConvention'`：80 通过，0 失败，0 跳过。
- `pnpm test:governance`：52 通过；`pnpm test:naming`：31 通过；`pnpm test:integration:tooling`：46 通过。
- 新增双库持久化测试覆盖回执回滚/提交、租户隔离、并发唯一领取、重复迁移保留结果；仅编译，不表示双库已执行。
- `pnpm test:integration:affected:plan -- --snapshot review-recovery-fixes-20260908 --phase inner`：已登记 209 恢复集与相关模块；K3Cloud 安全回退 Smoke，未执行重型集合。
- `dotnet build tests/Full.NET.IntegrationTests -c Release --no-restore -p:BuildInParallel=false --nologo`：最终编译成功，0 警告、0 错误；未运行数据库测试。
- `pnpm test:aot:analyzers`：成功，0 警告、0 错误，脚本恢复默认 JIT 还原图；不是 Linux 原生发布证据。
- `pnpm test:dotnet:architecture --selection api-native-aot --no-build`：73 通过，0 失败、0 跳过；复用本轮已构建且源码未改变的架构测试程序集。
- `git diff --check`：通过；仅有 Git 换行转换提示。验证时修复尚未提交，分支保持 `main`。

独立只读复核检查实际事务和资源生命周期，发现的仅编号状态分类问题已补失败回归后修正。未运行真实金蝶请求、SQL Server/MySQL 容器、Linux 原生发布或真实浏览器；不标记 `Verified`。

## 分片清单的既有漂移

`pnpm test:integration:partitions` 未通过：当前程序集发现 810 项，清单在增加本次四项后为 792。分片枚举分别为 SQL Server API 74、MySQL API 74、migrations 442、infrastructure 163、messaging-heavy 57；对应清单为 74、75、424、162、57。本轮没有删除或修改既有 API 测试，保留原门槛，未通过降低 MySQL API 门槛来换绿。该差异须单独核对既有测试登记后收敛，不能据此声称完整 Integration 门禁通过。

日志位于被 Git 忽略的 `artifacts/reviews/recovery-*.log`。
