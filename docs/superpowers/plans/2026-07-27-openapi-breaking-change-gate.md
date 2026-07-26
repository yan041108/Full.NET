# OpenAPI Breaking Change Gate Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 为 `contracts/openapi/*.json` 建立无需启动后端的向后兼容门禁，在 pull request 中阻止已冻结 v1 HTTP 契约被静默破坏。

**Architecture:** 使用 Node.js 标准库实现纯函数比较器与薄 CLI。比较器接收“基线夹具集合”和“当前夹具集合”，CLI 支持目录对目录测试以及 Git ref 对当前工作树的 CI 模式；GitHub Actions 使用 PR base SHA 作为基线，不访问网络、不占用 Docker。

**Tech Stack:** Node.js 24、`node:test`、Git CLI、pnpm、GitHub Actions

## Global Constraints

- 不修改 C#、业务模块、数据库、Realtime、Outbox 或日志代码。
- 不新增 npm 依赖；比较器只使用 Node.js 标准库。
- v1 夹具不可通过修改同一文件中的 `version` 绕过门禁；新版本必须使用新的夹具文件。
- 破坏性变化包括：删除夹具、路径、操作、schema 或属性；改变操作权限、成功状态码、请求/响应 schema；改变分页 `itemSchema`；改变平台 OpenAPI/Scalar 与安全方案稳定配置。
- 兼容变化包括：新增夹具、路径、操作、schema 或属性；描述文本和数组顺序变化。
- 诊断必须稳定排序并包含夹具名与精确位置，便于 CI 定位。
- 当前 OpenAPI 离线基线为 50 项；本切片不改变 .NET canonical 门槛。

---

### Task 1: 建立可失败的兼容性行为测试

**Files:**
- Create: `tests/openapi/openapi-breaking-change-gate.test.mjs`
- Test: `tests/openapi/openapi-breaking-change-gate.test.mjs`

**Interfaces:**
- Consumes: CLI `node scripts/openapi/check-openapi-breaking-changes.mjs --baseline-directory <path> --current-directory <path>`
- Produces: 兼容变化退出码 `0`；破坏变化退出码 `1`；stderr 包含稳定诊断

- [x] **Step 1: 写目录夹具与 CLI 测试辅助函数**

  在临时目录分别创建 `baseline` 与 `current`，写入最小 JSON 夹具；使用
  `spawnSync(process.execPath, [scriptPath, ...args])` 调用真实 CLI，并在测试结束后删除临时目录。

- [x] **Step 2: 写兼容变化 RED**

  覆盖新增文件、路径、操作、schema、属性，以及 description/数组顺序变化；期望退出码 `0`，
  stdout 包含 `OpenAPI compatibility check passed`。

- [x] **Step 3: 写破坏变化 RED**

  分别覆盖以下输入并断言退出码 `1` 与精确诊断：

  ```text
  contract removed: sample-v1.json
  path removed: sample-v1.json /api/v1/samples/{sampleId}
  operation removed: sample-v1.json GET /api/v1/samples
  operation changed: sample-v1.json POST /api/v1/samples permission
  schema removed: sample-v1.json SampleResponse
  schema property removed: sample-v1.json SampleResponse.id
  schema itemSchema changed: sample-v1.json SampleResponsePage
  stable setting changed: platform-api-documentation-v1.json securitySchemeScheme
  ```

- [x] **Step 4: 运行 RED**

  Run: `node --test tests/openapi/openapi-breaking-change-gate.test.mjs`

  Expected: FAIL；CLI 尚不存在，兼容与精确诊断断言失败。

### Task 2: 实现纯比较器与离线 CLI

**Files:**
- Create: `scripts/openapi/openapi-contract-compatibility.mjs`
- Create: `scripts/openapi/check-openapi-breaking-changes.mjs`
- Test: `tests/openapi/openapi-breaking-change-gate.test.mjs`

**Interfaces:**
- Produces: `compareContractSets(baselineContracts, currentContracts): string[]`
- Produces: `loadContractsFromDirectory(directoryPath): Promise<Map<string, object>>`
- Produces: `loadContractsAtGitRef(repositoryRoot, baseRef): Map<string, object>`
- Consumes: `--baseline-directory` + `--current-directory`，或 `--base-ref` + 可选 `--repository-root`

- [x] **Step 1: 实现规范化与稳定索引**

  使用文件名作为版本化契约身份；常规夹具按 `path + method`、schema 名、property 名建立 Map/Set。
  忽略 `description` 和数组顺序，不改变原始夹具。

- [x] **Step 2: 实现破坏变化比较**

  对基线中的每个文件、路径、操作、schema、property 和 `itemSchema` 做单向包含检查；
  操作字段比较固定为：

  ```js
  const stableOperationFields = [
    'permission',
    'successStatus',
    'requestSchema',
    'responseSchema'
  ];
  ```

  `platform-api-documentation-v1.json` 等无 `id/version/schemas` 的稳定配置夹具，对除说明文本外的
  基线字段做深相等比较。

- [x] **Step 3: 实现目录和 Git ref 加载**

  当前目录通过 `fs.readdir/readFile` 加载；Git ref 通过参数数组调用：

  ```js
  spawnSync('git', ['ls-tree', '-r', '--name-only', baseRef, '--', 'contracts/openapi'])
  spawnSync('git', ['show', `${baseRef}:${relativePath}`])
  ```

  禁止拼接 shell 字符串；ref 无效、JSON 无效或目录缺失时退出码 `2` 并输出明确错误。

- [x] **Step 4: 实现 CLI 输出与退出码**

  无破坏变化时输出已比较的基线/当前夹具数量并退出 `0`；存在破坏变化时稳定排序、逐行输出并退出
  `1`；使用错误退出 `2`。

- [x] **Step 5: 运行 GREEN**

  Run: `node --test tests/openapi/openapi-breaking-change-gate.test.mjs`

  Expected: 全部 PASS，失败变化均由正确诊断捕获。

- [x] **Step 6: 运行现有 OpenAPI 回归**

  Run: `pnpm test:openapi`

  Expected: 原 50 项加本切片新增测试全部通过。

### Task 3: 把门禁接入 package 与 pull request CI

**Files:**
- Create: `tests/openapi/openapi-breaking-change-ci-contract.test.mjs`
- Modify: `package.json`
- Modify: `.github/workflows/ci.yml`

**Interfaces:**
- Produces: `pnpm test:openapi:breaking -- --base-ref <git-ref>`
- Produces: PR `client-build-test` 中基于 `${{ github.event.pull_request.base.sha }}` 的离线门禁

- [x] **Step 1: 写 CI wiring RED**

  测试读取 `package.json` 和 `.github/workflows/ci.yml`，断言：

  ```text
  package script = node scripts/openapi/check-openapi-breaking-changes.mjs
  client-build-test checkout fetch-depth = 0
  PR step passes github.event.pull_request.base.sha as --base-ref
  ```

- [x] **Step 2: 运行 wiring RED**

  Run: `node --test tests/openapi/openapi-breaking-change-ci-contract.test.mjs`

  Expected: FAIL；package script 与 CI step 尚不存在。

- [x] **Step 3: 增加 package script**

  在 `package.json` 增加：

  ```json
  "test:openapi:breaking": "node scripts/openapi/check-openapi-breaking-changes.mjs"
  ```

- [x] **Step 4: 增加 PR CI 门禁**

  `client-build-test` 的 checkout 设置 `fetch-depth: 0`；在现有 `pnpm test:openapi` 后增加仅 PR 执行的
  `Verify OpenAPI backward compatibility` step，并传递 pull request base SHA。

- [x] **Step 5: 运行 wiring GREEN 与真实 Git 基线**

  Run:

  ```powershell
  node --test tests/openapi/openapi-breaking-change-ci-contract.test.mjs
  pnpm test:openapi:breaking -- --base-ref HEAD
  ```

  Expected: 两项均 PASS，HEAD 对当前工作树无破坏变化。

### Task 4: 文档、复盘、验证与主线收口

**Files:**
- Modify: `README.md`
- Modify: `docs/development/getting-started.md`
- Modify: `docs/roadmap/capability-status.md`
- Modify: `docs/verification/test-threshold-audit-2026-07-19.md`
- Modify: `rules/skill-evolution.md`
- Create: `docs/verification/openapi-breaking-change-gate-2026-07-27.md`
- Modify: `docs/superpowers/plans/2026-07-27-openapi-breaking-change-gate.md`

**Interfaces:**
- Consumes: Tasks 1–3 的 CLI、测试与 CI 入口
- Produces: 使用方法、兼容/破坏语义、最新 OpenAPI 测试数和可定位验证证据

- [x] **Step 1: 更新开发入口与能力状态**

  README 记录本地命令和 base ref 用法；能力矩阵将“OpenAPI 破坏性变更门禁待补”更新为已有离线
  PR 门禁，同时保留多客户端生成缺口。

- [x] **Step 2: 写验证记录并同步门槛**

  记录 RED/GREEN、破坏/兼容样例、CI 基线来源、OpenAPI 新总数；同步测试门槛审计，明确不改变
  .NET canonical 门槛。

- [x] **Step 3: 执行完整非 Docker 门槛**

  Run:

  ```powershell
  pnpm test:openapi
  pnpm test:openapi:breaking -- --base-ref HEAD
  pnpm test:governance
  pnpm test:skills
  pnpm test:workspace
  git diff --check
  ```

  Expected: 全部 PASS；无 warning/error；不启动 Docker。

- [x] **Step 4: 执行规则与 Skill 演进复盘**

  按 `rules/rule-evolution.md` 和 `rules/skill-evolution.md` 判断是否出现第二次可泛化遗漏。
  未达到门槛时在验证记录写明“不演进”，不得新增近义规则。

- [x] **Step 5: 提交、同步 main、合并并清理**

  在隔离分支提交聚焦变更；等待当前优先任务合并窗口，合并前同步最新 `main` 并重跑受影响门槛。
  使用 `--no-ff` 合并到 `main`，删除 `codex/openapi-breaking-change-gate` 分支和工作树，最后检查
  `git status`、`git worktree list` 与分支列表。
