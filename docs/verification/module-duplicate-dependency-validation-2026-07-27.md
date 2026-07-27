# 模块重复依赖键注册验证记录（2026-07-27）

## 范围

本切片收紧模块组合根的依赖图注册边界：

- 模块依赖集合中的键继续使用区分大小写的稳定模块键；
- `null`、空白键与同一集合内的重复键均在 `Add` 阶段立即失败；
- 合法依赖的确定排序、未知依赖与循环检测语义保持不变。

本切片仅修改 `FullNetModuleRegistry`、既有 `ModuleRegistryTests` 方法与本验证
记录，不增加测试方法，不改变模块公开契约、Host Profile、数据库或 Docker 场景。

## TDD 证据

| 阶段 | 命令 | 结果 |
| --- | --- | --- |
| 基线 | `dotnet test tests/Full.NET.UnitTests/Full.NET.UnitTests.csproj -c Release --no-restore --filter "FullyQualifiedName~ModuleRegistryTests"` | 10/10 |
| RED | 聚焦 `Registry_rejects_invalid_dependency_keys_when_adding_modules` | 0/1；重复 `base` 依赖未抛出 `InvalidOperationException` |
| GREEN | 再次运行全部 `ModuleRegistryTests` | 10/10 |

## 验证

| 门槛 | 结果 |
| --- | --- |
| Release solution | 0 warning / 0 error |
| Unit | 416/416；既有测试方法扩展，canonical 不变 |
| Compatibility | 7/7 |
| Architecture | 49/49 |
| Integration tooling | 4/4 |
| Governance / Skill | 11/11；52 项契约检查 |
| Workspace / diff | workspace 与 `git diff --check` 通过 |
| owned C# format | 两份本切片文件限定 `dotnet format --verify-no-changes` 通过 |
| Integration | 继承紧邻 Outbox 主线完整全量 191/191；本切片不占 Docker |

Windows checkout 初次 scoped format 检查报告 owned C# 文件仍使用 CRLF；formatter
仅将这两份文件规范为 `.editorconfig` 要求的 LF，Git 差异保持局部。

## 规则与 Skills 复盘

- 规则：这是模块依赖图注册期的局部输入完整性缺口；现有模块依赖与测试先行
  规则已经覆盖，不新增或修改规则。
- Skills：本切片没有形成跨模块重复且需要多步工程判断的新流程，也未暴露
  `fullnet-module-delivery` 缺口，本次无 Skills 变化。
