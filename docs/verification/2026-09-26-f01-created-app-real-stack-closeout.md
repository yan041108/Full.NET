# F01 创建应用 real-stack 收口

**基线提交**：执行时以 `git rev-parse HEAD` 为准（工作区实现 preset 迁移闭包 + created-app real-stack 测试）。

## 范围

- preset-scoped `framework-manifest.json` 迁移闭包（`migration-script-modules.mjs` + Migrator 过滤）
- [`tests/templates/created-app-real-stack.test.mjs`](../tests/templates/created-app-real-stack.test.mjs)（SqlServer + MySQL）
- CI 作业 `template-created-app-real-stack`（[`.github/workflows/ci.yml`](../.github/workflows/ci.yml)）

## 验证命令

```bash
pnpm test:templates
# 或仅 real-stack（需 Docker / Testcontainers + 干净 bundle 输入；本地显式开启）：
#   set FULLNET_RUN_TEMPLATE_REAL_STACK=1   # Windows
#   FULLNET_RUN_TEMPLATE_REAL_STACK=1 node --test tests/templates/created-app-real-stack.test.mjs
node --test tests/templates/created-app-real-stack.test.mjs
```

本地未提交 bundle 输入路径时，`source-bundle.test.mjs` 中带 `buildSourceBundle` 的用例会 **skip**（仍保留「脏树拒绝打包」单测）；**CI 干净提交** 下须全部通过。`build-source-bundle` CLI 仍拒绝脏树。

## 未验证项

- 离线无 npm 缓存的创建流程（F01 条文中的极端干净环境）
- `dotnet new fullnet-app` 直接安装路径（仍推荐 `create-app.mjs` 校验路径）
- F16 / Production-verified

## 状态

**Build-verified（工作区）**；CI run id 待 main 推送后登记。
