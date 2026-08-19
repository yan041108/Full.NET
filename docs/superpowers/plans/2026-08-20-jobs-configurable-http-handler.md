# Jobs 可配置 HTTP 任务实施计划

> **For agentic workers:** Use `fullnet-module-delivery`. RED first. Do not mark Verified until real-stack or equivalent E2E evidence exists.

**Goal：** 交付可配置 HTTP 任务定义（method / headers / Settings 密钥引用），Worker 可执行；保留 `ping` 兼容；计划层复用现有 Cron。

**Spec：** [`2026-08-20-jobs-configurable-http-handler-design.md`](../specs/2026-08-20-jobs-configurable-http-handler-design.md)（**Approved for implementation**）

**基线：** `main` @ `410f64dd`

**快照建议：**

- `jobs-http-handler-settings-secret-20260820`
- `jobs-http-handler-core-20260820`
- `jobs-http-handler-api-vue-20260820`

**停止条件：**

- Jobs 直读 Settings 表或引用 Settings 实现程序集
- `ArgsJson` / 执行错误原文出现敏感 Header 明文
- Production 允许私网 SSRF 开关为 true
- 引入动态脚本 / 程序集加载
- 本波实现 HTTP Body

---

### Task 0: Settings `secret` ValueKind + 只读 Port

- [ ] RED：Architecture 测试锁定「仅 Contracts Port」；Unit/Integration：secret 创建后读 API 脱敏、Port 可解析明文
- [ ] `ConfigValueKinds.Secret = "secret"` 进入 Contracts + 枚举目录
- [ ] Host 配置写路径支持 secret；列表/详情对非特权读路径返回掩码或空 Value + `hasValue`
- [ ] 新增 Settings.Contracts Port：`ResolveSecretValueAsync(configKey)`（HostOnly、仅 secret、仅启用）
- [ ] Jobs 模块只引用 Settings.Contracts；Composition 接线
- [ ] 验证：`pnpm test:inner -- --snapshot jobs-http-handler-settings-secret-20260820`（或任务基线）

### Task 1: 定义存储 + 执行器核心

- [ ] RED：Args 校验（method 集合、敏感头分流、无 Body）；SSRF 用例；ping 回归
- [ ] 迁移 `098_JobsDefinitionHandlerKindAndArgs`（SqlServer + MySQL）；默认 `HandlerKind=ping`，`ArgsJson=NULL`
- [ ] `JobExecutionContext` + 按 `HandlerKind` 解析执行器；删除 JobKey→Handler 创建校验
- [ ] `HttpJobExecutor`：method/headers/secretHeaders/timeout/successStatusCodes；`HttpClient` 不跟随危险重定向
- [ ] 健康查询改为注册 Kind 列表
- [ ] 验证：双库 Integration 最小执行路径（可用 TestServer / 环回仅在 `AllowPrivateNetwork` 测试配置下）

### Task 2: API + client-contracts + OpenAPI

- [ ] RED：创建 http 定义契约；明文 Authorization 被拒；响应脱敏
- [ ] `Create/Update/Response` 增加 `handlerKind` + `args`
- [ ] OpenAPI `jobs-host-definitions-v1.json` + client-contracts guards/Vitest
- [ ] 旧客户端缺省 `handlerKind`：拒绝并返回明确校验错误（避免静默当成 ping）

### Task 3: Vue 管理端

- [ ] 任务定义表单：Kind 切换；HTTP URL/Method/Headers/SecretHeaders(ConfigKey)
- [ ] JobKey 可输入（创建）；编辑仍不可改 JobKey
- [ ] 计划页定义选项展示 Kind；执行历史失败摘要无密钥
- [ ] i18n zh-CN / en-US
- [ ] 验证：admin 相关 Vitest + 必要 parity/real-stack 选择器（inner/slice 影响集）

### Task 4: Closeout

- [ ] `docs/verification/jobs-configurable-http-handler-2026-08-20.md`
- [ ] 更新 `docs/roadmap/adminnet-feature-parity.md` Jobs 行（Build-verified，不写 Verified）
- [ ] 权限库存若新增 Settings secret 行为则更新；`eng/testing/test-matrix.json` 仅改权威门槛
- [ ] `git diff --check`；规则演进一行结论

---

## 验收样例（合并前至少一条）

1. Settings 创建 `secret` 键 `jobs.http.secrets.demo_bearer`
2. Jobs 创建定义：`HandlerKind=http`，`GET https://…`，`secretHeaders.Authorization → configKey`
3. 手动触发 → 执行历史 `succeeded`
4. 将 URL 改为 `http://127.0.0.1/`（Production 配置）→ 执行 `failed` 且无出站
