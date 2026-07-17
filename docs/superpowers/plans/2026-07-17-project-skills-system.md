# Project Skills System Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 在仓库内建立可自动发现、契约测试先行、可持续演进的项目级 Skills，并交付首个 `fullnet-module-delivery` Skill。

**Architecture:** `.agents/skills/` 保存项目级 Skills，首个 Skill 用精简 `SKILL.md` 编排模块交付，并通过单层 `references/delivery-map.md` 按需加载仓库细节。`tests/skills/` 提供跨平台契约验证，`rules/skill-evolution.md` 管理候选、升级、修改和退役。

**Tech Stack:** Markdown、YAML、Python 3、Skill Creator scripts、Git、.NET 10/Microsoft Testing Platform

## Global Constraints

- 项目 Skills 必须位于 `.agents/skills/` 并随仓库版本控制。
- 每次只能创建或实质修改一个 Skill，验证并提交后才能处理下一个。
- 新建或修改 Skill 必须先运行一个因缺少目标行为而失败的契约或场景。
- `SKILL.md` Frontmatter 只能包含 `name` 与 `description`，描述必须以 `Use when...` 开头且只表达触发条件。
- Skill 正文不得超过 500 行，不得创建 Skill 内 README、安装指南或变更日志。
- 代码标识符使用英文；所有手写代码注释使用清晰中文。
- 纯机械流程优先实现为测试、脚本或 CI，不为每个命令创建 Skill。
- 本轮不调用子代理；使用契约场景、官方校验器和人工逐项审查作为可审计替代。

---

### Task 1: 建立 Skill 契约并验证 RED

**Files:**
- Create: `tests/skills/fullnet-module-delivery.contract.json`
- Create: `tests/skills/validate_project_skills.py`

**Interfaces:**
- Consumes: `docs/superpowers/specs/2026-07-17-project-skills-system-design.md` 的三个验收场景。
- Produces: `python tests/skills/validate_project_skills.py`，目标 Skill 不存在或契约缺项时返回非零退出码。

- [ ] **Step 1: 创建契约场景**

  创建以下 JSON：

  ```json
  {
    "skill": "fullnet-module-delivery",
    "max_lines": 500,
    "required_terms": [
      "AGENTS.md",
      "Dapper",
      "SQL Server",
      "MySQL",
      "ProblemDetails",
      "Admin.NET",
      "FusionCache",
      "MessagePack",
      "Outbox",
      "中文"
    ],
    "scenarios": [
      {
        "name": "完整业务模块",
        "prompt": "新增 Organization 模块 CRUD，并对标 Admin.NET 的机构管理。",
        "required_terms": ["Core", "Contracts", "Domain", "Features", "Persistence", "租户", "授权", "迁移", "集成测试"]
      },
      {
        "name": "事件与缓存",
        "prompt": "命令提交后发布事件并让多实例缓存失效。",
        "required_terms": ["事务", "Outbox", "MessagePack", "FusionCache", "提交后", "租户"]
      },
      {
        "name": "只读端点",
        "prompt": "新增无需数据库结构变化的只读查询端点。",
        "required_terms": ["按需", "不要创建", "ProblemDetails", "测试数量"]
      }
    ]
  }
  ```

- [ ] **Step 2: 创建跨平台契约验证器**

  创建 `tests/skills/validate_project_skills.py`：

  ```python
  from __future__ import annotations

  import json
  import re
  import sys
  from pathlib import Path


  ROOT = Path(__file__).resolve().parents[2]
  CONTRACT_PATH = Path(__file__).with_name("fullnet-module-delivery.contract.json")
  PLACEHOLDER_PATTERN = re.compile(
      r"\b(?:TB[D]|TO[DO]|FIXM[E])\b|implement\s+later|fill\s+in\s+details",
      re.IGNORECASE,
  )


  def read_utf8(path: Path) -> str:
      return path.read_text(encoding="utf-8")


  def main() -> int:
      contract = json.loads(read_utf8(CONTRACT_PATH))
      skill_name = contract["skill"]
      skill_dir = ROOT / ".agents" / "skills" / skill_name
      errors: list[str] = []

      if not skill_dir.is_dir():
          relative = skill_dir.relative_to(ROOT).as_posix()
          print(f"Missing skill directory: {relative}", file=sys.stderr)
          return 1

      skill_path = skill_dir / "SKILL.md"
      metadata_path = skill_dir / "agents" / "openai.yaml"
      reference_path = skill_dir / "references" / "delivery-map.md"

      for path in (skill_path, metadata_path, reference_path):
          if not path.is_file():
              errors.append(f"Missing required file: {path.relative_to(ROOT).as_posix()}")

      if errors:
          print("\n".join(errors), file=sys.stderr)
          return 1

      skill_text = read_utf8(skill_path)
      metadata_text = read_utf8(metadata_path)
      reference_text = read_utf8(reference_path)
      combined_text = f"{skill_text}\n{reference_text}"

      frontmatter = re.match(r"\A---\r?\n(.*?)\r?\n---\r?\n", skill_text, re.DOTALL)
      if frontmatter is None:
          errors.append("SKILL.md has no valid YAML frontmatter.")
      else:
          lines = [line for line in frontmatter.group(1).splitlines() if line.strip()]
          keys = [line.split(":", 1)[0].strip() for line in lines if ":" in line]
          fields = {
              line.split(":", 1)[0].strip(): line.split(":", 1)[1].strip()
              for line in lines
              if ":" in line
          }
          if keys != ["name", "description"]:
              errors.append(f"Frontmatter keys must be name, description; got {keys}.")
          if fields.get("name") != skill_name:
              errors.append(f"Frontmatter name must be {skill_name}.")
          if not fields.get("description", "").startswith("Use when "):
              errors.append("Frontmatter description must start with 'Use when '.")

      line_count = len(skill_text.splitlines())
      if line_count > contract["max_lines"]:
          errors.append(f"SKILL.md has {line_count} lines; maximum is {contract['max_lines']}.")
      if PLACEHOLDER_PATTERN.search(combined_text):
          errors.append("Skill content contains a placeholder marker.")
      if "(references/delivery-map.md)" not in skill_text:
          errors.append("SKILL.md must link directly to references/delivery-map.md.")
      if f"${skill_name}" not in metadata_text:
          errors.append(f"agents/openai.yaml must mention ${skill_name} in default_prompt.")

      required_terms = list(contract["required_terms"])
      for scenario in contract["scenarios"]:
          required_terms.extend(scenario["required_terms"])
      for term in dict.fromkeys(required_terms):
          if term not in combined_text:
              errors.append(f"Missing contract term: {term}")

      if errors:
          print("\n".join(errors), file=sys.stderr)
          return 1

      print(f"PASS {skill_name}: {len(tuple(dict.fromkeys(required_terms)))} contract checks")
      return 0


  if __name__ == "__main__":
      raise SystemExit(main())
  ```

- [ ] **Step 3: 运行 RED 并确认失败原因**

  Run:

  ```powershell
  python tests/skills/validate_project_skills.py
  ```

  Expected: 返回码为 `1`，输出包含 `Missing skill directory: .agents/skills/fullnet-module-delivery`；失败原因只能是 Skill 尚未创建。

- [ ] **Step 4: 提交失败契约**

  ```powershell
  git add tests/skills/fullnet-module-delivery.contract.json tests/skills/validate_project_skills.py
  git commit -m "test: define project skill delivery contract"
  ```

### Task 2: 初始化并实现 fullnet-module-delivery

**Files:**
- Create: `.agents/skills/fullnet-module-delivery/SKILL.md`
- Create: `.agents/skills/fullnet-module-delivery/agents/openai.yaml`
- Create: `.agents/skills/fullnet-module-delivery/references/delivery-map.md`

**Interfaces:**
- Consumes: Task 1 的契约词与三个场景。
- Produces: 可由项目上下文发现、可通过官方和项目校验器验证的 `fullnet-module-delivery` Skill。

- [ ] **Step 1: 使用官方初始化脚本创建 Skill**

  Run:

  ```powershell
  python C:/Users/Administrator/.codex/skills/.system/skill-creator/scripts/init_skill.py fullnet-module-delivery --path .agents/skills --resources references --interface "display_name=Full.NET 模块交付" --interface "short_description=按项目架构交付完整模块纵向切片" --interface "default_prompt=Use `$fullnet-module-delivery to implement a production-ready Full.NET module slice from contract through verification."
  ```

  Expected: 创建 Skill 目录、`agents/openai.yaml` 和空的 `references/`，命令退出码为 `0`。

- [ ] **Step 2: 编写精简 Skill 正文**

  `SKILL.md` 必须包含：读取规则与路线、功能归属、切片结构、RED-GREEN、Dapper 双数据库、事务/Outbox/缓存、标准 API 与兼容层、注册与序列化、测试/文档/规则复盘、按需决策表和常见错误。Frontmatter 使用：

  ```yaml
  ---
  name: fullnet-module-delivery
  description: Use when adding or extending a Full.NET module, CRUD feature, endpoint, command/query, Dapper persistence, SQL Server/MySQL migration, Admin.NET parity capability, or end-to-end product slice in this repository.
  ---
  ```

- [ ] **Step 3: 编写按需仓库地图**

  `references/delivery-map.md` 必须列出当前目录职责、Tenancy 参考切片、不同变更所需文件、四套测试命令和测试数量更新位置。不得复制 `SKILL.md` 的流程说明。

- [ ] **Step 4: 运行 GREEN 契约验证**

  Run:

  ```powershell
  python tests/skills/validate_project_skills.py
  ```

  Expected: 返回码为 `0`，输出以 `PASS fullnet-module-delivery:` 开头。

- [ ] **Step 5: 运行官方 Skill 校验**

  Run:

  ```powershell
  $validatorDeps = Join-Path $env:TEMP 'fullnet-skill-validation'
  python -m pip install --disable-pip-version-check --upgrade --target $validatorDeps PyYAML==6.0.2
  $env:PYTHONPATH = $validatorDeps
  python C:/Users/Administrator/.codex/skills/.system/skill-creator/scripts/quick_validate.py .agents/skills/fullnet-module-delivery
  ```

  Expected: PyYAML 安装到工作区外的临时目录；校验返回码为 `0`，输出确认 Skill 有效。

- [ ] **Step 6: 提交已验证 Skill**

  ```powershell
  git add .agents/skills/fullnet-module-delivery
  git commit -m "feat: add Full.NET module delivery skill"
  ```

### Task 3: 建立 Skills 自我迭代治理

**Files:**
- Create: `rules/skill-evolution.md`
- Modify: `AGENTS.md`
- Modify: `rules/README.md`
- Modify: `rules/rule-evolution.md`

**Interfaces:**
- Consumes: 已验证的 `fullnet-module-delivery` 与设计中的候选矩阵。
- Produces: 每项任务结束时执行 Skill 复盘，并能将重复、稳定、需要判断的工作流升级为独立 Skill。

- [ ] **Step 1: 编写 Skill 演进规则**

  `rules/skill-evolution.md` 必须定义规则与 Skill 的边界、候选登记、升级门槛、先自动化判断、RED-GREEN-REFACTOR、单 Skill 部署门禁、元数据同步、退役和交付披露，并登记设计中的七个后续候选。

- [ ] **Step 2: 接入仓库自动入口**

  在 `AGENTS.md` 的开始前流程中要求按任务匹配 `.agents/skills/`；在完成前流程中要求规则复盘后执行 Skill 复盘。在 `rules/README.md` 添加治理文件索引，在 `rules/rule-evolution.md` 说明两类复盘顺序。

- [ ] **Step 3: 验证入口、候选和链接**

  Run:

  ```powershell
  $required = @('.agents/skills/fullnet-module-delivery','rules/skill-evolution.md','fullnet-dual-database-change','fullnet-outbox-event-delivery','fullnet-api-compatibility','fullnet-cache-feature','fullnet-release-verification','fullnet-realtime-feature','fullnet-agentic-feature')
  $files = @('AGENTS.md','rules/README.md','rules/rule-evolution.md','rules/skill-evolution.md')
  $missing = $required | Where-Object { $term=$_; -not ($files | Where-Object { Select-String -Path $_ -SimpleMatch $term -Quiet }) }
  if ($missing) { throw "Missing skill governance terms: $($missing -join ', ')" }
  ```

  Expected: 命令退出码为 `0`，无输出。

- [ ] **Step 4: 提交治理规则**

  ```powershell
  git add AGENTS.md rules/README.md rules/rule-evolution.md rules/skill-evolution.md
  git commit -m "docs: govern project skill evolution"
  ```

### Task 4: 完成计划与最终验证

**Files:**
- Modify: `docs/superpowers/plans/2026-07-17-project-skills-system.md`

**Interfaces:**
- Consumes: Tasks 1-3 的提交。
- Produces: 完成状态、可复现验证证据和干净 `main`。

- [ ] **Step 1: 复核契约与 Skill 结构**

  Run:

  ```powershell
  python tests/skills/validate_project_skills.py
  $validatorDeps = Join-Path $env:TEMP 'fullnet-skill-validation'
  $env:PYTHONPATH = $validatorDeps
  python C:/Users/Administrator/.codex/skills/.system/skill-creator/scripts/quick_validate.py .agents/skills/fullnet-module-delivery
  git diff --check
  ```

  Expected: 两个验证器返回 `0`，空白检查无输出。

- [ ] **Step 2: 标记计划完成并提交**

  将本计划所有复选框改为 `[x]` 后执行：

  ```powershell
  git add docs/superpowers/plans/2026-07-17-project-skills-system.md
  git commit -m "docs: complete project skills plan"
  ```

- [ ] **Step 3: 运行仓库验证**

  Run:

  ```powershell
  dotnet build Full.NET.slnx -c Release --no-restore
  dotnet tests/Full.NET.UnitTests/bin/Release/net10.0/Full.NET.UnitTests.dll --no-ansi --progress off --minimum-expected-tests 48
  dotnet tests/Full.NET.CompatibilityTests/bin/Release/net10.0/Full.NET.CompatibilityTests.dll --no-ansi --progress off --minimum-expected-tests 4
  dotnet tests/Full.NET.ArchitectureTests/bin/Release/net10.0/Full.NET.ArchitectureTests.dll --no-ansi --progress off --minimum-expected-tests 7
  dotnet tests/Full.NET.IntegrationTests/bin/Release/net10.0/Full.NET.IntegrationTests.dll --no-ansi --progress off --minimum-expected-tests 6 --timeout 10m
  ```

  Expected: Release 构建 `0` warnings、`0` errors；四套测试总计 65、失败 0、跳过 0。

- [ ] **Step 4: 检查最终 Git 与分支状态**

  Run:

  ```powershell
  git status --short
  git branch --list
  git log --oneline -10
  ```

  Expected: 工作区无变更，本地只存在 `main`，最近提交包含设计、契约、Skill、治理和完成计划。
