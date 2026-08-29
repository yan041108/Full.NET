# Current Tenant 写能力收敛验证

## 范围与基线

- 基线提交：`0b94c29d325bceb4adac35e1fcff3943e0a10964`
- 日期：2026-08-29
- 范围：`CurrentTenantAccessor` 公共写入面、Scoped DI 别名和生产写入消费者。
- 非目标：不改变租户解析顺序、授权规则、状态恢复、数据库访问或 Outbox/Jobs 语义。

## 设计

- `ICurrentTenant` 保持普通业务只读契约。
- 新增 `ICurrentTenantContextWriter`，仅表达建立、切换和清理可信上下文的能力。
- `CurrentTenantAccessor` 继续承载唯一 Scoped 状态，但 `SetTenant`、`SetHost`、`Clear` 不再是 public concrete API；生产代码通过显式接口调用。
- `ICurrentTenant` 与 `ICurrentTenantContextWriter` 都解析到同一个 `CurrentTenantAccessor` 实例，不复制状态。
- 请求租户解析、Worker、Migrator、消费调度、后台维护和已授权的 Host 跨租户组织编排保留写能力；Architecture Tests 冻结精确文件清单。

## 兼容性说明

这是 pre-v1 安全边界的有意公共 API 收窄：仓库外若直接构造 `CurrentTenantAccessor` 并调用 Setter，需要改为由 DI 获取 `ICurrentTenantContextWriter`。只依赖 `ICurrentTenant` 的业务代码不受影响。测试程序集通过 friend assembly 驱动具体状态，生产程序集不获得该访问权限。

## 验证目标

- Unit：concrete 不再暴露三个 public Setter；写接口与只读接口观察同一状态；Migrator profile 同时注册读写能力。
- Architecture：生产代码仅 Tenancy 注册点和 accessor 实现可引用 concrete；写能力使用点必须与精确 allowlist 完全一致。
- Native AOT：接口为静态闭合 DI 注册，不使用反射发现、动态代码或运行时程序集扫描。

## 验证结果

- 受影响 Unit：71/71 通过，覆盖请求解析、消费调度、Outbox、Jobs、租户状态与 DI profile。
- Architecture 聚焦：50/50 通过；`api-native-aot` 选择集 71/71 通过。
- Governance：52/52 通过。
- `pnpm test:aot:analyzers`：0 警告、0 错误。
- `pnpm test:inner -- --base 0b94c29d325bceb4adac35e1fcff3943e0a10964`：Release 构建 0 警告、0 错误；受影响 MySQL Integration 65/65 通过。
- `pnpm test:aot:publish:linux`：Docker Linux SDK 完成真实 `linux-x64` Native AOT 链接，warning gate 接受 9 个既有精确告警；原生可执行文件 72,114,192 bytes。
- `pnpm test:aot:native:e2e`：Windows 发现门禁 19 项，19 项均按规则跳过；本地结果不证明 Linux 原生进程交互，等待 GitHub Actions Linux E2E。

功能与性能语义未变，不声明延迟、吞吐或分配提升。当前证据可声明 `Aot-analysis-clean` 与本地 `linux-x64` publish 成功，但不能把 Windows discovery 表述为 `Aot-published` 运行时闭环。

## 演进检查

本切片落实已有租户可信来源和 Native AOT 静态闭包规则，并新增可执行架构门禁；没有出现新的规则冲突或 Skill 流程缺口，不修改规则或 Skill。
