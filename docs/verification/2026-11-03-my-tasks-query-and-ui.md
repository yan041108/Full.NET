# F19 本人任务查询与页面验证

日期：2026-11-03（Asia/Shanghai）。基线：main / 75be6fc0e17433f9c59c42553093dacd479ef9d1；实际输入为当前工作区与 foundation-f19-my-tasks-20261103 快照后的本批差异。

环境：Windows 10.0.26300、Intel Core i9-14900K（32 逻辑处理器）、63.7 GiB 内存、.NET 10.0.13，Release。本机 Docker SQL Server 2022 CU14（夹具上限 3 GiB）及 MySQL 8.0（1.5 GiB）。负载与数据库共享资源，不代表生产或独立故障域。

## 方法

每来源 2,000 行、2 租户、每租户 10 用户/各 100 行，包含相同时间及 2 KiB 私有正文。相同生产 SQL 比较移除/恢复候选索引的首屏、后续页、状态筛选；单并发、预热 5 次/采样 30 次，每次最多返回 21 条摘要。生产默认 20/最大 100，每来源一次 SELECT，不 COUNT、不逐行读取、不读取正文。

MySQL 两组均 ANALYZE TABLE，陈旧统计信息样本作废；候选断言检查 EXPLAIN 实际 key，而非 possible_keys。迁移 354 在另一来源索引已存在时完整执行并重跑。SQL Server 读取数为所有算子 ActualRowsRead 合计，包含索引和回表，不能当作唯一业务行数。MySQL 原始实际访问树保留，候选三个形状使用本人索引且无 filesort。未测 CPU、连接池等待、锁分位或系统吞吐。

## 查询结果

耗时为直接 SQL 客户端往返毫秒，未包含 HTTP 授权。

| 库 | 来源 | 形状 | 候选索引 | P50/P95/P99 ms | 算子读取合计 | 排序 |
| --- | --- | --- | --- | --- | --- | --- |
| SqlServer | import | first | 无 | 1.5309/1.9883/2.011 | 2000 | 有 |
| SqlServer | import | after | 无 | 1.4932/1.7492/2.1099 | 2000 | 有 |
| SqlServer | import | filtered | 无 | 1.4492/1.7091/1.7291 | 2000 | 有 |
| SqlServer | import | first | 有 | 0.5722/0.6642/1.2173 | 42 | 无 |
| SqlServer | import | after | 有 | 0.5461/0.7543/1.81 | 42 | 无 |
| SqlServer | import | filtered | 有 | 0.4956/0.5716/0.7471 | 72 | 无 |
| SqlServer | report | first | 无 | 2.6935/4.4971/5.0173 | 2000 | 有 |
| SqlServer | report | after | 无 | 1.7907/2.8367/3.0119 | 2000 | 有 |
| SqlServer | report | filtered | 无 | 1.8434/3.1427/3.4276 | 2000 | 有 |
| SqlServer | report | first | 有 | 0.4842/1.0166/1.5806 | 42 | 无 |
| SqlServer | report | after | 有 | 0.5942/1.2242/1.2682 | 42 | 无 |
| SqlServer | report | filtered | 有 | 0.4662/0.5683/0.631 | 72 | 无 |
| MySql | import | first | 无 | 0.6743/0.9168/1.3041 | 见实际访问树 | 无 |
| MySql | import | after | 无 | 0.7684/0.9048/0.9261 | 见实际访问树 | 无 |
| MySql | import | filtered | 无 | 0.9316/1.2842/1.3823 | 见实际访问树 | 无 |
| MySql | import | first | 有 | 0.3426/0.4279/0.4431 | 见实际访问树 | 无 |
| MySql | import | after | 有 | 0.443/0.5013/0.5851 | 见实际访问树 | 无 |
| MySql | import | filtered | 有 | 0.3291/0.4341/0.445 | 见实际访问树 | 无 |
| MySql | report | first | 无 | 1.2879/1.6485/2.1299 | 见实际访问树 | 有 |
| MySql | report | after | 无 | 0.922/1.102/1.1378 | 见实际访问树 | 有 |
| MySql | report | filtered | 无 | 0.7775/0.9971/1.0341 | 见实际访问树 | 有 |
| MySql | report | first | 有 | 0.3793/0.4201/0.431 | 见实际访问树 | 无 |
| MySql | report | after | 有 | 0.4249/0.4877/1.6299 | 见实际访问树 | 无 |
| MySql | report | filtered | 有 | 0.3445/0.4402/0.5 | 见实际访问树 | 无 |

## 写入影响

每组另外采样 30 次单行插入和 100 行状态更新；清理探针并恢复原状态分布。新增索引增加一棵 B-tree 的写入维护成本，混合本机负载中的较低时延不证明写入优化，不据此增加并发。

| 库 | 来源 | 候选索引 | 插入 P50/P95/P99 ms | 更新 100 行 P50/P95/P99 ms |
| --- | --- | --- | --- | --- |
| SqlServer | import | 无 | 45.6044/47.3304/49.658 | 4.3409/10.0147/10.8424 |
| SqlServer | import | 有 | 45.6471/50.7441/51.3889 | 4.3038/6.0083/9.4543 |
| SqlServer | report | 无 | 1.6556/5.0463/11.7268 | 4.698/7.4463/8.5467 |
| SqlServer | report | 有 | 1.7828/2.5979/4.845 | 3.4129/4.0465/4.285 |
| MySql | import | 无 | 2.2697/8.3371/47.3048 | 5.702/10.9955/11.5304 |
| MySql | import | 有 | 2.1676/4.3524/5.1961 | 4.3158/6.7009/8.4351 |
| MySql | report | 无 | 3.0704/4.8185/6.2721 | 6.4029/9.9366/10.704 |
| MySql | report | 有 | 2.2055/2.6035/2.7446 | 4.1565/7.2887/7.9063 |

## 功能、包体与限制

3/3 游标单测、34/34 相关 Vue 用例、真实双库本人 API 及 1 个真实浏览器流程通过，无跳过。Edge/Kestrel/SQL Server/后台循环/Files 均真实运行，仅替代外部报表数据库端口。受邀普通成员创建导出，经我的任务下载 XLSX（PK）并进入来源详情；桌面/390px、Escape、当前页 axe 无 critical/serious。不代表独立 Worker 进程终止恢复验收。

类型检查、Vite Release 构建和 Reporting/ImportExport/Platform 模块 AOT 分析通过，分析不代替完整 Native 发布。真实 OpenAPI 导出通过，规范快照及四份生成客户端零漂移。

当前首屏静态图 71 个 chunk，minified 1782514 B/gzip 439281 B，**未通过**原预算 1426485/376928 B +5%。MyTasksView 延迟 chunk 为 11267/4050 B，不在首屏静态图。没有调整预算基线。

命名检查 36/37：本批表/列注释、固定 DDL 审查及双库结构已补齐；余项为未修改的 MySQL 352 心跳主键显式名称。未运行全量测试、完整 Native 发布或更大规模数据/并发，保持 Capacity-not-verified。

## 复现与原始记录

- 资源包装构建 IntegrationTests 后，run-dotnet-test-suite.mjs workflowActivityIntegration 仅选择 MyTasksApiTests（最小 2）和 ReportingPublicationBrowserUiTests（最小 1），使用 --no-build。单库选择方法后缀 with_mysql/with_sql_server（最小 1）。
- Vue 只选择 myTasks、MyTaskSourcePanel、MyTasksView、useTaskDetailRoute、my-tasks API、ReportingExportTasksView、ImportExportTasksView 七个文件。
- 原始两库 summary.json、计划与写入分位保存在本机小型临时证据目录：C:\Users\dai27\AppData\Local\Temp\fullnet-my-tasks-evidence-b34fc906e4414b919278318e8088ec2d。没有源码副本和发布二进制。
- 系统临时日志：fullnet-my-tasks-final-integration.log（SQL Server 通过及原 MySQL 夹具失败）；fullnet-my-tasks-mysql-browser.log（MySQL/浏览器通过）；fullnet-my-tasks-mysql-plans.log（校准统计及实际索引断言通过）。
