# Auditing Access Log Cursor Pagination Specification

## 1. 目标

为 Host 访问日志新增显式游标分页，消除深页 OFFSET 和每页精确 COUNT 的固定成本，同时
保持现有分页接口、权限、Host 数据范围、筛选语义和详情接口兼容。

## 2. 公共 HTTP 契约

- 保留 `GET /api/v1/auditing/access-logs` 及现有 `PagedResult` 响应，不改变既有客户端。
- 新增 `GET /api/v1/auditing/access-logs/cursor`。
- 查询参数：
  - `limit`：默认 20，服务端限制为 1～100；
  - `cursor`：可选不透明字符串；缺省表示第一批；
  - `fromUtc`、`toUtc`、`httpMethod`、`statusCode`、`pathContains`：与现有接口使用相同
    的规范化和筛选语义。
- 成功响应 `AccessLogCursorPageResponse`：
  - `items: AccessLogResponse[]`；
  - `nextCursor: string | null`；
  - `hasMore: boolean`。
- 无效、版本未知或与当前规范化筛选不匹配的游标返回 HTTP 400 ProblemDetails，稳定错误码
  为 `auditing.access_log.cursor_invalid`。
- 继续使用 `auditing.access.read` 权限；游标不是授权令牌，不能扩大 Host 数据范围。

## 3. 游标语义

- 稳定排序固定为 `(OccurredAtUtc DESC, Id DESC)`。
- 下一批边界固定为：

```sql
OccurredAtUtc < @CursorOccurredAtUtc
OR (OccurredAtUtc = @CursorOccurredAtUtc AND Id < @CursorId)
```

- 游标使用版本化 Base64Url 二进制载荷，包含版本、UTC ticks、UUID 网络字节序和当前
  规范化筛选的 SHA-256 摘要。
- 筛选摘要用于阻止客户端无意中把游标复用于另一组筛选；它不是认证签名。权限与
  `SqlDataScope.HostOnly` 始终是安全边界。
- 读取 `limit + 1` 行判断 `hasMore`，向客户端只返回前 `limit` 行；有后续时以最后
  一条已返回记录生成 `nextCursor`。
- 并发新增的更晚记录不会插入已开始的向后遍历；删除记录允许后续批次缩短，但不得重复
  已返回 ID。

## 4. 数据库实现

- SQL Server 使用按现有五个可选筛选位缓存的固定参数化 SQL 形状；第一批和带游标批次
  使用独立 Statement，禁止运行时拼接用户 SQL。
- MySQL 使用第一批和带游标批次两个固定参数化 Statement；不使用 `FORCE INDEX`。
- 两个 Provider 都只执行一次列表查询，不执行 COUNT，不创建迁移或新索引。
- SQL Statement Scope 固定为 `HostOnly`，Statement Name 使用稳定低基数值。
- SQL Server `uniqueidentifier` 与 MySQL `BINARY(16)` 都由数据库按与 ORDER BY 相同
  的 Provider 原生比较语义处理；C# 不自行比较 UUID 大小。

## 5. 客户端

- `packages/client-contracts` 新增游标页类型和运行时守卫，不修改现有分页类型。
- Vue 与 Layui 访问日志页首屏改用游标接口，并提供“加载更多”；追加时按服务端顺序
  保持现有记录，不在客户端重新排序。
- 两端继续使用统一 ProblemDetails 处理和 `accessLogs.loadMore` 多语言键。
- 旧 `listAuditingAccessLogs(page, pageSize)` 客户端函数保留；新增游标函数供新页面使用。

## 6. 验收

1. 游标编码/解码、版本、畸形输入和筛选不匹配有 RED/GREEN 单元测试。
2. SQL Server/MySQL Statement 均参数化、`HostOnly`、无 OFFSET、无 COUNT，并严格使用
   二元 keyset。
3. 服务单次查询 `limit + 1`，正确返回 `hasMore/nextCursor`，第二批不重复第一批 ID。
4. OpenAPI 冻结夹具、源生成 JSON、客户端契约和 Vue/Layui 流程同步。
5. SQL Server/MySQL 真实 API 均验证权限、第一批、下一批、无重复、相同时间戳跨页边界和无效游标 400。
6. 保留现有 OFFSET API 的单元、兼容和集成行为。
7. 独立性能验证比较 100,000 行深 OFFSET 与等价 keyset 页的 P50/P95/P99 和执行计划；
   未得到双库证据前不宣称生产固定收益。

## 7. 非目标

- 不删除或重定向旧 OFFSET API。
- 不为游标响应提供精确总数或页码。
- 不把 contains 搜索包装成已解决；无界 contains 仍服从时间范围或专用搜索设施门禁。
- 不新增迁移、索引、缓存、Broker 或后台物化任务。
