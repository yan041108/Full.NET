# CodeGeneration 模块 Native AOT 闭环计划

基线：`9f04f10d1f9f1ea982d462af5b414a29307a36c8`

## 目标

闭合 Host.Api 可达的 CodeGeneration SQL 参数与行物化路径，并以 Linux 原生产物在 SQL Server/MySQL 上验证模板、目录和运行读取。Worker 检查点清理只闭合静态绑定，不外推为 Worker Native AOT 运行时验证。

## 步骤

1. 新增可失败的 Architecture 门禁，枚举匿名 SQL 参数及全部自定义结果类型。
2. 引入模块内固定参数工厂，保持原 SQL、事务和分页语义。
3. 补齐 CatalogColumn、Template、Run、CheckpointCleanup materializer。
4. 扩展 Native E2E，覆盖可安全执行的 CodeGeneration Host 流程。
5. 执行 Architecture、AOT analyzer、Linux publish、双库原生进程和受影响测试。
6. 独立审查后提交。
