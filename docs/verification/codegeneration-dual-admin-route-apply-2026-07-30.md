# CodeGeneration 双管理端路由显式接入验证

- 日期：2026-07-30
- 范围：Vue Router 与 Layui controller registry 的本地可信映射
- 命令：`apply-client-route-integration`

## 显式目标契约

旧模块接入目标 JSON 保持兼容；只有需要自动接入双端路由时才增加：

```json
{
  "clientRoute": {
    "routePath": "/catalog/products",
    "vueRouteName": "catalog-products",
    "vueComponentPath": "ui/admin/src/views/CatalogProductsView.vue",
    "layuiControllerPath": "ui/admin-layui/js/core/catalog-products.js",
    "layuiControllerExport": "createCatalogProductsController"
  }
}
```

路由路径和 Vue 名称只接受小写 kebab-case 稳定机器码；两个适配文件必须是仓库内
可移植相对路径并且已经存在。命令不会从 Schema、服务端菜单或动态导航猜测可执行组件。

## 已验证行为

1. 未声明 `clientRoute` 时，规划结果继续标记 `ManualReview`；Apply 以退出码 `2`
   失败且零写入。
2. 显式适配文件缺失、Layui export 不存在、非标准路由形态、重复 route/name、
   注释诱饵、相邻路由字段误拼或既有映射冲突均失败关闭。
3. Vue 编辑器只在唯一标准 `routes` 数组中、`/403` 状态路由之前增加静态 lazy
   import；Layui 编辑器只在唯一标准 controller Map 尾部增加静态 import/export 映射。
4. Apply 要求后端生成清单、模块入口与 Composition 已依次完成，不代替前三条命令。
5. 写盘前复核模块聚合桥、入口、Composition、两个路由文件和两个适配文件；使用
   Composition、模块与客户端三把锁避免并发接入互相穿透。
6. 两个候选文件先完成 staging，再按 Vue → Layui 提交；第二文件失败会回滚 Vue，
   回滚失败则保留 recovery 文件供人工审查。
7. 二次执行返回两个 `Unchanged`，不会重复增加路由。

## 分层验证

```text
CodeGeneration Unit：165/165
ModuleIntegrationBackendApplyTests：5/5
Affected inner smoke：8/8
Affected inner CodeGeneration + Realtime：24/24
Vue typecheck：通过
Layui production build：通过
Release solution build：0 warning / 0 error
Full Unit：708/708
Release discovery：Integration 228，API SQL Server/MySQL 38/38，
Migrations 70，Infrastructure 82
Test matrix contract：4/4
git diff --check：通过
```

## 保留边界

- 生成器当前只生成 headless Vue/Layui page model；真实 `.vue` View 和 Layui
  controller 适配层仍须先显式创建并完成业务验收。
- 菜单、权限、翻译、动态导航目录和 E2E 不由路由命令推断或修改。
- 客户端可见性不替代服务端授权；未知服务端导航标识仍必须由本地白名单拒绝。
- 非标准 Router/Map 结构保持人工接入，不扩大启发式文本改写范围。
