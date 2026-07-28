# Auditing Contains Time Boundary Specification

## 1. 目标

为 Host 访问、操作和异常审计列表中的 `contains` 查询增加显式时间边界，阻止无时间范围的
前后通配扫描持续放大数据库成本，同时保持无 `contains` 的普通列表、详情接口、权限和
Host 数据范围兼容。

## 2. 公共 HTTP 契约

- 访问日志分页和游标接口的 `pathContains`、操作日志分页接口的 `pathContains`，以及
  异常日志分页接口的 `pathContains`/`exceptionTypeContains` 只要规范化后任一非空，
  请求就必须同时携带 `fromUtc` 与 `toUtc`。
- `fromUtc` 必须小于或等于 `toUtc`；闭区间长度不得超过服务端配置的最大窗口。
- 空白 `contains` 按未提供处理，不触发时间窗门禁。
- 无 `contains` 的请求继续允许省略一个或两个时间参数；现有普通列表行为不变。
- 服务端不得静默补齐、截断或扩大调用方给出的时间范围。
- 违反契约返回 HTTP 400 ProblemDetails，并使用以下稳定错误码：
  - `auditing.query.contains_time_range_required`：缺少 `fromUtc` 或 `toUtc`；
  - `auditing.query.time_range_invalid`：`fromUtc` 晚于 `toUtc`；
  - `auditing.query.contains_time_range_exceeded`：窗口超过配置上限。

## 3. 配置契约

- 稳定配置节为 `Auditing:Query`。
- `MaximumContainsWindowDays` 默认 `1`，允许范围 `1..31`。放宽生产值前必须复跑当前
  数据规模与 Provider 的最大窗口基准。
- API Host 在启动时执行配置校验；非法值必须启动失败，不能延迟到首次查询。
- 配置只控制服务端允许的最大窗口，不向客户端泄露数据库实现或自动改写查询。

## 4. 服务端实现

- 三类查询在规范化筛选后、打开数据库连接前复用同一时间窗策略。
- 访问日志分页与游标接口使用同一策略；游标筛选摘要继续包含规范化后的时间范围和
  `pathContains`，避免跨筛选复用。
- 保留现有参数化 SQL、SQL Server 查询形状缓存、MySQL 固定 Statement 和
  `SqlDataScope.HostOnly`。
- 本任务不新增索引、不引入动态 SQL、不优化深 OFFSET，也不把普通 B-tree 描述为
  前后通配 contains 的解决方案。

## 5. 客户端

- `packages/client-contracts` 发布访问日志查询筛选类型，显式包含 `fromUtc`、`toUtc` 和
  `pathContains`。
- Vue 与 Layui 访问日志页提供可见的路径 contains、开始时间和结束时间控件。
- 用户首次输入非空 contains 且时间范围尚未填写时，客户端默认填入“当前时刻向前 24 小时”
  的可见范围；用户可在服务端上限内调整。
- 客户端只负责便利默认值，服务端始终独立验证；清空 contains 后不得强制附加时间范围。
- 翻页或游标加载更多必须复用首批的同一规范化筛选，筛选变更后重新从首批开始。

## 6. 验收

1. 单元测试覆盖默认配置、配置边界、缺失范围、反向范围、超窗、合法最大窗口、普通列表和
   空白 contains。
2. SQL Server/MySQL 真实 API 都覆盖三类日志的缺失范围与超窗 400，并验证稳定错误码。
3. SQL Server/MySQL 验证合法 contains、无 contains 普通列表，以及访问日志游标跨批次。
4. OpenAPI 冻结 `fromUtc`、`toUtc`、各 contains 参数及 400 响应。
5. 共享客户端、Vue 和 Layui 测试覆盖筛选序列化、24 小时默认范围与加载更多筛选保持。
6. 复用 100,000 行 Audit benchmark，记录两库最大允许窗口的 P50/P95/P99 和执行计划；
   结果只作为后续专用搜索设施 Decision Gate 的证据，不作为生产 SLA。

## 7. 非目标

- 不删除现有 OFFSET 接口或精确 COUNT。
- 不要求所有普通审计列表都提供时间范围。
- 不自动纠正反向时间、不把超窗拆成多次查询、不静默截断结果。
- 不新增缓存、搜索引擎、物化表、Broker、CDC、数据库迁移或索引。
