# Document 第二批整改

来源：[已批准设计 §7、§12](../specs/2026-08-02-document-module-design.md)。用户授权集中完成功能后，仅验收本次修改及必要边界，不运行全量测试。

基线：`main` / `a77902afd87af1662af3b43206d85160eeec566b`；第一批改动尚未提交，使用任务快照 `document-batch2-20261024` 保存增量起点。

## 实施范围

- 文档列表服务端关键字、分类及子孙、状态、类型和标签组合过滤，稳定分页，结果与总数共用 ACL。
- 统计限定当前可读未删除 Published 文档；当前大小与历史大小分离，字节累计后统一换算；UTC 半开日区间按访问日志聚合。
- 下载/预览按持久逻辑操作键归并重试与 Range；分享打开保持第一批事务幂等，Office 状态轮询不计访问。
- 分类变更与文档分类引用先锁模块拥有的 Host 闸门，再读取/锁根行；防间接环、并发互移、深度越界以及删除与引用创建竞争。回收站仍持有分类引用，因此不能删除仍被其引用的分类。
- Vue 使用服务端总数和筛选；编辑表单支持显式清空分类、描述、缩略图、标签，展示统计口径，修复匿名下载仍提交口令的调用。
- 288 双库可恢复迁移、源生成/AOT 物化、OpenAPI 和客户端同步。

## 聚焦验收

集中实现后执行：第 101 条与组合筛选、ACL/状态计数、当前/历史大小、UTC 边界和逻辑访问重放、分类并发与引用竞争的双库集成场景；288 半完成未记账恢复；受影响契约/页面用例、生成零漂移与模块 AOT 分析。浏览器只验收本批页面交互。第三批预览引用生命周期、全量测试和原生发布不在本批范围。

## 状态与证据

已完成关联实现与本地聚焦验收，未运行全量测试。实际结果如下（这是运行记录，不是重复维护的套件门槛）：

| 范围 | 实际结果 |
| --- | --- |
| 第二批双库业务 | `DocumentBatchTwoTests`：筛选/统计与分类并发两个方法，各 SQL Server/MySQL，共 4 项通过；包含第 101 条、组合条件、分页稳定性、ACL/状态、无版本计数、当前/历史大小、UTC 上下界、Range 同键与跨日重放 |
| 288 半完成恢复 | `Migration288DocumentQueryRecoveryTests`：两库共 2 项通过；已有 OperationKey 但未记账时补齐闸门、种子和唯一索引，再次重跑收敛 |
| 第一批必要回归 | `Metadata_acl_sessions_versions_and_release_follow_batch_one_contract`：两库 2 项通过；跨实例同会话下载现在精确记一次，其他版本并发、引用释放及超过 100 条关联日志的清理继续通过 |
| Office 输出必要回归 | `Document_share_security_is_atomic_with_` 两库 2 项通过；生产 Runner 配合受控 PDF 转换器，返回字节一致，匿名会话与已认证访问者各自重复读取只记一次 preview，轮询不记访问 |
| 运行时 OpenAPI | `snapshot-client-openapi.mjs --update --no-build`：SQL Server/MySQL 各 1 项通过；生成器 `--check` 零漂移；条目/分享/统计 3 个切片夹具检查通过 |
| .NET Unit | `DocumentApplicationAuthorizationTests`、`DocumentContractCompatibilityTests`、`DocumentShareStreamOwnershipTests` 共 7 项通过，零跳过 |
| Vue 与共享契约 | 7 个受影响文件共 29 项 Vue/适配器测试通过，`document-parity.test.ts` 3 项通过；最终 `pnpm --filter @fullnet/admin build` 包含类型检查并成功 |
| 浏览器 | Edge，1440×1000 与 390×844；第二批分页/清空与直接受影响的第一批生命周期菜单，共 4 项通过；截图复核分类、查询、标题、表格与紧凑分页，键盘确认通过 |
| 迁移静态检查 | `node --test tests/sql/document-batch-two-migrations.test.mjs`：命名、UUID、SQL 安全、注释共 4 项通过 |
| 静态闭包与发现 | Document 项目 `-p:FullNetAotAnalysis=true`，零警告、零错误；Integration 分片发现结构校验通过，无遗漏或重复；未执行整套分片测试 |

后端实际执行过滤器：

```text
FullyQualifiedName~DocumentBatchTwoTests|FullyQualifiedName~Migration288DocumentQueryRecoveryTests
FullyQualifiedName~Filters_statistics_and_logical_events_use_current_readable_scope|FullyQualifiedName~Metadata_acl_sessions_versions_and_release_follow_batch_one_contract
FullyQualifiedName~Document_share_security_is_atomic_with_
```

首轮分类并发及迁移恢复 4 项通过；另两项在最后一段测试种子缺少删除审计字段时被双库约束拒绝。修正种子后只重跑筛选/统计及必要第一批回归，4 项全部通过，不重复执行未变的分类/迁移场景。静态检查发现的相邻清理 SQL 误删已从任务快照的原始 Git Blob 原样恢复，最终第一批清理回归通过。

构建使用 `run-build-with-resources.mjs --cpu 2 --memory-gib 6`，Release、最大并发 2、禁用共享编译；运行时聚焦用例使用现有重型资源锁，无源码副本。环境为 Windows，.NET SDK 10.0.402 / Runtime 10.0.13、Node 24.20.0、pnpm 10.26.0、Docker Engine 29.9.1；复用项目 SQL Server/MySQL 测试容器，数据库按夹具隔离。

浏览器使用受控 HTTP；本批未执行真实浏览器到数据库全链路、全量测试、Native AOT publish 或原生外部进程测试，也没有性能/容量实测。早期布局验收发现并修正移动端挤出与固定操作列遮挡；生命周期菜单回归同步等待旧菜单关闭，最终 4 项通过。部分早期 Vite 运行出现 ResizeObserver 通知警告，不能据通过断言宣称全站控制台无告警。

模块整体保持 Build-verified、Capacity-not-verified 和非 Production-verified；第三批仍待实施。分批日志清理仍遵循第三批尚未完成的最小删除审计保留限制，不因本批统计通过升级。


最终验收输入：相对任务快照的 53 份非 Markdown 文件，按路径排序并将路径、空字节、文件原始字节、空字节依次累计 SHA-256：`fad4f4f08ebb7870cb55155321a180ac1995a1015c04497139fa1a3b66fd7ed9`。最终分支和 HEAD 未变，`git diff --check` 通过，改动未提交。

清理：临时资源包装脚本、浏览器截图/输出及失败测试生成的 6 份文件字节已逐路径删除；批量删除曾被自动审批策略拒绝，缩小为不带强制/递归的单路径操作后完成。仅在系统临时目录保留 4 个失败上下文的 7 份最小诊断文件，共 4242 字节（`fullnet-test-run-xtHTBY`、`fullnet-test-run-AGq6SH`、`fullnet-test-run-BAvkFa`、`fullnet-test-run-xEGGyl`），没有文件字节或二进制副本；根目录没有 artifacts。
