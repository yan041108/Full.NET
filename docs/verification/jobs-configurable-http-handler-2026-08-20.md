# Jobs 可配置 HTTP 任务验证记录

**日期：** 2026-08-20  
**状态：** Build-verified（非 Verified）  
**Spec：** [2026-08-20-jobs-configurable-http-handler-design.md](../superpowers/specs/2026-08-20-jobs-configurable-http-handler-design.md)

## 已验证（构建 / 单测 / 集成夹具）

- 迁移 `098_JobsDefinitionHandlerKindAndArgs`（SqlServer + MySQL）幂等恢复测试
- `HandlerKind` + `ArgsJson` 持久化；`ping` / `http` 执行器按 Kind 解析
- `HttpJobArgsValidator`：敏感 Header 分流、URL 约束
- `HttpSsrfGuard`：环回/私网/元数据地址拦截（单测）
- `JobsSettingsBoundaryTests`：Jobs 仅引用 `Settings.Contracts`（精确程序集引用检查）
- `SettingsSecretValueResolver`：Host secret 明文解析 Port
- Settings `secret` ValueKind + `hasValue` 读 API 脱敏
- 集成：HTTP 定义创建、secretHeaders、手动触发成功；私网 URL 执行失败
- Vue：`HostJobsView` Kind 切换、HTTP URL/方法/headers/secretHeaders 编辑；计划页定义选项含 Kind
- client-contracts / OpenAPI 契约更新（Vitest 通过）

## 未验证（需真实栈 E2E）

- 完整 Admin 人工流程：创建 HTTP 定义 → Cron 调度 → 生产环境 SSRF 门禁
- Production 强制 `Jobs:Http:AllowPrivateNetwork=false` 运维配置审计

## 本地命令

```bash
dotnet build src/Modules/Full.NET.Modules.Jobs/Full.NET.Modules.Jobs.csproj
dotnet test tests/Full.NET.UnitTests --filter "FullyQualifiedName~HttpJobArgsValidatorTests|FullyQualifiedName~SettingsSecretValueResolverTests|FullyQualifiedName~HttpSsrfGuardTests"
dotnet test tests/Full.NET.ArchitectureTests --filter "FullyQualifiedName~JobsSettingsBoundaryTests"
pnpm --dir packages/client-contracts test -- --run host-jobs settings-config-entries
```
