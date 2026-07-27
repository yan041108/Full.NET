# Seeding DefaultLocale 启动校验验证记录（2026-07-27）

- 范围：`Seeding:DefaultLocale` 启动配置校验、聚焦 Unit 回归。
- 状态：**Build-verified**。
- 最终同步基线：`main@306b4583841826c39c814a32c7ee5f4839fca5bb`。

## 行为合同

1. `Seeding:DefaultLocale` 必须是可由 `CultureInfo` 解析的非空语言标签。
2. 非空但非法的标签必须在 `IStartupValidator.Validate()` 阶段以
   `OptionsValidationException` 快速失败，并保留稳定机器码
   `seed.options.invalid`。
3. 默认值 `zh-CN` 与现有 `LockTimeoutSeconds` 范围不变；本切片不修改
   Seed 编排、数据库租约、SQL Server/MySQL 语义或持久化结构。

## RED / GREEN 证据

| 阶段 | 新鲜结果 |
| --- | --- |
| 基线 | Seeding 聚焦 **45/45**，失败 0，跳过 0 |
| RED | `Startup_validation_rejects_an_invalid_default_locale` 因未抛异常失败 **1/1** |
| GREEN | 新增契约 **1/1**；Seeding 聚焦 **46/46**，失败 0，跳过 0 |
| 格式与差异 | 两个代码文件完成定向 `dotnet format`；`git diff --check` 通过 |

## 最终验证

| 门槛 | 新鲜结果 |
| --- | --- |
| `Full.NET.slnx` Release | **0 warning / 0 error** |
| Unit / Compatibility / Architecture | **410/410** / **7/7** / **49/49**，失败 0，跳过 0 |
| Seeding SQL Server/MySQL 聚焦 | **6/6**，失败 0，跳过 0，**2m57s** |
| Integration 分片发现 | API SQL Server **35** + API MySQL **35** + Migrations **62** + Infrastructure **57** = **189** |
| Governance / Project Skill / Workspace | **11/11** / **52** 项 / 通过 |
| Docker / Integration 进程 | **0 / 0** |

紧邻前序 Tenancy 共享宿主变更已在最终主线序列中完成完整 Integration
**189/189**，失败 0、跳过 0，**30m07s**，stderr 0。本切片只修改
Migrator 专用 Seeding 配置验证，不改共享宿主、SQL、事务或数据映射；按
`rules/development-quality.md` 第 11.1 节执行受影响的 Seeding 双提供程序
聚焦 **6/6**，不重复完整全量。

最终 canonical 为 **410/7/49/189**；四处门槛与审计记录已同步。

## 规则与 Skills 复盘

本次缺口已由仓库现有“全栈使用规范 BCP 47 标签”和“配置在启动期校验”
规则覆盖，并已用自动化契约阻断，无需新增近义规则。该切片没有形成新的、
重复且包含多项工程判断的交付流程，现有项目 Skill 无缺口，因此无 Skills
变化。
