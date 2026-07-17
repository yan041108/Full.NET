# Project Rules Governance Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 建立可自动发现、覆盖代码注释与开发遗漏防护、并能基于证据持续演进的 Full.NET 项目规则体系。

**Architecture:** 根目录 `AGENTS.md` 是所有开发代理的强制入口，负责加载模块化 `rules/` 文件。规则文件分别管理索引、注释质量、开发质量和规则演进，避免单文件无限增长；规则变更通过 Git diff、复盘门槛和验证命令保持可审计。

**Tech Stack:** Markdown、Git、PowerShell、ripgrep

## Global Constraints

- 规则不得覆盖系统、开发者或用户指令。
- 代码标识符使用清晰英文；所有手写源代码注释（包括 XML 文档注释）统一使用清晰中文，专业术语可保留英文。
- “清楚完整”要求解释意图、边界和不变量，不要求逐行复述代码。
- 新规则必须具体、可执行、尽可能可验证，并记录来源或理由。
- 只有重复遗漏、高风险问题、明确长期决策或已有规则歧义才能升级为强制规则。
- 所有手写文件使用 UTF-8 编码。

---

### Task 1: 自动入口与规则索引

**Files:**
- Create: `AGENTS.md`
- Create: `rules/README.md`

**Interfaces:**
- Consumes: `docs/superpowers/specs/2026-07-17-project-rules-governance-design.md` 中的文件结构与优先级设计。
- Produces: 仓库级自动入口和指向 `rules/code-comments.md`、`rules/development-quality.md`、`rules/rule-evolution.md` 的规则索引。

- [x] **Step 1: 创建根目录入口**

  创建 `AGENTS.md`，明确：开始任务前读取规则、按任务类型加载详细文件、结束前执行验证与遗漏复盘、规则冲突时服从更高优先级指令。

- [x] **Step 2: 创建规则索引**

  创建 `rules/README.md`，列出每个规则文件的适用场景、规则用词、优先级和维护流程。

- [x] **Step 3: 验证入口引用完整**

  Run:

  ```powershell
  $required = @('rules/README.md','rules/code-comments.md','rules/development-quality.md','rules/rule-evolution.md')
  $missing = $required | Where-Object { -not (Select-String -Path AGENTS.md -SimpleMatch $_ -Quiet) }
  if ($missing) { throw "AGENTS.md missing references: $($missing -join ', ')" }
  ```

  Expected: 命令退出码为 `0`，无输出。

- [x] **Step 4: 提交入口与索引**

  ```powershell
  git add AGENTS.md rules/README.md
  git commit -m "docs: add project rules entry point"
  ```

### Task 2: 注释规范与遗漏防护规则

**Files:**
- Create: `rules/code-comments.md`
- Create: `rules/development-quality.md`

**Interfaces:**
- Consumes: `AGENTS.md` 的强制加载约定和 Full.NET 当前技术选型。
- Produces: 可用于代码审查的注释要求，以及覆盖需求、架构、数据、安全、API、基础设施、测试、文档、依赖和 Git 的完成清单。

- [x] **Step 1: 编写代码注释规则**

  `rules/code-comments.md` 必须包含适用范围、必须注释的场景、各文件类型规范、禁止项、审查清单和示例。示例必须对比“解释原因/约束”的有效注释与“复述语法”的无效注释。

- [x] **Step 2: 编写开发质量规则**

  `rules/development-quality.md` 必须覆盖：范围授权、架构边界、安全与租户、并发事务与 Outbox、Dapper 与双数据库、API 与兼容性、缓存消息与可观测性、测试验证、文档状态、依赖许可证、Git 与跨平台。

- [x] **Step 3: 验证关键项目约束已覆盖**

  Run:

  ```powershell
  $terms = @('Dapper','SQL Server','MySQL','ProblemDetails','Admin.NET','FusionCache','MessagePack','Outbox')
  $missing = $terms | Where-Object { -not (Select-String -Path rules/development-quality.md -SimpleMatch $_ -Quiet) }
  if ($missing) { throw "Missing Full.NET constraints: $($missing -join ', ')" }
  ```

  Expected: 命令退出码为 `0`，无输出。

- [x] **Step 4: 提交质量规则**

  ```powershell
  git add rules/code-comments.md rules/development-quality.md
  git commit -m "docs: define comment and development quality rules"
  ```

### Task 3: 规则自我迭代与最终验证

**Files:**
- Create: `rules/rule-evolution.md`
- Modify: `docs/superpowers/plans/2026-07-17-project-rules-governance.md`

**Interfaces:**
- Consumes: `rules/README.md` 的维护流程以及 `rules/development-quality.md` 的任务结束检查。
- Produces: 任务结束复盘、候选经验、规则升级、冲突消解、规则退役和变更报告机制。

- [x] **Step 1: 编写规则演进机制**

  `rules/rule-evolution.md` 必须定义：每次任务的复盘问题、三类发现、四项升级门槛、规则模板、去重与冲突检查、规则退役，以及禁止静默修改和无限自我扩张。

- [x] **Step 2: 检查占位符与链接目标**

  Run:

  ```powershell
  $files = @('AGENTS.md','rules/README.md','rules/code-comments.md','rules/development-quality.md','rules/rule-evolution.md')
  $files | ForEach-Object { if (-not (Test-Path $_)) { throw "Missing file: $_" } }
  $placeholderPattern = '\bTBD\b|implement later|fill in details'
  $hits = Select-String -Path $files -Pattern $placeholderPattern
  if ($hits) { $hits | Format-Table; throw 'Rule files contain placeholders.' }
  ```

  Expected: 命令退出码为 `0`，无输出。

- [x] **Step 3: 检查 UTF-8、空白和仓库状态**

  Run:

  ```powershell
  $utf8 = [System.Text.UTF8Encoding]::new($false, $true)
  @('AGENTS.md','rules/README.md','rules/code-comments.md','rules/development-quality.md','rules/rule-evolution.md') | ForEach-Object {
      $null = $utf8.GetString([System.IO.File]::ReadAllBytes((Resolve-Path $_)))
  }
  git diff --check
  git status --short
  ```

  Expected: UTF-8 解码无异常，`git diff --check` 无输出，状态仅包含本任务计划与 `rules/rule-evolution.md` 的预期变更。

- [x] **Step 4: 标记计划完成并提交**

  将本计划的复选框全部改为 `[x]`，然后执行：

  ```powershell
  git add rules/rule-evolution.md docs/superpowers/plans/2026-07-17-project-rules-governance.md
  git commit -m "docs: add evidence-driven rule evolution"
  ```

- [x] **Step 5: 执行最终仓库验证**

  Run:

  ```powershell
  dotnet build Full.NET.slnx -c Release
  git status --short
  git branch --list
  ```

  Expected: 构建成功且 `0` errors，工作区无变更，本地只存在 `main` 分支。
