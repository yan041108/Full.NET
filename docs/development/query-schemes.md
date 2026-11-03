# 查询方案与个人常用入口

企业申请列表可保存筛选条件、排序和列展示。个人方案按账号、Host/租户上下文及 Grid 隔离；首次进入列表应用当前用户默认方案。每个上下文/Grid 最多 32 个个人方案，名称最多 64 字符。

拥有对应 `settings.query_schemes.*` 权限后可新建、更新、删除和设默认；租户共享另需 `settings.query_schemes.share`。只有创建者能修改或撤销自己的共享方案。选用共享方案只复用条件，业务读取仍检查当前成员、租户、机构与字段权限。共享被撤销、来源不可访问或 Schema 变化时，旧默认显示失效，可清除默认或由本人修正；版本冲突先刷新，不自动重放修改。

业务模块通过 `IQuerySchemeContributor` 注册稳定 GridKey、SchemaVersion、精确读取权限、支持的上下文和筛选/排序/列白名单，并声明 Settings 依赖。Settings 只保存经过验证的配置，不解释业务 SQL。首个目录为 `enterprise_request.enterprise_requests`，只支持 Tenant；查询仍调用企业申请原列表接口，人工页面通过 HTTP 适配器传递条件，不修改生成文件。

接入字段时同时维护服务端目录与客户端校验；字段删除或语义改变应提高 SchemaVersion。配置上限为 16 KiB、8 个筛选、32 列；当前运算符仅 `contains`，列键、顺序、宽度与固定位置均需通过校验。API 位于 `/api/v1/me/query-schemes/{gridKey}`，来源读取权限与方案操作权限分别检查。

全局搜索中的“常用入口”仅保存当前浏览器的稳定路由名，最多 12 个，按账号与 Host/租户隔离。每次展示和跳转都与当前可见导航取交集；不保存任意 URL、查询参数或对象 ID，不宣称跨设备同步。

聚焦验收与限制见 [F18 验证记录](../verification/2026-11-03-query-schemes.md)。
