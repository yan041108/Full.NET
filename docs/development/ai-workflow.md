# AI 开发环境与 Full.NET 任务路由

## 环境自检

在仓库根目录运行：

```powershell
pnpm run doctor --profile frontend
pnpm run doctor --profile backend
pnpm run doctor --profile full
# pnpm 本身异常时直接从 Node 启动；机器读取使用 JSON。
node scripts/doctor.mjs --profile full --json
```

默认范围为 `full`。必须使用 `pnpm run doctor`，因为 `pnpm doctor` 是包管理器自己的内置命令。所有范围检查当前 Node、PATH 中 pnpm 的实际路径及版本、Git；`frontend` 追加 Vue/Playwright 依赖解析、浏览器文件和前端测试端口；`backend` 追加 global.json SDK 选择及 Docker Engine；`full` 合并并检查真实栈前端端口。版本来源为 `.nvmrc`、`package.json`、`global.json`，不单独维护副本。

退出码 `0` 表示所选范围没有失败项，`1` 表示存在环境阻塞，`2` 表示参数或配置读取错误。端口已监听仅报告警告，需核对服务归属及复用配置。自检不构建、不启动服务或容器、不安装依赖、不修改 PATH，也不读取连接密码。SDK/浏览器文件存在不证明构建、浏览器启动或业务流程通过；后端范围检查 Docker，但不证明镜像、数据库及所有专项工具已经就绪。

若工具路径指向 Codex 附带运行时，而版本与项目不符，在当前终端选择机器上已安装的匹配版本；不要通过升级仓库版本满足错误的 PATH。恢复依赖时使用正确的 pnpm 和 `--frozen-lockfile`。自检仅识别问题，不执行修复。

## 文档来源

Microsoft Learn MCP 使用官方端点 `https://learn.microsoft.com/api/mcp`。个人 Codex 配置可通过以下命令注册并检查：

```powershell
codex mcp add microsoft-learn --url https://learn.microsoft.com/api/mcp
codex mcp get microsoft-learn --json
```

修改 MCP 配置后，在新的 Codex 会话验证工具发现及一次文档查询；配置存在不等于当前会话已加载。查询 .NET、ASP.NET Core、Microsoft.Data.SqlClient 或 SQL Server 时优先使用可用的 Microsoft Learn MCP，并结合仓库版本。Vue 等第三方库可使用已连接的 Context7；不可用时读取官方文档，不宣称未执行的查询成功。查询不携带仓库私有代码、凭据或业务数据。

## 故障排查适配

个人技能 `$debug-troubleshoot` 进入此仓库时使用本节。先读取 `AGENTS.md` 与 `rules/README.md`；测试时机、基线/快照、资源互斥和影响集由 [测试与验证](../../rules/development-quality.md#11-测试与验证) 决定，不建立第二套门禁。

| 故障位置 | 证据与入口 |
| --- | --- |
| 工具、依赖、启动失败 | 先按范围执行 doctor，区分 PATH、版本、依赖链接、SDK 与 Docker 就绪问题 |
| Vue 页面／组件 | `ui/admin` 的组件测试、类型检查及相关页面；通过 package.json 选择已有命令 |
| HTTP／鉴权／租户 | API 请求、ProblemDetails、可信租户与服务端日志；相关 Integration 按影响集选择 |
| SQL／迁移 | 使用[数据库诊断流程](../operations/database-diagnostics.md)：先检查独立连接，再按症状选择结构、索引或阻塞；对照 Dapper SQL、DbUp 迁移，按数据规则覆盖 SQL Server/MySQL |
| Worker／Outbox | Worker 日志、租约与状态转换、对应测试；不将浏览器成功替代后台处理证据 |
| 性能 | 转入 `.agents/skills/fullnet-performance-hardening/SKILL.md`，以实测区分瓶颈 |

新增或扩展业务能力使用既有 `fullnet-module-delivery`。只要求诊断时不修改代码；要求修复时完成相应回归。环境失败不算业务缺陷证据，修复环境后只重跑原受阻范围。PostgreSQL/Oracle 已列入[新增 Provider 支持矩阵](../operations/database-diagnostics.md#postgresqloracle-新-provider-支持矩阵)，当前状态为规划中；技术栈列举不自动代表运行时已支持，也不在 Vue 中引入 jQuery。

## 前端验收适配

个人技能 `$frontend-acceptance` 使用本节及 [客户端规则](../../rules/client-frontend.md)。活动后台为 `ui/admin`，Layui 冻结；普通验收不扩展为 Layui 对等测试。入口和覆盖门槛以 `package.json`、`eng/testing/test-matrix.json` 与配置文件为准。

- 环境检查选择 `frontend`；真实后端验收选择 `full`。doctor 通过后仍按任务运行相关检查。
- Mock 页面交互位于 `tests/e2e/admin-parity`；真实 API/Worker/数据库链路位于 `tests/e2e/admin-real-stack`。报告必须分别说明，两者不能相互替代。
- 通过已有 Playwright 的文件、项目和 grep 过滤选择本次相关路径；先检查 `--list`，防止零发现或误选冻结客户端，不默认运行全站或全量真实栈。
- 端口复用、认证准备及 teardown 使用现有配置和脚本，禁止仅因端口占用杀掉其他任务的服务。真实栈遵循仓库资源锁与测试数据边界。
- 核对相关桌面／移动视口、加载／空／错误状态、键盘交互及现有 axe 检查；可访问性失败不通过禁用规则或跳过测试掩盖。
- 本地 Playwright 临时产物使用独立系统临时目录（`--output`）；成功后清理本次产物，失败仅保留最小诊断证据。不得覆盖其他会话的输出或复制整仓。
- 只报告本次实际通过的范围；Mock 通过、配置存在、构建成功都不表示真实栈或完整页面已验收。测试未请求修复时先报告缺陷。
