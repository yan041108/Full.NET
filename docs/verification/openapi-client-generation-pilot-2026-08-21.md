# OpenAPI 驱动客户端生成三类试点验证（2026-08-21）

- 决策：`Pilot-passed`
- 能力状态：`Build-verified`（仅三类试点；完整批量迁移仍未开始）
- 解除分支基线：`main` @ `37a5379d`
- 比较基线：`6b7b8bd57bee9622b17f945691190ab239524338`
- 任务快照：`openapi-client-pilot-unblock-20260821`
- 适用决策：[`ADR-0007`](../architecture/adr/ADR-0007-openapi-driven-client-generation-boundary.md)

## 结论

第一阶段严格只实施三类代表性试点：Identity Host Users、Files Host Files、Settings Host Config Entries。标准 OpenAPI 快照、确定性仓库内生成器、运行时守卫、共享 HTTP Runtime、Vue 薄适配层和 CRUD OpenAPI 收敛均已落地；29 个 Operation 已进入生成清单并在解除门禁后全部提升为 `generated`。

首轮验证曾因 Vue 全量 Unit、Vue Build 与客户端 High advisory 判定 `Pilot-stopped`。解除窗口在 `37a5379d` 修复上述阻断并完整重跑 ADR-0007 第 5 节门禁后，全部通过，因此判定 `Pilot-passed`。仍禁止未经新计划迁移第四个模块，也不得修改 `ui/admin-layui`。

## 实施范围与提交

| 提交 | 内容 |
| --- | --- |
| `006c8374` | 建立客户端生成就绪失败关闭门禁 |
| `99b97688` | 固定三类试点的 operationId、主 Tag 与运行时元数据 |
| `77625154` | 冻结规范化标准 OpenAPI 快照与精确试点清单 |
| `1fc794b5` | 选择并实现确定性仓库内客户端生成器 |
| `0e578e59` | 生成三个试点并收缩 Vue API 为薄适配层 |
| `44b4cd41` | 让 CRUD 代码生成器产出标准 OpenAPI 并复用同一客户端链路 |
| `a3b27255` | 记录首轮 `Pilot-stopped` 证据 |
| （解除窗口） | 修复 Vue Unit/Build 基线、解除 `nanoid` High advisory，并将 29 条清单提升为 `generated` |

清单只包含以下三组，共 29 个 Operation（状态均为 `generated`）：

| 试点 | Operation 数 | 代表语义 |
| --- | ---: | --- |
| `identity-host-users` | 13 | JSON、分页、Path/Query/Body、创建与状态动作 |
| `files-host-files` | 5 | multipart 上传、Blob 下载 |
| `settings-host-config-entries` | 11 | JSON、批量动作、`204 No Content` |

未修改 `ui/admin-layui`，未迁移非试点 Vue API 模块。

## 工具选择与生成成本

验证环境：Windows NT 10.0.19045.0、Node.js 24.12.0、pnpm 10.26.0、.NET SDK 10.0.400。

OpenAPI Generator 候选固定为 Generator 7.24.0 与 `@openapitools/openapi-generator-cli` 2.40.1。实际环境 Java 8 仅支持 class file 52，而候选 JAR 要求 55，并依赖外部 JAR 下载；按 ADR-0007 工具停止门禁拒绝该候选。最终选择零新增依赖的仓库内 Node.js 生成器 `scripts/openapi/generate-fullnet-client.mjs`，没有把第三方 Runtime、Configuration 或 Class 暴露给 Vue。

| 指标 | 结果 |
| --- | ---: |
| 生成文件 | 4 |
| 生成行数 | 1,196 |
| 生成字节 | 49,842 |
| `--check` 实测耗时 | 536 ms |
| 连续生成漂移 | 0 |
| 新增生成器运行时依赖 | 0 |

生成物只包含 models、guards、operations 和公开入口。JSON Operation 使用 `unknown → generated guard → DTO`；Blob 使用共享 `requestBlob`；Void/`204` 不解析 JSON；所有 Operation 注入既有 `HttpClient`。

生产依赖许可清单命令成功，共识别 372 个 package entry、13 类许可证表达式；本次没有因生成器新增生产依赖，`THIRD-PARTY-NOTICES` 无需变更。

## 解除阻断修复

| 阻断 | 修复 |
| --- | --- |
| Vue Unit 11 fail | Job Schedules / Menus 测试夹具对齐运行时守卫；`CodeGenerationPreviewsView` / `CodeGenerationTemplatesView` 补齐 `vue-router` mock |
| Vue Build | `CodeGenerationTemplatesView.vue` 收窄 `CodeGenerationPreviewRequest` 联合类型，capabilities/relationships 仅作用于 modern 分支 |
| `GHSA-2v37-7h3g-55p8` | 根 `package.json` `pnpm.overrides` 将传递依赖 `nanoid` 固定为 `3.3.18`；`pnpm audit:clients` 退出 0 |

## 新鲜验证证据（解除后完整重跑）

| 命令 | 结果 |
| --- | --- |
| `pnpm openapi:client:generate -- --check` | 退出码 0，生成产物零漂移 |
| `pnpm test:openapi` | 108/108，通过；含生成清单 `pilot→generated` 晋升兼容门禁 |
| `pnpm --filter @fullnet/client-contracts test` | 137/137，通过 |
| `pnpm --filter @fullnet/client-contracts build` | 退出码 0 |
| `pnpm --filter @fullnet/admin exec vitest run src/api/users.test.ts src/api/host-files.test.ts src/api/config-entries.test.ts` | 16/16，通过 |
| `pnpm --filter @fullnet/admin test` | 125 文件 / 455 项，全部通过 |
| `pnpm --filter @fullnet/admin build` | 退出码 0（vue-tsc + Vite） |
| `pnpm audit:clients` | 退出码 0 |
| `pnpm test:naming` | 30/30，通过 |
| `pnpm test:governance` | 38/38，通过 |
| `dotnet build Full.NET.slnx -c Release` | 退出码 0，0 warning、0 error |
| `pnpm test:integration:affected -- --base 6b7b8bd57bee9622b17f945691190ab239524338 --phase merge` | 87/87，通过，SQL Server/MySQL 双 Provider，覆盖三个试点、客户端快照、CodeGeneration 与 Smoke |

双库 Integration 的客户端规范快照逐字节一致，并覆盖 JSON、multipart、Blob、`204`、ProblemDetails、鉴权和稳定 Operation 身份。共享 `client-contracts` 测试继续覆盖 Refresh、Cookie、语言、ProblemDetails、取消和不可信 JSON 守卫语义；三个 Vue 业务适配文件不再声明后端 DTO 或拼接试点路径。

## 通过后的边界

- `contracts/openapi/client-generation-manifest-v1.json` 的 29 个条目已改为 `generated`。
- 完整生成式 SDK / 其余 Vue API 模块仍需独立迁移计划；不得在本验证外批量改写。
- 允许按计划 Task 8 规则新建“单模块迁移”计划，但仍须每个 slice 只迁移一个模块并重复门禁。
- 不修改 `operationId`、主 Tag、路径或序列化契约；不得删除运行时 JSON guards 或绕过 `createHttpClient`。

## 规则与 Skill 复盘

本轮命中的风险已由 ADR-0007、开发质量规则、客户端前端规则以及确定性测试覆盖，没有发现新的规则冲突或重复失败类别，因此不新增规则候选。项目 Skill 对模块、OpenAPI、双库与生成器边界已提供足够指导，没有形成新的稳定 Skill 缺口。
