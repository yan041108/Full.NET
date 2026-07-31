# Platform Overview 检查会话真实栈探针

- 建立日期：2026-07-29
- 状态：Build-verified

## 范围

1. 受限查看者经 UI 点击「检查会话」，真实 `/api/v1/me` 成功并展示连接用户。
2. Host 管理员探针：先经真实 API 获取 `authorization.permission_denied` ProblemDetails（含 traceId），再在探针点击时注入同一份响应，验证双端 `error-code` / `trace-id` 呈现。

## 验证

`host-overview-probe.spec.mjs`：**4/4**（2 唯一场景 × Vue/Layui）。
