# CodeGeneration Host Apply 验证记录

## 结论

Host 代码生成工作台已完成受控本地 Apply 纵向切片，并保持 `Build-verified`：请求只接受成功且来源于持久化模板的 `previewRunId`，服务端重新生成并复核 Schema/Manifest 摘要后，才可写入运维配置的本地工作区。功能默认关闭，具有独立权限、单进程互斥、Manifest 所有权与恢复语义；运行记录先写 `apply/running`，再以双库单行保护 SQL 收敛到 `succeeded/failed`。

本切片仅覆盖 Apply 首交付边界；不含 Worker/队列化执行或模块接入。后续演进已独立交付：产品 Rollback（见 [codegeneration-product-rollback-2026-08-02.md](codegeneration-product-rollback-2026-08-02.md)）、多实例互斥、远程 Git、检查点保留与生产 Helm 启用路径。

## 安全与兼容边界

- API 只接收 `previewRunId`，客户端不能提交路径、内联 Schema、源码或目标仓库地址。
- Apply 默认禁用；启用时工作区必须由 `CodeGeneration:Apply:WorkspaceRoot` 明确配置并通过启动校验。
- 模板删除、版本或摘要漂移、工作区冲突和并发 Apply 均 fail-closed；响应和历史不暴露绝对路径、Schema、源码或异常正文。
- Vue 与 Layui 仅在具有独立 Apply 权限且最近一次模板预览仍有效时展示操作，必须显式确认；输入变化会使已审查预览失效。
- `046_CodeGenerationApply` 同步扩展 SQL Server/MySQL 的运行状态约束，并具备中断恢复验证。

## 新鲜验证证据

| 验证 | 结果 |
| --- | --- |
| `CodeGenerationApplyServiceTests` + `CodeGenerationRunServiceTests` | 15/15 |
| Integration Release build | 0 warning / 0 error |
| SQL Server/MySQL CodeGeneration API | 2/2 |
| SQL Server/MySQL 046 恢复 | 2/2 |
| Vue 聚焦测试 / Layui 聚焦测试 | 6/6 / 3/3 |
| Vue typecheck、Vue/Layui production build | 全部通过 |
| real-stack bootstrap contracts | 11/11 |
| Vue/Layui Host Apply 真实浏览器 E2E | 4/4 |
| `pnpm test:naming` | 24/24 |
| affected Integration inner / slice | 两阶段均为工具链 39/39、治理 16/16、Smoke 8/8、CodeGeneration + 046 + Realtime 去重聚焦 37/37 |
| fresh discovery | Unit 884；Integration 251（API SQL Server 43、API MySQL 43、migrations 82、infrastructure 83） |
| 测试矩阵契约 | 4/4 |

真实浏览器 E2E 使用操作系统临时目录，Apply 后直接读取 `.fullnet/codegeneration-manifest.json`，并校验代表性客户端产物的 SHA-256 与 Manifest 一致；受限 Host 账号直接调用 Apply 返回 `403 authorization.permission_denied`。测试结束后临时工作区、测试 runner、SQL Server/MySQL/Ryuk/Testcontainers 容器残留均为 0。

完整 Unit 与完整 Integration 集合未在本地重复执行；依照仓库分层测试策略，完整集合继续由 `main` CI 的互斥分片门禁负责。

## 治理复盘

本次没有命中新的通用规则或 Skill 缺口。046 条件约束修复 DDL 已按现有命名治理机制登记精确、非通配的静态扫描债务，不扩张规则或 Skill。
