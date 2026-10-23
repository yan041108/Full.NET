# Full.NET Admin Form Designer

`@fullnet/admin-form-designer` 是管理端可复用的表单设计器宿主，不属于 Workflow 模块。

当前实现仍是基于 VForm3 3.0.10 JSON/交互模型重写的 ESM 安全子集，提供 `getFormJson` / `setFormJson` 接口，尚未完整对齐原版。

2026-10-23 用户确定的新目标是：先完整原样迁入 VForm 3 源码，保留全部组件、布局、属性、预览、工具栏及无损设计 JSON，完成原版对齐后再二开。本包负责完整设计器/渲染器的通用宿主和 Web 运行域；Workflow 负责自己的设计资产、发布、授权和业务绑定。本包不定义 Workflow 审批语义。唯一活动入口见[完整迁入计划](../../docs/superpowers/plans/2026-10-23-vform3-full-source-migration.md)，本次文档更新不表示代码已经替换。

## 使用边界

- 完整原版设计与 Web 渲染能力用于 Vue 后台，不进入 Host.Api/Worker 的动态执行路径，也不直接安装到 uni-app/小程序或 Flutter。
- 完整设计 JSON 是设计资产的权威内容，使用专门契约无损持久化；业务校验模型和授权规则另行生成。禁止先压缩为简化业务 Schema 再反推设计稿。
- 设计范围以固定上游提交全部功能为准，不按当前 Workflow 业务字段目录截断组件、布局或属性；未就绪的生产业务映射明确诊断并保留原稿。
- 脚本、样式、事件和相关编辑器保留，通过独立隔离 Web 运行域运行设计/预览；该域不能获得管理端凭据、宿主 DOM/存储或无约束平台 API。
- 采用完整源码适配现有工具链，不以有已知画布问题的旧 UMD 或自研精简面板作为最终替代。保留原版功能，必要差异记录来源与理由。
- 上游来源固定到提交 `c67479e496bab56a93a3dff168a4f529d8293c67`，自定义许可与作者声明必须持续保留，详见 `vendor/vform3/PROVENANCE.md`。
