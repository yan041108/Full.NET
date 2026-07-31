# CodeGeneration Composition 显式接入验证

- 日期：2026-07-30
- 范围：显式模块项目引用与 Composition Catalog 模块构造
- 命令：`apply-composition-integration`

## 已验证行为

1. 命令要求后端聚合桥仍受 Manifest 所有且模块入口已经完成两条稳定聚合调用；
   前置条件不满足时不修改 Composition。
2. `.csproj` 编辑器只增加目标 JSON 指定模块的精确相对
   `ProjectReference`；等价正斜杠/反斜杠引用保持幂等。
3. Catalog 编辑器只接受唯一标准
   `CreateModules() => [ ... ];`，在列表尾部追加模块构造，并忽略注释和普通字符串诱饵。
4. 多个 ProjectReference ItemGroup、无法解析 XML、传统方法体、表达式体偏差或其他
   无法安全证明的结构均失败关闭。
5. 编译门禁使用系统临时 MSBuild targets 注入模块项目引用、移除真实 Catalog 并加入
   候选 Catalog，对显式 Composition 项目执行真实 Release 构建，不预写真实文件。
6. 编译通过后，在仓库 Composition 锁与模块生成锁内再次复核聚合桥、模块入口、
   `.csproj` 和 Catalog 原文。
7. 两文件提交按“项目引用 → Catalog”排序并预先 staging；Catalog 提交失败会回滚项目，
   回滚失败则保留原项目 recovery 文件供人工审查。进程中断后，单独项目引用仍可编译且
   命令可安全重入。
8. 二次执行返回两个 `Unchanged`，不重复执行 Release 编译；Composition 目录不产生
   `bin/obj`。

## 新鲜聚焦验证

```text
Composition 编辑器、投影与 CLI Unit：8/8
ModuleIntegrationBackendApplyTests：4/4
```

Integration 场景按 `apply-module-integration` →
`apply-module-entry-integration` → `apply-composition-integration`
完整顺序执行，并覆盖 Composition 编译失败零写入、首次双文件提交、二次幂等和模块入口
前置条件失效。

## 保留边界

- Vue/Layui 路由、菜单、权限、翻译和页面仍保持独立接入与双端验收。
- 自定义 Composition 项目组织或非标准 Catalog 必须人工改为规范形态或先批准独立编辑策略。
- 本命令不创建新模块、不推断模块依赖，也不修改宿主 `Program.cs`。
