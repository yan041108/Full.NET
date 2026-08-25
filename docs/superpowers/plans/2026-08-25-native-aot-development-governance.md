# Native AOT Development Governance Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 将 Host.Api Native AOT 的真实故障经验、官方兼容原则和现有 CI 证据沉淀为可执行规则、开发知识库与防漂移门禁。

**Architecture:** `rules/native-aot.md` 只保存全仓强制边界和完成门槛；`docs/development/native-aot-development-guide.md` 解释编码模式、故障诊断和开发工作流；ADR-0008/0009 继续作为运行时与 Provider 决策源，verification 继续只保存特定基线证据。Governance 测试验证入口、关键禁令、权威命令和 ADR 链接保持同步，现有 Architecture/CI 测试继续验证生产代码行为。

**Tech Stack:** .NET 10 Native AOT、ASP.NET Core、System.Text.Json source generation、Dapper.AOT、MSTest、Node.js test runner、GitHub Actions

## Global Constraints

- 不重复 ADR-0008/0009 的决策历史，也不把一次 CI 运行写成永久规则事实。
- 规则使用“必须/禁止/应/可”，并包含来源、风险、验证和例外。
- 知识库必须区分 JIT build、AOT analysis、linux-x64 publish 与原生外部进程 E2E，禁止将任一前置门禁替代 `Aot-published`。
- Provider 声明必须保持精确：S3 与 API Kafka Replay 已验证，不外推 Worker、CDC Relay、DLQ、Lag Observer 或 AWS 全凭据链。
- 自有代码不得用 `NoWarn=IL*`、通配 linker root、通配 descriptor 或无依据 suppression 换绿。
- 所有手写中文 Markdown 使用 UTF-8，并通过 governance 与 `git diff --check`。

---

### Task 1: 建立 Native AOT 强制规则源

**Files:**
- Create: `rules/native-aot.md`
- Modify: `rules/README.md`
- Modify: `AGENTS.md`

**Interfaces:**
- Consumes: `docs/architecture/adr/ADR-0008-api-native-aot-runtime-boundary.md`、`docs/architecture/adr/ADR-0009-host-api-native-aot-provider-runtime-boundary.md`、`eng/testing/test-matrix.json`
- Produces: 所有 Host.Api 可达代码、依赖、配置与测试任务必须读取的 Native AOT 规则入口

- [ ] **Step 1: 写入强制规则文件**

创建 `rules/native-aot.md`，至少包含以下可执行章节：

1. 适用范围与状态词：`Aot-analysis-clean`、`Aot-published`、`Native-provider-verified:*`。
2. 静态闭包原则：禁止运行时代码生成、无界反射、字符串类型发现和只在 JIT 下验证。
3. JSON/HTTP/SignalR：所有可达 DTO 注册 `JsonSerializerContext`；使用 `JsonTypeInfo`/context overload；HTTP 与 Hub resolver chain 同步；仅 JSON Hub。
4. DI/泛型：AOT 路径注册闭合泛型；禁止依赖容器在运行时枚举或构造未闭合泛型元数据。
5. SQL/Dapper：禁止匿名 SQL 参数；只允许 `DynamicParameters`、`IReadOnlyDictionary<string, object?>` 或显式注册参数类型；所有非标量查询结果必须同步注册行物化器；注册必须在首个请求前同步完成；双库读取转换必须显式覆盖。
6. 配置与序列化：优先源生成配置绑定；禁止回退到未分析的 `Bind`/反射式序列化；MemoryPack 继续服从 ADR-0008 的受控具体类型协议。
7. 第三方库：引入或升级时必须跑 analyzer + publish + 原生进程路径；RD.XML/DynamicDependency 只能精确保留已证明的成员；新增告警必须先定位根因再更新精确 allowlist。
8. 测试分层：代码内循环跑聚焦 Unit/Architecture；slice 跑 analyzer；发布关闭跑 Linux publish 与相关原生 E2E；状态升级只接受 fresh CI。
9. 诊断顺序：保留原生 stdout/stderr/TRX，先定位启动、DI、JSON、SQL 物化、参数绑定、native library binding，再做最小修复。
10. 完成清单与当前非目标。

- [ ] **Step 2: 注册规则入口**

在 `rules/README.md` 的规则表新增 `native-aot.md`；在根 `AGENTS.md` 的“开始前”增加条件读取要求，并在“详细规则索引”加入链接。适用条件限定为：修改 Host.Api 可达代码、AOT 编译条件、JSON/配置源生成、Dapper AOT、Provider native binding、AOT 测试或工作流。

- [ ] **Step 3: 检查规则没有复制可变测试数量**

Run:

```powershell
rg -n "29/29|5/5|2/2|minimum.*[0-9]" rules/native-aot.md
```

Expected: 无输出；规则只链接 `eng/testing/test-matrix.json`。

### Task 2: 建立开发知识库与故障模式手册

**Files:**
- Create: `docs/development/native-aot-development-guide.md`
- Modify: `docs/development/onboarding.md`

**Interfaces:**
- Consumes: Task 1 的强制规则、ADR-0008/0009、现有 `package.json` 命令与 Native AOT Architecture 测试
- Produces: 面向开发者的编码示例、选择树、命令矩阵与排障入口

- [ ] **Step 1: 编写知识库正文**

知识库必须覆盖：

- “为什么 JIT 通过而 Native AOT 失败”的编译模型说明。
- 新 Endpoint/DTO、新 SQL 查询或命令、新 Validator/Behavior、新第三方 Provider 的变更清单。
- 正确/错误对照示例：JSON context、闭合泛型 DI、字典或注册参数、显式行物化器、精确 RD.XML。
- 本轮已证实故障模式：FluentValidation 开放泛型、SignalR HTTP/Hub JSON metadata 漂移、Dapper 匿名参数、缺失物化器、MySQL `DateTime`→`DateTimeOffset`、Confluent native binding、MinIO readiness race、原生进程日志泵提前释放。
- 验证梯度及命令：`test:aot:analyzers`、`test:aot:publish:linux`、`test:dotnet:architecture --selection api-native-aot`、核心/S3/Kafka Replay E2E。
- 状态声明边界和 PR 审查问题。
- 官方依据：
  - [Native AOT deployment](https://learn.microsoft.com/dotnet/core/deploying/native-aot/)
  - [ASP.NET Core support for Native AOT](https://learn.microsoft.com/aspnet/core/fundamentals/native-aot/)
  - [System.Text.Json source generation](https://learn.microsoft.com/dotnet/standard/serialization/system-text-json/source-generation)
  - [Prepare .NET libraries for trimming](https://learn.microsoft.com/dotnet/core/deploying/trimming/prepare-libraries-for-trimming)

- [ ] **Step 2: 在 onboarding 增加单一入口**

在 `docs/development/onboarding.md` 的开发规则/能力导航区域增加知识库链接；不得复制整份检查表。

- [ ] **Step 3: 验证仓库内链接目标存在**

Run:

```powershell
@(
  'rules/native-aot.md',
  'docs/development/native-aot-development-guide.md',
  'docs/architecture/adr/ADR-0008-api-native-aot-runtime-boundary.md',
  'docs/architecture/adr/ADR-0009-host-api-native-aot-provider-runtime-boundary.md'
) | ForEach-Object { if (-not (Test-Path $_)) { throw "Missing: $_" } }
```

Expected: exit code 0。

### Task 3: 增加知识防漂移门禁并完成验证

**Files:**
- Create: `tests/governance/native-aot-guidance.test.mjs`
- Modify: `tests/governance/agents-rules-consistency.test.mjs` only if the existing generic index test cannot cover the new rule link

**Interfaces:**
- Consumes: Task 1–2 的规则与知识库
- Produces: `pnpm test:governance` 中可执行的入口、术语、命令和边界一致性检查

- [ ] **Step 1: 先写失败的 governance 测试**

新增测试，读取 `AGENTS.md`、`rules/README.md`、`rules/native-aot.md`、知识库、ADR-0008/0009、`package.json` 和 `eng/testing/test-matrix.json`，断言：

- 两个入口都链接 `rules/native-aot.md`；
- 规则包含 `JsonSerializerContext`、`DynamicParameters`、`IReadOnlyDictionary<string, object?>`、`DapperAotMaterializerRegistry`、`NoWarn=IL*`、`Aot-published`；
- 知识库列出的每条 `pnpm test:aot:*` 命令都真实存在于 `package.json`；
- Provider 精确状态链接 ADR-0009，并明确不覆盖 Worker/CDC；
- 规则不硬编码 `test-matrix` 的可变最低测试数。

- [ ] **Step 2: 运行测试并确认在文档未完成时失败**

Run:

```bash
node --test tests/governance/native-aot-guidance.test.mjs
```

Expected: 在缺少任一入口、术语或真实命令时 FAIL，并报告具体文件。

- [ ] **Step 3: 完成最小文档调整后重跑**

Run:

```bash
node --test tests/governance/native-aot-guidance.test.mjs
pnpm test:governance
```

Expected: 新测试全部通过；governance 全绿。

- [ ] **Step 4: 运行 Native AOT 文档相称门禁**

Run:

```bash
dotnet test tests/Full.NET.ArchitectureTests/Full.NET.ArchitectureTests.csproj -c Release --filter "ClassName~NativeAot" --verbosity minimal
git diff --check
git status --short
```

Expected: Native AOT Architecture 测试全绿；无空白错误；状态只包含计划内文件。

- [ ] **Step 5: 提交**

```bash
git add AGENTS.md rules/README.md rules/native-aot.md docs/development/native-aot-development-guide.md docs/development/onboarding.md tests/governance/native-aot-guidance.test.mjs docs/superpowers/plans/2026-08-25-native-aot-development-governance.md
git commit -m "docs: codify Native AOT development governance"
```
