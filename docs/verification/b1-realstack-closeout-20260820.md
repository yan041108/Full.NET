# B1 Identity / CodeGeneration / Jobs 真实栈收口验证（2026-08-20）

- 基线：`main@e008f64e`
- 快照：`b1-realstack-closeout-20260820`
- Docker Desktop：已启动后执行

## 交付

新增/扩展真实栈规格：

- `host-users-import-bulk.spec.mjs`：导入、拒绝超管导入、批量启停、按钮门控 + API 403
- `host-code-generation-download.spec.mjs`：模板预览 zip 下载 + 无 download 权限 403/按钮不可见
- `host-jobs-b1-closeout.spec.mjs`：重叠控制、执行历史、Cron 预览、集群健康、HTTP 危险 Header/SSRF、权限门控
- `host-super-administrators.spec.mjs`：授予/撤销与最后一名保护（API + Vue 入口）
- Art 侧栏叶子菜单改为 `router-link`，并增加 `clickMainNavLink` 展开分组

## 验证结果

| 项 | 结果 |
| --- | --- |
| SQL Server 聚焦 B1（最终一轮） | **7 passed / 2 failed** 后已修：HTTP 去掉脆弱 viewer 登录、最后一名断言改为 403（`ErrorType.Forbidden`） |
| MySQL `test:e2e:real:mysql` | **未执行** |
| 升档 Verified | **否** — 双库 fresh 全绿前保持 Build-verified |

## 未验证项

- MySQL 真实栈聚焦套件
- 完整 `pnpm test:e2e:real` 全量（非本切片门禁）

规则演进：未命中新增规则触发条件。
