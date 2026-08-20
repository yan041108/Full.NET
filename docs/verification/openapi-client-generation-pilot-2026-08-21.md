# OpenAPI 驱动客户端生成三类试点验证（2026-08-21）

- 决策：`Pilot-stopped`
- 能力状态：`Implemented/Experimental`
- 分支：`codex/openapi-client-pilots`
- 比较基线：`6b7b8bd57bee9622b17f945691190ab239524338`
- 适用决策：[`ADR-0007`](../architecture/adr/ADR-0007-openapi-driven-client-generation-boundary.md)

## 结论

第一阶段严格只实施三类代表性试点：Identity Host Users、Files Host Files、Settings Host Config Entries。标准 OpenAPI 快照、确定性仓库内生成器、运行时守卫、共享 HTTP Runtime、Vue 薄适配层和 CRUD OpenAPI 收敛均已落地；29 个 Operation 已进入试点清单。

试点实现的聚焦测试和双库 Integration 通过，但 ADR-0007 第 5 节要求所有门禁同时通过。最终严格门禁仍有三项失败：Vue 全量 Unit、Vue Build、客户端依赖 High 漏洞审计。因此本轮必须判定 `Pilot-stopped`，清单状态继续保持 `pilot`，不提升路线图能力状态，也不允许迁移第四个模块。

## 实施范围与提交

| 提交 | 内容 |
| --- | --- |
| `006c8374` | 建立客户端生成就绪失败关闭门禁 |
| `99b97688` | 固定三类试点的 operationId、主 Tag 与运行时元数据 |
| `77625154` | 冻结规范化标准 OpenAPI 快照与精确试点清单 |
| `1fc794b5` | 选择并实现确定性仓库内客户端生成器 |
| `0e578e59` | 生成三个试点并收缩 Vue API 为薄适配层 |
| `44b4cd41` | 让 CRUD 代码生成器产出标准 OpenAPI 并复用同一客户端链路 |

清单只包含以下三组，共 29 个 Operation：

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

## 新鲜验证证据

### 通过项

| 命令 | 结果 |
| --- | --- |
| `pnpm openapi:client:generate -- --check` | 退出码 0，生成产物零漂移 |
| `pnpm test:openapi` | 106/106，通过；包含就绪、规范化、生成器、CRUD Golden OpenAPI 与覆盖门禁 |
| `pnpm --filter @fullnet/client-contracts test` | 137/137，通过 |
| `pnpm --filter @fullnet/client-contracts build` | 退出码 0 |
| `pnpm --filter @fullnet/admin exec vitest run src/api/users.test.ts src/api/host-files.test.ts src/api/config-entries.test.ts` | 16/16，通过 |
| `dotnet ...Full.NET.UnitTests.dll --filter FullyQualifiedName~CodeGeneration` | 306/306，通过 |
| `pnpm test:naming` | 30/30，通过 |
| `pnpm test:governance` | 38/38，通过 |
| `pnpm test:integration:affected -- --base 6b7b8bd57bee9622b17f945691190ab239524338 --phase merge` | 87/87，通过，SQL Server/MySQL 双 Provider，覆盖三个试点、客户端快照、CodeGeneration 与 Smoke |
| `dotnet build Full.NET.slnx -c Release` | 退出码 0，0 warning、0 error |
| `pnpm licenses list --prod --json` | 退出码 0 |

双库 Integration 的客户端规范快照逐字节一致，并覆盖 JSON、multipart、Blob、`204`、ProblemDetails、鉴权和稳定 Operation 身份。共享 `client-contracts` 测试继续覆盖 Refresh、Cookie、语言、ProblemDetails、取消和不可信 JSON 守卫语义；三个 Vue 业务适配文件不再声明后端 DTO 或拼接试点路径。

### 停止门禁

| 命令 | 实际结果 | 判定 |
| --- | --- | --- |
| `pnpm --filter @fullnet/admin test` | 125 个文件中 3 个失败；455 项中 11 项失败、444 项通过，另有 6 个未处理错误 | FAIL |
| `pnpm --filter @fullnet/admin build` | 退出码 2；`CodeGenerationTemplatesView.vue` 存在 4 个 TypeScript 错误 | FAIL |
| `pnpm audit:clients` | 退出码 1；未复核 High advisory `GHSA-2v37-7h3g-55p8` | FAIL |

Vue Unit 失败集中在既有 Job Schedules、Menus 与 `CodeGenerationPreviewsView` 测试；未处理错误为测试中的 route mock 缺失导致读取 `route.query` 失败。Vue Build 错误位于 `CodeGenerationTemplatesView.vue` 第 219、233、504、519 行。上述文件不在三个试点适配层改动范围内，并已在试点基线复现，但 ADR-0007 不允许因其为既有失败而跳过严格门禁。

依赖审计阻断来自 uni-app 传递链中的 `nanoid` 3.3.16（`GHSA-2v37-7h3g-55p8`）。本次没有新增该依赖，但在完成复核、升级或正式例外登记前仍视为失败。

## 停止后的边界

- `contracts/openapi/client-generation-manifest-v1.json` 的 29 个条目继续保持 `pilot`，不得改成 `generated`。
- 不更新 `docs/roadmap/capability-status.md`，不把该能力描述为 `Build-verified`。
- 不创建 Task 8 或批量迁移计划；现有非试点 Vue API 模块继续保持手写实现。
- 修复 Vue 全量 Unit/Build 基线并处理 High advisory 后，必须从零漂移、OpenAPI、client-contracts、Vue、依赖审计、双库 merge Integration 到 Release build 完整重跑，不能只补跑失败项。

## 规则与 Skill 复盘

本轮命中的风险已由 ADR-0007、开发质量规则、客户端前端规则以及确定性测试覆盖，没有发现新的规则冲突或重复失败类别，因此不新增规则候选。项目 Skill 对模块、OpenAPI、双库与生成器边界已提供足够指导，没有形成新的稳定 Skill 缺口。
