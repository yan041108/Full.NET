# Jobs 可配置 HTTP 任务设计

**状态：** Approved for implementation  
**日期：** 2026-08-20  
**基线：** `main` @ `410f64dd`  
**适用范围：** Host Jobs 定义、Worker 执行、Settings Host 配置密钥引用、Vue 管理端  
**Admin.NET 映射：** 对标「可配置 HTTP 任务 + Trigger/Cron」能力；**不对标** 动态 C#、脚本任务、运行时程序集加载

## 1. 决策摘要

当前 Full.NET Jobs 将 `JobKey` 与编译期 `IJobHandler` 1:1 绑定，管理端只能创建已注册键（如 `jobs.ping`），无法表达「按 Cron 访问某 URL」类运营任务。

本设计将执行模型升级为：

- **`HandlerKind`**：稳定机器码，选择内置执行器（`ping` / `http`）
- **`ArgsJson`**：执行参数（URL、HTTP method、普通 Header、敏感 Header 的密钥引用）
- **`JobKey`**：定义级唯一业务键，**不再**要求等于某个 C# Handler 类名；由创建时校验唯一性与格式

第一版交付可配置 **HTTP 任务**（含 method / header），并保留既有 `ping` 兼容路径。计划（Cron / 一次性 / 手动）继续使用现有 `JobSchedule`，不改调度真源。

## 2. 目标与非目标

### 纳入

1. 创建/更新 Host 任务定义时选择 `HandlerKind=http`，配置：
   - `url`（必填）
   - `method`（必填；允许集合见 §4）
   - `headers`（可选；普通明文 Header）
   - `secretHeaders`（可选；敏感 Header 仅存 Settings 配置键引用）
2. Worker 执行时解析 Args，发起 HTTP 请求；按状态码与传输错误写入执行成败
3. Vue：创建表单按 Kind 切换字段；计划页仍选择定义
4. Settings：Host 配置提供密钥类条目 + **最小只读 Port** 供 Jobs Worker 解析引用（跨模块禁止直读 Settings 表）

### 明确拒绝（本 Spec）

- 动态 C# / 脚本 / 任意程序集 / `JobType` 反射加载
- 请求 Body（含 JSON/form）— 可后续独立 Spec
- 任意重定向跟随到内网（见 SSRF）
- 在 `ArgsJson` 或执行历史中持久化敏感 Header 明文
- 租户作用域 Jobs（本波仍为 Host）

## 3. 字段与兼容

### 3.1 `fn_jobs_definition` 新增列

| 列 | 类型 | 默认 | 说明 |
| --- | --- | --- | --- |
| `HandlerKind` | 字符串稳定机器码 | `'ping'` | `ping` \| `http` |
| `ArgsJson` | 可空长文本 / `nvarchar(max)` / MySQL 等价 | `NULL` | `ping` 必须为 `NULL`；`http` 必须为合法 JSON 对象 |

既有行：`HandlerKind='ping'`，`ArgsJson=NULL`，`JobKey='jobs.ping'` 行为不变。

### 3.2 JobKey 语义

| 项 | 规则 |
| --- | --- |
| 唯一性 | Host 作用域 `JobKey` 唯一（现有约束保留） |
| 格式 | 沿用现有正则（小写段 + 点分），禁止空格 |
| 与 Handler | **不再**要求 `JobHandlerRegistry.TryGetHandler(jobKey)` |
| 建议 | `http` 定义可用业务键如 `ops.site_health`；系统不强制生成 |

创建校验改为：`HandlerKind` ∈ 允许集合；按 Kind 校验 `ArgsJson`；`JobKey` 格式 + 唯一。

## 4. HTTP Args 契约

稳定 JSON 形状（camelCase，与 API JSON 一致）：

```json
{
  "url": "https://example.com/health",
  "method": "GET",
  "headers": {
    "Accept": "application/json",
    "X-Trace-Source": "fullnet-jobs"
  },
  "secretHeaders": {
    "Authorization": {
      "configKey": "jobs.http.secrets.example_bearer"
    }
  },
  "timeoutSeconds": 30,
  "successStatusCodes": [200, 204]
}
```

| 字段 | 约束 |
| --- | --- |
| `url` | 必填；仅 `http`/`https`；长度上限 2048；禁止用户信息嵌入 URL（`user:pass@host`） |
| `method` | 必填；允许：`GET`、`HEAD`、`POST`、`PUT`、`PATCH`、`DELETE`（第一版即支持；**Body 仍禁止**，故 `POST`/`PUT`/`PATCH` 仅允许无 Body） |
| `headers` | 可选；键名 ASCII token；值长度单条 ≤ 1024；总条目 ≤ 32 |
| `secretHeaders` | 可选；键名规则同 headers；值必须为 `{ "configKey": "<Host ConfigKey>" }`；条目 ≤ 16 |
| `timeoutSeconds` | 可选；默认 30；范围 1–120 |
| `successStatusCodes` | 可选；默认 `[200,201,202,204]`；元素 ∈ 100–599；长度 ≤ 16 |

### 4.1 敏感 Header 判定

下列 Header 名（大小写不敏感）**禁止**出现在明文 `headers`，**必须**走 `secretHeaders`（若需要）：

- `Authorization`
- `Proxy-Authorization`
- `Cookie`
- `Set-Cookie`
- `X-Api-Key`
- `Api-Key`

未知名默认可进明文 `headers`；运维可将自定义密钥名放入 `secretHeaders`。

### 4.2 密钥引用（Settings）

- 引用目标：Host 作用域配置条目的稳定 `ConfigKey`
- Settings 新增或复用 **`secret` ValueKind**（若尚无）：写入可存明文于受保护库列，**读 API 对管理端脱敏**（仅返回已设置标记 / 掩码）
- Jobs **禁止** SQL 读取 Settings 表；必须经 Settings 公开 Contract Port，例如：

```csharp
// 示意：Settings.Contracts
Task<Result<string>> ResolveSecretValueAsync(string configKey, CancellationToken ct);
```

- Port 行为：
  - 仅 Host；键不存在 / 非 `secret` / 未启用 → 失败，执行记 `failed`
  - 成功返回明文仅用于当次请求内存；禁止写入 Jobs 日志、执行错误原文、Outbox
- 解析时机：每次执行领取后、发起 HTTP 前；配置轮换后下次执行自动生效

## 5. 执行模型

### 5.1 Handler 解析

废弃「按 JobKey 找 Handler」。改为：

```text
HandlerKind → IJobHandlerExecutor
ArgsJson + JobExecutionContext → Execute
```

| HandlerKind | 执行器 | Args |
| --- | --- | --- |
| `ping` | `PingJobExecutor` | 必须无 Args |
| `http` | `HttpJobExecutor` | 必须符合 §4 |

健康页「已注册 Handler」改为展示 **已注册 HandlerKind 列表**（或 Kind + 说明），避免继续暗示 JobKey 白名单。

### 5.2 `IJobHandler` 演进

引入带上下文的执行接口（名称可在实现时微调，语义冻结）：

```csharp
Task ExecuteAsync(JobExecutionContext context, CancellationToken cancellationToken);
```

`JobExecutionContext` 至少包含：`ExecutionId`、`JobDefinitionId`、`JobKey`、`HandlerKind`、`ArgsJson`、`TriggerKind`。

旧无参 `ExecuteAsync(CancellationToken)` 仅作为适配层过渡时允许，合并前必须删除。

### 5.3 HTTP 执行语义

1. 校验 URL / method / headers / secretHeaders
2. SSRF 检查（§6）
3. 解析 `secretHeaders` → 合并到请求 Header（同名冲突：secret 覆盖明文）
4. 使用 typed `HttpClient`（禁用自动解压到无界内存外的策略按实现；响应体最多读取 N 字节用于错误摘要，默认 2 KiB）
5. **不跟随重定向到失败的 SSRF 目标**；建议 `MaxAutomaticRedirections=0` 或逐跳校验
6. 状态码 ∈ `successStatusCodes` → `succeeded`；否则 `failed`，`ErrorMessage` 仅含稳定摘要（方法、主机、状态码、截断 reason），**禁止**回显 Authorization 等值
7. 传输异常 / 超时 → `failed`

## 6. SSRF 与网络安全

必须在执行前拒绝（记失败，不发出请求）：

1. 非 `http`/`https`
2. Host 为字面量环回 / 未指定 / 链路本地（含 IPv6 对应形态）
3. DNS 解析后任一地址属于私网、环回、链路本地、元数据常见段（含 `169.254.169.254`）
4. 显式配置的阻止主机后缀列表（可配置，默认含 `.internal`、`.local` 可在实现计划中定默认）

允许列表模式不在本 Spec；默认拒绝私网。

开发/测试可用 Settings 或环境开关 `Jobs:Http:AllowPrivateNetwork=true` **仅非 Production**，Production 强制关闭。

## 7. API / 权限 / 审计

- 无强制新权限码；沿用 `jobs.definitions.create/update/read`
- 创建/更新请求增加 `handlerKind`、`args`（对象）或 `argsJson`
- 读响应返回 `handlerKind` 与 **脱敏后的 args**：`secretHeaders` 只回显 `configKey`，不回显密钥值
- 操作审计：记录定义 Id、JobKey、HandlerKind、URL 主机名（非完整 query 若含敏感信息则只记 host+path 策略在实现中保守截断）

Settings 密钥条目的创建/更新仍走 Settings 既有权限；Jobs UI 可深链到系统配置，但不在 Jobs 内嵌密钥明文编辑器（第一版）。

## 8. Vue 管理端

1. **任务定义**创建/编辑：
   - 选择 HandlerKind：`探针(ping)` / `HTTP`
   - `HTTP`：URL、Method 下拉、Header 表（名/值）、敏感 Header 表（名 / ConfigKey 选择或手输）
   - 取消「仅白名单 JobKey 下拉」；JobKey 改为可输入（创建后仍不可改，与现行为一致则保持不可改）
2. **任务计划**：定义下拉展示 `displayName` + `jobKey` + kind 徽标
3. **执行历史**：失败摘要可见；无密钥泄漏
4. **集群健康**：注册 Kind 列表

## 9. 验证门禁

1. Unit：Args 校验、敏感 Header 分流、SSRF 拒绝用例
2. Architecture：Jobs 不引用 Settings 实现项目；仅 Contracts Port
3. Integration（双库）：
   - 创建 `http` 定义 + Cron/手动触发，对测试 HTTP 服务器 `GET/POST(无 body)` 成功
   - 明文放入 `Authorization` 被 API 拒绝
   - `secretHeaders` 引用 Settings secret 后请求带正确头
   - 私网 URL 执行失败且无出站
4. Vue / client-contracts：创建表单与脱敏往返
5. 不标记 Verified，直至真实栈或等价 E2E 至少覆盖「创建 HTTP 定义 + 手动触发成功」一条路径

## 10. 迁移与回滚

- 新迁移（建议 `098_JobsDefinitionHandlerKindAndArgs`）双库同时交付
- 回滚：应用层停止写入新 Kind；列可保留（向前兼容）
- 旧客户端未传 `handlerKind` 时：服务端默认 `ping` 且要求 `jobKey` 仍能解析为 ping 兼容路径，或拒绝并提示升级（实施计划选定一种并测）

## 11. 被替代关系

- 部分取代「Jobs 仅 JobKey 白名单」产品假设；不取代 overlap / schedule / health 既有 Spec
- 明确废止「未来才做 method/header」的临时说法；method/header 为本 Spec 第一版范围
- Body、mTLS、OAuth 客户端凭证流：后续独立 Spec
