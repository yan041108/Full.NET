# CodeGeneration Lane B 收口

**日期**：2026-09-22

| 项 | 交付 | 证据 |
|----|------|------|
| Release 编译门禁 | `ModuleIntegrationCompilationTests` | `tests/Full.NET.IntegrationTests/CodeGeneration/ModuleIntegrationCompilationTests.cs`；受影响集 `run-affected-integration.mjs` CodeGeneration 过滤器 |
| 治理断言 | Release 编译测试类须存在 | `tests/governance/codegeneration-release-compile-gate.test.mjs` |
| real-stack 纵向 | 预览工作台 | `host-code-generation-previews.spec.mjs` |
| 清单 36 列同步 | `POST /api/v1/code-generation/catalog/column-sync` | OpenAPI `code-generation-contract.test.mjs`；real-stack `host-code-generation-catalog-column-sync.spec.mjs` |

**开放项（仅 Spec）**：Worker 多实例 Apply、远程仓库 — 未默认实现。
