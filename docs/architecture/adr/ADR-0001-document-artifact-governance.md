# ADR-0001：文档产物采用双层强制分层治理

- 状态：已批准
- 日期：2026-07-18
- 决策者：项目所有者在当前任务中明确确认
- 适用范围：Full.NET 仓库内的架构评估、设计规格、重大决策、实施计划和验证记录

## 背景

Full.NET 已经同时使用 `docs/verification/`、`docs/superpowers/specs/` 和 `docs/superpowers/plans/`，但此前没有一条所有后续会话都会读取的统一规则，明确区分“评估建议”“已批准基线”“单项重大决策”“实施步骤”和“完成证据”。如果只依赖目录习惯，后续任务可能把评估报告误当成架构批准、把计划勾选误当成实现证据，或创建多份相互竞争的设计事实源。

## 候选方案

### 方案一：只修改 `rules/development-quality.md`

详细规则集中，变更最小；但根入口不突出，后续会话可能只知道需要阅读开发质量规则，却不能在任务开始阶段主动识别文档分类义务。

### 方案二：根入口加详细规则，采用双层强制

在 `AGENTS.md` 增加所有任务可见的强制入口，在 `rules/development-quality.md` 维护单一详细定义。入口负责发现，详细规则负责路径职责、状态流转、冲突和验证，避免在两处复制完整规则。

### 方案三：新增独立 `rules/documentation-governance.md`

主题隔离最强，但当前文档治理规则规模较小；新增必须读取的规则文件会增加入口和维护成本，也容易与 `development-quality.md` 的文档完成定义重复。

## 决策

采用方案二：双层强制。

1. 根 `AGENTS.md` 在每项任务开始前要求识别文档产物分层，并链接到详细规则。
2. `rules/development-quality.md` 第 12.1 节是分层职责和状态流转的唯一规则事实源。
3. `docs/verification/` 保存评估、审查、实验和验证事实，不自动改变已批准架构。
4. `docs/superpowers/specs/` 保存经明确确认的长期设计和架构基线。
5. `docs/architecture/adr/` 保存重大单项决策的上下文、备选方案、取舍和替代关系。
6. `docs/superpowers/plans/` 只分解已批准 Spec 或 ADR，不充当设计批准或完成证据。

## 后果

正面后果：

- 后续会话从根规则即可发现分层要求；
- 评估、批准、计划、实施和验证具有明确状态流；
- ADR 保留重大决策的原因，而 Spec 继续保持面向当前基线的完整描述；
- 计划勾选和文档声明不能替代新鲜验证证据。

成本与限制：

- 文档任务必须先判断产物类型和授权状态；
- 重大决策需要同时维护 ADR 与受影响 Spec 摘要；
- 目录正确不代表状态正确，仍需检查批准和验证元数据。

## 验证

- 根 `AGENTS.md` 必须包含指向 `rules/development-quality.md` 第 12.1 节的入口；
- 详细规则必须同时覆盖 `docs/verification/`、`docs/superpowers/specs/`、`docs/architecture/adr/` 和 `docs/superpowers/plans/`；
- 新计划必须能链接到已批准 Spec 或 ADR；
- `git diff --check` 必须无空白错误。
