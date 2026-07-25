# Settings 数据字典真实栈 E2E 纵向切片

> 补齐父切片「Mock/真实栈 E2E」中真实栈缺口；本机缺容器时只交付脚本并登记未实跑。

- 建立日期：2026-07-25
- 状态：**Build-verified**（SQL Server + MySQL 真实栈聚焦各 **4/4**；Integration 双库 **2/2**）
- 父切片：[`2026-07-25-settings-dictionary-vertical-slice.md`](2026-07-25-settings-dictionary-vertical-slice.md)

## 任务

1. [x] `real-stack-auth.mjs`：`createSettingsDictTypeViaApi`、`createSettingsDictItemViaApi`
2. [x] `host-dict-types.spec.mjs`：管理员加载/创建类型与项；受限账号 API 403 + 导航裁剪 + 深链 403
3. [x] 真实栈门槛 **46 → 50**；更新验证记录
4. [x] SQL Server 真实栈聚焦实跑 **4/4**（2026-07-25）
5. [x] MySQL 真实栈聚焦实跑 **4/4**（2026-07-25）

**非目标：** 解除禁用引用真实栈、租户级字典。全量真实栈矩阵仍由 CI 覆盖。
