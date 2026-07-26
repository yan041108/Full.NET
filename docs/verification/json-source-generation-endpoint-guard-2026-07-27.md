# System.Text.Json Endpoint 源生成守卫验证

## 范围

- 复用现有 Architecture 序列化测试，不增加测试总数。
- 启动真实 API 模块映射并读取 `/api/v1/*` Endpoint 的请求、响应元数据。
- 要求 Full.NET 自有 HTTP 契约至少被一个已注册的 `JsonSerializerContext` 覆盖。
- 不修改业务模块、数据库、Realtime、日志或客户端实现。

## 测试先行证据

1. Architecture 基线为 49/49。
2. 首次扫描当前生产 Endpoint 时直接通过，表明当前契约不存在存量遗漏。
3. 加入测试程序集专用的未注册 Request/Response 探针后，测试按预期失败，并准确列出两个探针契约。
4. 守卫最终精确断言探针必须被发现，再要求剔除探针后的生产遗漏数为零。

该探针同时防止扫描范围、Endpoint 元数据读取或源生成 Context 识别逻辑退化为空检查。

## 验证结果

| 验证项 | 结果 |
| --- | --- |
| 聚焦源生成守卫 | 1/1 通过 |
| Architecture | 49/49 通过 |
| Governance | 11/11 通过 |
| 项目 Skill 测试 | 52 项契约检查通过 |
| Workspace 校验 | 通过 |
| `git diff --check` | 通过 |

本变更不增加 Architecture 测试数量，因此门槛继续保持 49。
