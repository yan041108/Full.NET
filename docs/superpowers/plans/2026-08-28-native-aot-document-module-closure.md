# Document 模块 Native AOT 闭环实施计划

**Goal:** 让 Host.Api Native AOT 原生产物在 SQL Server/MySQL 上完成 Document 核心 Host API，并以静态门禁阻止匿名 SQL 参数和遗漏行物化器回归。

**Architecture:** 保持 Document 模块、显式 SQL、Host 数据范围、Files 引用边界、事务、权限和公共 HTTP 契约不变。模块内部统一使用固定键名参数工厂，并在启动阶段注册全部持久化记录的 ordinal 物化器；复用已验证的 AOT 多结果执行器。现有核心 Native 外部进程测试增加不依赖真实文件上传的 Document 纵向链路。

**Baseline:** `bf76418e08b6c3c69ab845421a22bce4e9a2ed83`

**Task snapshot:** `native-aot-document-20260828`

**Scope boundary:** 不修改数据库结构、SQL 语义、公共 API、Files 存储、匿名分享安全策略或生产配置；Auditing 留作独立切片。

## Task 1: 静态闭包 RED

- [x] Architecture 门禁拒绝 Document SQL 执行调用中的匿名参数对象。
- [x] Architecture 门禁要求全部 12 个持久化记录注册 AOT 物化器。
- [x] 运行聚焦测试，确认失败来自现有匿名参数和缺失 contributor。

## Task 2: 参数与物化器 GREEN

- [x] 新增 `DocumentSqlParameters`，替换全部 SQL 匿名参数并保持空值、名称与类型不变。
- [x] 新增 `DocumentDapperAotMaterializerContributor`，按稳定列名注册全部记录并兼容同类型的不同显式投影。
- [x] 增加 Data.Dapper 友元和模块引用，在 `FULLNET_AOT_COMPILE` 下同步注册。
- [x] 重跑 Architecture、影响集与 AOT analyzers。

## Task 3: 双库原生进程闭环

- [x] 扩展核心 Native E2E，覆盖无需文件上传的 Document 创建、详情、分页及目录/统计读取。
- [x] 重新发布 Linux ELF，并在 SQL Server/MySQL 执行真实原生进程测试。
- [x] 运行任务快照 inner/slice、governance、naming、`git diff --check` 和独立审查。
- [x] 记录结果、环境限制和未验证边界，不外推为 Auditing/Worker/Migrator 闭环。
