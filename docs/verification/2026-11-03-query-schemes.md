# F18 查询方案验证

日期 2026-11-03，基线 `main / 75be6fc0e17433f9c59c42553093dacd479ef9d1`，任务快照 `foundation-f18-query-schemes-20261103`。实际输入为当前工作区本批差异，未创建整仓副本。

交付 Settings 强类型方案、个人默认 CAS、租户共享及本人所有权、配对迁移 355、业务静态 Schema、九个生成客户端操作、企业申请真实查询接线和浏览器常用入口。Host 支持按各 Schema 显式声明，企业申请不开放 Host；框架 Host 隔离由测试专用 Schema 验证，不冒充实际 Host 业务接入。

## 实际结果

- 13/13 聚焦 .NET 单测：Schema/配置/必填 JSON/SQL Scope、最小 Worker 装配以及 AOT 命令创建和复用的时间精度；35/35 直接相关 Vue 用例通过，类型检查和生产构建通过。
- 双库 API 2/2：真实邀请接受的普通成员、个人与共享可见性、创建者修改保护、超管不可读他人私有方案、CAS、撤销后默认失效、旧 Schema 修正、32 条并发上限、稳定游标、第二租户与 Host 隔离、DDL 部分完成后重复执行。
- 改变参数绑定后，双库方案、ImportExport/Reporting 本人游标和真实浏览器合计 5/5 通过；执行计划采集批次的双库方案 2/2 通过。重复运行不重复累计唯一用例数。
- Edge + Kestrel + SQL Server + Vue：保存、默认刷新恢复、列设置、共享条件由只读成员应用到真实业务列表、个人入口跨账号隔离及键盘激活；桌面/390 窄屏无横向溢出，查询面板 axe 无 critical/serious。已人工复核截图。浏览器无跳过或 flaky。
- 实际 OpenAPI 导出 1 项通过，规范快照及四份生成产物零漂移。Settings/样例模块 AOT 分析通过；公共时间参数修复后的 Settings 依赖闭包复查为零警告/错误。未执行完整 Native 发布或全量测试。

失败证据未计作通过：初始 Host 空 GUID 设计违反分配主键门禁，改为可信 ScopeKey 并保留实际 TenantId；测试角色旧 `all` 值改为 `RoleDataScopeKinds.All`；真实截图暴露只读列表 500 后补了响应和列表结果断言；原删除按钮对比度不足已修复。SQL Server 非毫秒网格时间使旧参数绑定重复翻页，现字典执行与 AOT 创建路径显式使用 DateTime2，并用固定 `.123456` 秒数据和无重复页断言验证；没有提高限流额度规避故障。

## 查询范围与执行计划

Windows 10.0.26300、i9-14900K/32 逻辑处理器、63.7 GiB、.NET 10.0.13 Release；本机 Docker SQL Server 2022 CU14/MySQL 8.0。每库 2,048 条方案、2 租户、每租户 32 用户/各 32 条，单并发，预热 5 次/采样 20 次，页大小 7 加一条探测。只比较本批本人/共享首屏及后续页，不测试容量。

本人和共享各一次有界摘要 SELECT，不 COUNT、不读配置 JSON；默认读取至多两次固定元数据查询，无逐行回源。权威会话与业务读取授权在本地事务前完成；写入由用户/上下文/Grid 默认行锁串行化。无缓存、事件或外部 I/O。

| 库 | 查询 | P50/P95/P99 ms |
| --- | --- | --- |
| SQL Server | 本人首屏 | 0.4650 / 0.5370 / 0.5375 |
| SQL Server | 本人后续 | 0.4663 / 0.5686 / 0.7228 |
| SQL Server | 共享首屏 | 0.4389 / 0.5849 / 0.6569 |
| SQL Server | 共享后续 | 0.4744 / 0.5123 / 0.5532 |
| MySQL | 本人首屏 | 0.3954 / 0.6701 / 0.7365 |
| MySQL | 本人后续 | 0.3570 / 0.4292 / 0.4487 |
| MySQL | 共享首屏 | 0.3599 / 0.4410 / 0.4577 |
| MySQL | 共享后续 | 0.4458 / 0.5102 / 0.5486 |

耗时为直接 SQL 往返，不包含 HTTP 授权。SQL Server 实际选择 Owner_Created/Shared_Created 索引 Seek 与主键回表，无 Sort；MySQL ANALYZE 后实际 key 分别为两个索引，四种形状均无 filesort。没有索引移除 A/B 或写入成本实测，不宣称较旧实现提速；新增两棵二级索引会增加写入维护与空间成本。未测 CPU、池等待、锁尾延迟或系统吞吐，保持 `Capacity-not-verified`。

原始计划与摘要：`C:\Users\dai27\AppData\Local\Temp\fullnet-f18-query-evidence-9be7fc39814640cebbf3f0ee9d68ab55`。主要命令为 `run-dotnet-test-suite.mjs workflowActivityIntegration --filter FullyQualifiedName~QuerySchemesApiTests --minimum-expected-tests 2 --no-build`，采集时设 `FULLNET_QUERY_SCHEME_QUERY_EVIDENCE` 到上述独立临时目录。正式日志为系统临时目录 `fullnet-f18-precision-functional.log`、`fullnet-f18-query-evidence.log`、`fullnet-f18-precision-unit-final.log`。

## 尚未通过的整体门禁

生产首屏静态图 71 个 chunk，minified 1,796,919 B / gzip 442,649 B，仍超出既定 1,426,485 / 376,928 B +5% 预算；未修改基线。企业申请延迟 chunk 56,771 / 16,135 B，不在首屏静态图。命名检查 36/37，余项仍为本批未修改的 MySQL 352 心跳主键名称。上述失败事实保留，功能用例通过不等同全站发布验收通过。
