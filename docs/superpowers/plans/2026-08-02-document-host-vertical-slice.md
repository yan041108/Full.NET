# Document Host 文档库纵向切片实施计划

> **For agents:** 使用 [`fullnet-module-delivery`](../../../.agents/skills/fullnet-module-delivery/SKILL.md)。**Gate G4 批准前不得创建模块代码。** 行为变更必须先失败测试再实现。

- 建立日期：2026-08-02
- 状态：**待 Gate G4 批准规格后开工**
- 设计规格：[`2026-08-02-document-module-design.md`](../specs/2026-08-02-document-module-design.md)
- 建议快照：`document-host-vertical-slice-20260802`
- 预期迁移号：**053**（开工前必须现场确认两库空闲）

**Goal:** Host 管理员维护文档元数据、分类/标签、版本链（引用 Files `FileId`）、软删除/恢复与基础 ACL；不存储文件字节。

**Architecture:** 单主项目 `Full.NET.Modules.Document`（首切片不拆 Contracts 程序集，公开 DTO 放 `Contracts/` 目录）；表 `fn_document_*`；权限 `document.host_documents.*`；API `/api/v1/document/host/...`。

**Tech Stack:** DbUp 053 双库迁移、Dapper、ProblemDetails、Files Host API 引用、Vue/Layui 同步、Playwright 真实栈。

---

## 前置门禁

1. [`2026-08-02-document-module-design.md`](../specs/2026-08-02-document-module-design.md) Gate G4 批准，路线图 `Mapped` → `Planned`。
2. Files Host 上传 `Build-verified`（`pending→ready` 状态机已闭合）。
3. `pnpm test:task:start -- document-host-vertical-slice-20260802` 创建快照。

---

## 切片范围

### 必须交付（Task A–F）

| Task | 内容 | 验证 |
| --- | --- | --- |
| A | 双库迁移 `053_DocumentHostFoundation.sql`：`item`、`version`、`category`、`tag`、`tag_assignment`；**首切片可推迟 `acl`/`share_link` 表至 Task B** | Migration053 恢复测试 ×2 |
| B | Host 文档 CRUD + 版本绑定 `fileId` + 软删/恢复 API | Integration SQL Server/MySQL |
| C | 分类/标签只读+维护 API | Integration + OpenAPI |
| D | 权限 Contributor、导航、`document.host_documents.read/write/delete` | Architecture + 403 用例 |
| E | Vue/Layui 列表/详情/上传新版本流程 | 客户端单测 + parity |
| F | 真实栈 E2E `host-documents` SQL Server + MySQL | `admin-real-stack` |

### 明确非目标（本切片）

- 租户文档库、外链分享、预览转换、病毒扫描、ACL 细粒度 UI（若 Task A 未建 ACL 表则全局权限即可）。
- Document 模块内 JOIN `fn_files_file`；任何 Blob 列。

---

## Task A：迁移与 RED（示例顺序）

1. 确认迁移号 `053` 空闲；编写 SQL Server/MySQL 配对脚本。
2. RED：`Migration053DocumentHostFoundationRecoveryTests`（半完成索引/列、二次运行、数据保留）。
3. GREEN：迁移脚本 + DbUp 登记 + `test-matrix.json` 选择器 `053`。
4. Architecture：Document 模块不得引用 `Full.NET.Data.SqlServer` / `MySql` 驱动。

---

## Task B：文档与版本 API

1. RED：Integration `Host_document_items_follow_contract_with_sql_server/mysql`。
2. 流程：创建 item → Files 上传得 `fileId` → `POST .../versions` → 列表/详情含 `currentVersion`。
3. 软删/恢复；版本号单调；损坏/越权 `fileId` 拒绝。
4. JSON 源生成 Context 登记。

---

## Task C–F：概要

- **C：** 分类树与标签字典；名称唯一约束（未删除行）。
- **D：** `DocumentAuthorizationContributor`；Endpoint `RequireAuthorization`；种子角色不含写权限的 403 测试。
- **E：** 路由 `/document/host-items`；与 `@fullnet/client-contracts` 导航同步。
- **F：** E2E 创建→版本→删→恢复→受限账号 403。

---

## 完成定义

- affected merge 双库非零发现全绿。
- `pnpm test:naming`、`test:sql-safety`、Architecture、OpenAPI、Clients 通过。
- `capability-status.md` 更新为 `Build-verified`（非 `Verified`，除非 E2E 与生产边界齐备）。
- 单切片单提交；禁止与 Workflow/其他大型模块混交。

---

## 停止条件

- 规格未批准即开工 → 停止。
- Files 引用需 JOIN 业务表才能工作 → 停止，先补 Files 只读端口契约。
- 迁移号冲突 → 停止并重新协调，禁止抢号。