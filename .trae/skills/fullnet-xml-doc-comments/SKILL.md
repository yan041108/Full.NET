---
name: "fullnet-xml-doc-comments"
description: "为 Full.NET 项目按规范批量补充缺失的中文 XML 文档注释（summary/param/returns）并构建验证提交。Invoke 当用户要求补注释、补文档、补 XML 注释或复查注释缺口时使用。"
---

# Full.NET XML 文档注释批量补齐

适用于 Full.NET 仓库的三轮注释累计实践沉淀。核心目标：让项目所有 public class / interface / record / enum / struct / delegate 以及其关键方法/属性都具备规范的三斜杠 XML 文档注释，开发者通过 IntelliSense 即可理解设计意图、边界条件和不变量。

---

## 触发条件（Invoke when）

用户提出以下任一需求时调用本 Skill：

1. "补一轮注释"、"再加注释"、"补 XML 文档注释"、"补 <summary>"
2. "分析下还有哪些需要加注释方便读代码"
3. "复查注释缺口"、"扫描缺注释的类型"
4. 任何涉及 rules/code-comments.md 规范对齐的纯注释增补任务

---

## 前置依赖

- 仓库根目录存在 `rules/code-comments.md` 规范文档
- 工作区可执行 `dotnet build` （.NET SDK 与仓库 restore 缓存就绪）

---

## 执行流程（6 步）

### Step 1 — 精确扫描缺口（必须先做）

用 PowerShell 在 `src/` 下扫描所有 public 类型，检查声明行上方 300 字符内是否存在 `/// <summary>`。300 字符窗口很重要（200 对含多 `<param>` 的 record 会误判）。

```powershell
$files = Get-ChildItem -Path src -Recurse -Filter *.cs |
    Where-Object { $_.FullName -notmatch '\\obj\\|\\bin\\' }
$noSummary = @()
foreach ($f in $files) {
    $content = Get-Content $f.FullName -Raw
    $typeLines = [regex]::Matches($content,
        '(?m)^(\s*)public\s+(?:(?:sealed|abstract|static)\s+)?' +
        '(?:class|interface|record(?:<[^>]+>)?|enum|struct|delegate)\s+([A-Z][A-Za-z0-9_]*)')
    foreach ($m in $typeLines) {
        $typeName = $m.Groups[2].Value
        $idx = $m.Index
        $before = $content.Substring(
            [Math]::Max(0, $idx-300),
            [Math]::Min(300, $idx))
        if ($before -notmatch '///\s*<summary>') {
            $noSummary += [PSCustomObject]@{ File=$f.FullName; Type=$typeName }
        }
    }
}
$noSummary | Group-Object File | Sort-Object Count -Descending |
    Select-Object -First 30 | Format-Table Count, Name
```

输出按文件聚合的 Top 30 缺口清单，据此分批次（每批 5-10 个任务，每批 50-100 个类型）。

### Step 2 — 分批并行分派

按**缺口密度**从高到低分批，用 `general_purpose_task` 子代理并行执行。每批的子代理任务描述必须明确：

1. **目标文件清单**（完整绝对路径）
2. **操作原则**：先 Read 再补；只补缺 `<summary>` 的，绝不覆盖已有完整注释；若已有 `<summary>` 但缺 `<param>/<returns>` 且为公开契约/方法则补参数
3. **注释规范**：
   - 中文清晰说明**意图、边界、不变量、风险**四要素
   - 专业术语保留英文（Outbox、Inbox、CAS、CDC、FAIL-closed、SignalR、Debezium、FusionCache 等）
   - 契约 record 强调机器码稳定性：字段顺序、枚举数值、权限码字符串、错误码前缀发布后不可改名/删除，新增只能追加
   - record 主构造函数参数逐个补 `<param name="...">值含义</param>`
   - 接口方法补完整 `<summary>` + 每个显式参数 `<param>` + `<returns>` + `<exception>`（如有）+ `<remarks>`（说明幂等/并发/原子提交/交付保证）
   - 枚举成员逐个补 `<summary>`，枚举类顶部加 `<summary>` 说明"机器码顺序不变"

4. **子代理任务规模**：每子代理处理 10-30 个文件，避免写回失败。

推荐分批划分模板：

| 批次 | 目标区域 |
|------|----------|
| 1 | Top 缺口最大的 Contracts 文件（如 Document/Jobs/Messaging/…Contracts.cs） |
| 2 | Identity.Contracts + Tenancy/Settings/Auditing 等模块 Contracts + Port 接口 |
| 3 | BuildingBlocks Data.Abstractions + Abstractions 根项目散点 + Data.Dapper 散点 |
| 4 | Messaging.Abstractions + Messaging.Kafka + Seeding + DbUp + Caching + Hosting Forwarding/RateLimit |
| 5 | Data.CodeGeneration Schema/Generation/Integration + CodeGeneration 模块散点 |
| 6 | Modularity 接口、实时/本地化/验证/序列化、模块权限码/错误码/配置常量 |

### Step 3 — 子代理安全边界

严格限制子代理只做 XML 注释增补，不做代码变更：

- **禁止**在补注释时一并"顺手修代码"。若发现真实代码错误（如编译错误、逻辑 Bug），记录后由主代理在 Step 5 单独修复，并在提交说明中显式列出
- **禁止**重命名、重排、删除任何符号
- **允许**补 `<inheritdoc/>` 作为接口实现类上的简化注释（当接口注释完整时）

### Step 4 — 全量构建验证

至少验证以下 6 个入口项目：

```bash
dotnet build src/Hosts/Full.NET.Host.Api/Full.NET.Host.Api.csproj      --no-restore
dotnet build src/Hosts/Full.NET.Host.Worker/Full.NET.Host.Worker.csproj  --no-restore
dotnet build src/Hosts/Full.NET.Host.Migrator/Full.NET.Host.Migrator.csproj --no-restore
dotnet build src/Tools/Full.NET.CodeGeneration.Cli/Full.NET.CodeGeneration.Cli.csproj --no-restore
dotnet build src/Tools/Full.NET.Messaging.Cli/Full.NET.Messaging.Cli.csproj   --no-restore
dotnet build src/Generators/Full.NET.Messaging.Generators/Full.NET.Messaging.Generators.csproj --no-restore
```

**注意**：并行构建会因 Composition 共享 dll 文件锁竞争出现偶发 MSB3026/CS2012，非代码问题，失败项单独重跑即可。

构建结果判定标准：
- 0 **错误** = 合格
- 若 0 警告则标注"0W 0E"；有 XML 相关警告（CS1572/CS1574/CS1734）则逐一修正
- 若出现 CS1503/StringComparer vs StringComparison 这类代码错误，按 Step 5 处理

### Step 5 — 代码错误修复（仅当子代理触发或发现遗留 Bug 时）

任何非注释修改都必须：

1. 单独 Read 相关文件定位根因
2. 用最小 diff 修复（一个参数、一个枚举名）
3. 在最终提交信息的"修复"章节逐条列出
4. 重跑相关项目构建验证

### Step 6 — 提交

```bash
git add src/
git commit -m "docs: 第N轮补全 XML 文档注释（主题）" \
    -m "【A 区块】概要 + 不变量说明" \
    -m "【B 区块】概要 + 风险说明" \
    -m "【修复】逐项列出所有非注释变更" \
    -m "验证：6 个入口项目构建 0W 0E。"
```

提交信息主题应包含本轮重点区域（如 "Contracts 大文件与 BuildingBlocks 散点"）。

---

## 常见风险与应对

| 风险 | 表现 | 应对 |
|------|------|------|
| 扫描 200 字符窗口误判 | record 含 <remarks>+多 <param> 时被算"缺 summary" | 扫描窗口 ≥ 300 字符 |
| 子代理越界写代码 | 新增方法体、改动逻辑而非注释 | 主代理抽查 3-5 个关键文件 diff |
| cref 引用不存在 | CS1574 警告 | 构建开启警告输出，逐条修正或改为 `<c>TypeName</c>` |
| 并行构建文件锁 | CS2012/MSB3026 | 串行重跑失败项 |
| 子代理声称改动但未落盘 | git diff --stat 无输出 | 主代理直接 Read 关键文件头 40 行核对 |
| 子代理重复补已有注释 | 覆盖原有文字 | 任务描述强制"已有完整注释绝不覆盖"；主 diff 审查只加不减 |

---

## 注释内容优先级（四选三原则）

当注释空间有限（如简单 DTO）时按此取舍：

1. ✅ **机器码稳定性声明** — 契约/枚举/权限码必写
2. ✅ **幂等语义** — 写操作接口必写（重放重复调用何副作用）
3. ✅ **安全边界** — SQL Scope、租户越权、CAS 并发守卫、最后一名保护
4. ⭕ **性能/内存特性** — 迭代器/非线程安全/IAsyncEnumerable 等酌情

---

## 质量验收清单

- [ ] `git diff -- src/` 全为纯注释（XML 三斜杠），例外项在 commit message "修复"区列出
- [ ] 随机抽查 5 个 Contract record：每个字段都有 `<param>`，顶部有 `<summary>` + 机器码稳定性 `<remarks>`
- [ ] 随机抽查 3 个 Service 接口方法：有 `<summary>` + 全部 `<param>` + `<returns>`
- [ ] 随机抽查 3 个 enum：枚举类 + 每个成员都有 `<summary>`
- [ ] 6 个入口项目全部 dotnet build 0 错误
- [ ] `git status` 干净或暂存正确
