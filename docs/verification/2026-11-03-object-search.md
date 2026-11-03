# F17 近期对象搜索验证

日期 2026-11-03；基线 `main / 75be6fc0e17433f9c59c42553093dacd479ef9d1`；任务快照 `foundation-f17-object-search-20261103`。工作区原有大量改动保持，实际输入为快照后的本批差异。

交付 Document Host 文档与样例 Tenant 企业申请两个来源；上下文内独立读取，未扩大 Enterprise 对 Document 的依赖。新增来源内摘要 API、官方配对迁移 356/样例配对迁移 004、静态来源注册、全局搜索分组、取消/超时、固定详情导航、文档与主单只读详情。未建立中央索引、跨上下文查询或新任务引擎。

## 功能与新鲜证据

- 直接相关前端 13 个文件共 61 个唯一用例通过；最初批次 59 项，后新增两个深链边界随两个文件 8 项定向补跑，不累计重复运行数量。覆盖两来源独立失败、250ms 防抖、8 秒超时、迟到响应、撤权/上下文清理、有界分页、固定路由及键盘焦点、详情失败关闭和默认方案延迟应用。最新 Vue 类型检查及生产构建通过。
- 双库对象搜索 API 2/2，原普通成员机构/租户矩阵扩展搜索后 2/2：字面符号、大小写、90 日窗口、稳定排序/分页、最小字段、非法/重复参数、缺权、Host/Tenant 拒绝、ACL 授予/撤销、超管不能绕过 restricted 文档、机构与第二租户隔离。实际采集时验证第 20 页截断标识及不再提供下一页。
- Edge/Kestrel/SQL Server/Vue 的真实浏览器 1/1：Host 文档检索、方向键/Enter、权威详情读取/关闭清理目标、切入租户后只显示企业申请、实际申请详情、390 窄屏及面板 axe 无 critical/serious；截图已人工复核。最终深链改动后重跑同一场景通过，零跳过/零 flaky。
- 实际运行时 OpenAPI 导出 1 项通过；新增搜索和文档详情操作进入 manifest，四份生成客户端更新，离线快照与生成零漂移。Document/样例模块 AOT 分析均零警告/错误；未运行完整 Native 发布或全量测试。

初轮 MySQL 在样例迁移入口失败，尚未运行搜索 SQL；修正测试工厂为与正式 Migrator 一致的“仅迁移连接允许固定 DDL 变量”，业务连接保持严格参数模式，重跑通过。Vue 首轮测试存根定位和错误返回 mock 的 beforeEach 已修复，不将失败轮次算作通过。发现仅主单读取权不能使用旧主从详情，已补独立只读分支；详情期间恢复的默认条件会在关闭后应用，避免面板与列表不一致。

## 同步等待和查询上界

复用既有认证、可信租户与精确权限管线；搜索新增一次当前交互会话权威授权 Port。文档执行一次带本模块 ACL 的摘要 SELECT；申请另调用一次角色数据范围解析 Port，然后执行一条已注入可信机构条件的摘要 SELECT。未逐结果回源、不 COUNT、不读取正文/文件/金额/申请人，不增加缓存、Outbox、外部 I/O 或写入业务。上述 Port 内部复用已有边界，未采集其 SQL 明细；下表直接 SQL 耗时不代表完整 HTTP 时延。

窗口固定为最近 90 个 UTC 自然日，输入 2–64 字符，固定每页 5 加一行，页码 1–20。OFFSET 最多 95，不开放深页；来源失败与无结果分别表达。排序为 CreatedAtUtc/Id，表达当前数据，不承诺跨页快照或跨来源相关度。

## 双库实际计划

Windows 10.0.26300、i9-14900K/32 逻辑处理器、63.7 GiB、.NET 10.0.13 Release；本机 Docker SQL Server 2022 CU14/MySQL 8.0。每来源 2,000 行，包含当前/过期窗口；申请含两个租户。单并发，预热 5 次、采样 20 次；P99 为这组小样本的最大值，不能推导生产 SLO。

| 库 | 来源/页 | P50/P95/P99 ms |
| --- | --- | --- |
| SQL Server | 文档/1 | 0.4744 / 0.8503 / 0.9097 |
| SQL Server | 文档/20 | 0.4789 / 0.6217 / 0.6651 |
| SQL Server | 申请/1 | 0.3669 / 0.4066 / 0.4470 |
| SQL Server | 申请/20 | 0.4300 / 0.5616 / 0.7393 |
| MySQL | 文档/1 | 0.4436 / 0.5953 / 0.7359 |
| MySQL | 文档/20 | 0.4210 / 0.5133 / 0.5254 |
| MySQL | 申请/1 | 0.3694 / 0.4123 / 0.4243 |
| MySQL | 申请/20 | 0.3962 / 0.4318 / 0.4856 |

SQL Server 四种形状实际选择对应近期索引，无 Sort，文档 ACL 另使用已有权限索引。MySQL ANALYZE 后的 EXPLAIN ANALYZE 四种形状均使用近期索引且无 Sort；文档第 20 页估算 JSON 报 ALL，但实际执行树为近期索引 range scan，因此没有只凭 possible_keys 或估算计划判定实际选中。MySQL 原始 JSON 和实际执行树均保留。

在隔离测试数据库保留文档索引、删除样例索引后，成对迁移完整重放两次，验证部分完成恢复。每来源增加一棵二级索引及空间/维护成本，SQL Server 包含列变更也需要维护；未做无索引 A/B 或写放大测量，不宣称吞吐提升。未测 CPU、连接池等待、锁尾延迟或更大数据规模，保持 `Capacity-not-verified`。

原始计划、摘要及包体：`C:\Users\dai27\AppData\Local\Temp\fullnet-f17-query-evidence-6f440636059a49059b7cb89ce6aeea38`。主要日志为系统临时目录 `fullnet-f17-functional.log`、`fullnet-f17-browser-frozen.log`、`fullnet-f17-ui-final.log`、`fullnet-f17-deeplink-final.log`、`fullnet-f17-typecheck-verified.log`。采集使用 `FULLNET_OBJECT_SEARCH_EVIDENCE`，命令为 `run-dotnet-test-suite.mjs workflowActivityIntegration --filter 'FullyQualifiedName~ObjectSearchApiTests|FullyQualifiedName~EnterpriseRequestAccessMatrixTests|FullyQualifiedName~ObjectSearchBrowserUiTests' --minimum-expected-tests 5 --no-build`。

## 保留的整体门禁差集

最新生产首屏静态图 72 个 chunk，minified 1,804,585 B / gzip 445,027 B，仍超过原 1,426,485 / 376,928 B +5% 预算。ObjectSearchPanel 延迟 chunk 为 4,164 / 1,733 B，不在首屏静态图；没有提高预算或告警阈值。命名检查仍为 36/37，唯一余项是本批未修改的 MySQL 352 心跳主键名称。功能验收通过不等同全站发布或容量认证。
