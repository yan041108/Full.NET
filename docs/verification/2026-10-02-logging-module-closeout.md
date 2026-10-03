# 日志模块本期收尾验收

代码基线为 `c609759576c4bf54ff9e743161ecb829ca8d4d4a` 加累计日志变更；用户批准的全项目本地验收规则已单独提交 `91176bfd`。本轮补 Vue 真实栈与低视口布局，核对当前说明、历史任务和交付范围。

环境为 Windows / Docker Desktop 29.6.2、.NET SDK 10.0.401、Edge 149.0.4022.69；i7-12700H，20 个逻辑处理器，约 63.75 GiB 内存。SQL Server 固定 CU14 与 MySQL 8.0 测试容器、真实 Migrator/Development Overlay、API、Worker、Redis、Vue Vite 及 Edge；浏览器通过实际登录、导航和 HTTP 接口验证，不模拟接口返回。

## 浏览器验证

详情夹具仅允许本地 Development 状态、Testcontainers 标记与固定数据库镜像，建立应用生成 UUID v7 的 23 条独立操作摘要，按本次 ID 清理。它不修改生产播种或全局采集开关；真实读路径与数据库映射接受有效、已到期及历史无详情三类数据。

- 有权限时，日志消息、请求参数、返回内容三个页签可用；进入日志消息时不读取受限详情，两个受限页签共用一次按需获取。
- 请求网络上下文与固定安全请求/返回摘要显示正确；分页使用实际第二页请求，23 条筛选结果的第二页只有 3 条。
- 到期和历史无详情记录的详情 API 均返回 404；页面解释不可用并保留摘要，不显示原 IP。
- 经真实角色 API 撤销详情权限并重新登录后，新会话没有受限页签、不发起详情读取；直接请求详情 API 返回 403。此范围不等于旧会话内打开的抽屉实时撤权验证。
- 1280×720 下，操作、异常、访问日志表格至少保留 120px 可操作高度，分页位于表格之后。首次真实点击发现分页遮住操作详情按钮；修复三页的滚动、趋势/筛选收缩与表格最低高度后复测。

SQL Server 命令：`pnpm --filter @fullnet/admin-real-stack-e2e exec playwright test host-operation-log-details.spec.mjs --project vue-admin`，最终 4/4 通过、0 跳过，耗时 2.3 分钟（包含启动、迁移及清理）。本地输出 `.tmp/logging-closeout-browser-sqlserver.log`。

MySQL 命令：设置 `FULLNET_E2E_DATABASE_PROVIDER=MySql` 后运行 `pnpm --filter @fullnet/admin-real-stack-e2e exec playwright test host-operation-log-details.spec.mjs host-access-logs.spec.mjs host-exception-logs.spec.mjs --project vue-admin`；最终 8/8 通过、0 跳过，耗时 3.2 分钟，包含详情四项和访问/异常页面与权限负例四项。命令结束后恢复原环境变量，本地输出 `.tmp/logging-closeout-browser-mysql.log`。两次最终运行均完成自有 API/Worker/数据库/Redis 清理。

首次完整 MySQL 回归的旧访问测试以全局总数恒定判断“没有重复”，实际从 25 增至 27。源码确认 Development 显式 `AccessLogCapture.Enabled=true`，查询自身也被采集，该断言不适用。修正后使用唯一 W3C TraceId、有限时间窗与路由筛选，等待本次请求恰有一条 GET/200/已认证记录；没有关闭采集或放宽为总数大于零，完整八项重新运行通过。

## 受影响回归与审查

| 命令 | 本轮实际结果 |
| --- | --- |
| `pnpm test:dotnet:unit -- --selection logging-delivery` | 318/318，0 跳过；构建 0 警告/错误 |
| 六个日志页面/详情组件的 `vitest run` | 23/23，六文件通过 |
| `pnpm --filter @fullnet/admin build` | 类型检查与 Vite 构建通过 |
| `pnpm test:bundle-budgets` | 初始、图表及可选设计器三个预算通过 |
| `pnpm test:helm` | 27/27 |
| `pnpm test:observability-deploy` | 16/16 |
| `pnpm test:integration:tooling` | 54/54 |
| `pnpm test:governance` | 57/57 |
| `pnpm test:performance-governance` | 14/14 |
| `pnpm --filter @fullnet/admin-real-stack-e2e test:provisioner` | 43/43 |

六个组件为 `OperationLogsView`、`AccessLogsView`、`ExceptionLogsView`、`OutboundCallLogsView`、`AuditLogDetailDrawer` 与 `ObservabilityElasticsearchHealthView`。真实栈保留既有 ECharts Legend 未注册控制台提示，本轮未修改图表公共组件。

独立审查覆盖新夹具/E2E、三页布局、核心安全封套、Kafka 出口及消费者确认路径。已修正测试捕获状态大小写、行定位、有效会话令牌与抽屉/可见页签定位；源码未发现新增 P1/P2 阻断项。Kafka/TLS/HTTPS ES、Offset/DLQ 以及 API 原生运行证据见[秘密跨出口回归](2026-10-01-logging-secret-boundary.md)，同负载请求 P99 和资源比较见[两路线比较](2026-10-01-logging-route-comparison.md)，本轮不扩大这些实测范围。

## 交付结论与后续边界

本期已按本地验收标准完成核销，可结束本期开发；报告随累计日志交付提交。Collector/ApplicationKafka 入口已按本地准入允许显式配置，默认仍为 Legacy；独立消费者部署仍需显式实验开关，不自动部署或切流。旧 ES 兼容包仍由 Hosting 引用，运行时开关默认关闭。

归档、旧兼容包移出核心、专用安全事件入口、更大容量/长时 Soak、全部节点/磁盘永久丢失及跨拓扑切流矩阵归入后续扩展。未完成的 1 万在途认证继续保留 `Capacity-not-verified`；本期完成不承诺全链零丢失、跨重启重放从请求入队开始或指定生产最大吞吐。
