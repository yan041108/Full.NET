# Settings 字典项双端 UI 纵向切片

> **For agents:** 使用 [`fullnet-module-delivery`](../../../.agents/skills/fullnet-module-delivery/SKILL.md)。后端字典项 API 已在父切片交付；本切片只补齐双端 UI、契约守卫与 Mock parity E2E。

- 建立日期：2026-07-25
- 状态：**Build-verified**
- 父切片：[`2026-07-25-settings-dictionary-vertical-slice.md`](2026-07-25-settings-dictionary-vertical-slice.md)
- 验证记录将增补至：[`settings-dictionary-2026-07-25.md`](../../verification/settings-dictionary-2026-07-25.md)

**Goal:** Host 管理员在选定字典类型后，于 Vue/Layui 双端维护字典项（列表/创建/更新 Label/禁用）；`Value` 创建后不可变。

**Architecture:** 复用既有 `/api/v1/settings/dict-types/{typeId}/items` 与 `/api/v1/settings/dict-items/{id}`；不新增导航节点，在字典类型页内嵌管理面板。

**非目标：** 租户级字典、颜色选择器高级组件、真实栈 E2E（脚本可后补；本机缺容器）、L5 字典文本翻译。

## 任务

1. [x] `client-contracts`：`SettingsDictItem` / Page / Create / Update 守卫 + 测试（contracts **40**）
2. [x] `admin-i18n`：`dictItems.*` 中英
3. [x] Vue：`dict-types` API 扩展 + `DictTypesView` 选型后项管理面板 + API 单测
4. [x] Layui：`dict-types.js` 选型/项 CRUD + `index.html` 面板 + 控制器单测
5. [x] shell-parity：「字典项列表、创建与禁用在两端保持一致」**2/2**
6. [x] 更新验证记录与 `adminnet-feature-parity` / capability 缺口说明
