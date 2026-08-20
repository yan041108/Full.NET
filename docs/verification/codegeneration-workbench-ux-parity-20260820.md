# CodeGeneration 工作台 UX 对标验证（2026-08-20）

**状态：** Build-verified（工作台 UX 对齐；未升 Verified）  
**基线：** `main` @ `ce7dddf6`  
**快照：** `codegen-ux-parity-20260820`  
**Spec / Plan：**  
- [`docs/superpowers/specs/2026-08-20-codegeneration-workbench-ux-parity-design.md`](../superpowers/specs/2026-08-20-codegeneration-workbench-ux-parity-design.md)  
- [`docs/superpowers/plans/2026-08-20-codegeneration-workbench-ux-parity.md`](../superpowers/plans/2026-08-20-codegeneration-workbench-ux-parity.md)

## 交付摘要

| 项 | 结果 |
| --- | --- |
| 模板列表筛选 `name` / `tableName` | 已交付（双库 JSON 路径过滤） |
| 模板复制 | Vue `create` + 名称后缀，无新表/权限 |
| Templates Tabs（基础/能力/列/关系/JSON） | 已交付；列网格补齐 sortable / queryKind / importExport |
| Previews `?templateId=` 深链 | 已交付 |
| `integrationTarget` 可视化表单 | 已交付（Apply 可选） |
| runs Action 目录 execute/apply/rollback | 已登记 |
| 路线图状态 | 保持 **Build-verified** |

## 明确未做

- Dict/FK/Upload 等扩展 controlKind、多库定位器、打印、ReZero、Layui、升 Verified

## 验证证据

- Unit：模板筛选 SQL/参数与 Authorization Actions（本波 `CodeGenerationTemplate*` + Authorization filter 通过）；`eng/testing/test-matrix.json` unit minimum `1469` → `1472`
- Architecture（inner 附带）：38/38
- Integration inner（快照 `codegen-ux-parity-20260820`，CodeGeneration MySQL 聚焦）：12/12
- Integration slice（同快照）：35/35
- E2E 规格：`host-code-generation-templates.spec.mjs` 增补 Vue UX 用例；本波不以完整双库 `test:e2e:real` 收口为 Verified 门槛
- `git diff --check`：无 whitespace error

## 规则 / Skill

未命中 rule-evolution / skill-evolution 触发条件；不更新规则或 Skill 候选。
