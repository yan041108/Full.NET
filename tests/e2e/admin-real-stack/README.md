# 真实后台浏览器测试

执行时机、影响集和证据要求以[测试与验证](../../../rules/development-quality.md#11-测试与验证)为准。

正式入口构建并运行 Release 的 Migrator、API 和独立 Worker。默认编译缓存与宿主日志位于系统临时目录的 `fullnet-real-stack/<工作区标识>/`：同一工作区的双库共用编译缓存，日志按 Provider 分开保存。

集中验收或 CI 可显式配置以下绝对路径：

- `FULLNET_E2E_BUILD_ROOT`：三个 Host 共用的构建产物目录。
- `FULLNET_E2E_LOG_ROOT`：当前运行的 API、Worker 日志目录。

路径配置由实际启动入口读取，跨 Playwright 进程仍然生效；它不改变用例、断言或超时。CI 只收集日志和浏览器报告，不上传编译缓存。
