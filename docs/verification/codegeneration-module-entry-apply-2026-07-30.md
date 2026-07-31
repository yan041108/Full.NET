# CodeGeneration 模块入口显式接线验证

- 日期：2026-07-30
- 范围：已生成后端聚合桥到 `IFullNetModule` 的显式接线
- 命令：`apply-module-entry-integration`

## 已验证行为

1. 命令只读取目标 JSON 明确指定的模块项目和模块入口，不推断
   Composition、Vue 或 Layui 路由。
2. 写盘前确认 `Generated/FullNetGeneratedModuleFeatures.g.cs` 仍由
   CodeGeneration Manifest 拥有且内容摘要未漂移。
3. 源码编辑器忽略注释和字符串中的伪调用，只接受唯一、块体形式的
   `AddServices` 与 `MapEndpoints`；表达式体、重复或缺失声明保守失败。
4. 候选入口通过临时 MSBuild 投影替换真实入口并执行目标模块 Release 编译；
   编译输出和 `bin/obj` 全部位于命令临时目录，失败时入口零写入。
5. 编译通过后，在生成器工作区锁内再次复核聚合桥和入口原文，再使用同目录
   临时文件替换入口；入口本身不纳入生成 Manifest，手写文件所有权不转移给生成器。
6. 首次执行增加生成命名空间及两条稳定聚合调用；再次执行返回 `Unchanged`
   且不重复编译。

## 新鲜验证

```text
dotnet test tests/Full.NET.UnitTests/Full.NET.UnitTests.csproj \
  -c Release --filter "FullyQualifiedName~CodeGeneration" --no-restore
通过：146/146

dotnet test tests/Full.NET.IntegrationTests/Full.NET.IntegrationTests.csproj \
  -c Release --filter "FullyQualifiedName~ModuleIntegrationBackendApplyTests" \
  --no-restore
通过：3/3
```

集成场景同时覆盖：候选编译失败零写入、成功接线、二次幂等、聚合桥漂移拒绝，
以及仓库模块目录不产生 `bin/obj`。

## 保留边界

- Composition Catalog 和 Vue/Layui 路由仍需后续独立、显式且可编译/可运行验证的接入命令。
- 表达式体或其他无法安全证明的模块入口需先由开发者改成标准块体，不做猜测性重写。
- 迁移草案仍必须分配正式编号并完成 SQL Server/MySQL 恢复语义评审。
