# Notifications 通知模板多语言验证记录（任务 13）

> 日期：2026-09-05  
> 状态：`Build-verified`；双库 Integration、页面真实栈 E2E 与 Linux Native AOT 以目标提交 GitHub Actions 为最终门禁。  
> 任务基线：`11083159618b981c5dcd0ccee840547ae60965a5`

## 1. 交付边界

本切片为通知模板引入 BCP 47 语言变体、按收件人偏好选取与缺失语言提示：

- **数据**：迁移 117 为 `fn_notifications_template` / `fn_notifications_template_version` 增加 `LocaleTag`、`DefaultLocaleTag`；唯一键扩展为 `(TenantScopeKey, TemplateKey, LocaleTag)`；存量回填 `zh-CN`
- **解析**：`NotificationTemplateLocaleResolver` 规范化受支持标签、回退链与缺失语言计算
- **选取**：`NotificationTemplateSelector` 按偏好语言与默认语言挑选已发布变体；收件人目录返回 `PreferredLocale`
- **投递**：站内信投影按收件人语言渲染；外部渠道仍绑定默认语言 `TemplateVersionId`（保留边界）
- **API**：`NotificationTemplateResponse` 增加 `localeTag`、`defaultLocaleTag`、`publishedLocaleTags`、`missingLocaleTags`；创建请求可选 `localeTag` / `defaultLocaleTag`
- **Vue**：模板页支持语言版本创建、列表展示语言标签、选中后展示已发布/缺失语言提示

## 2. 本地新鲜证据

| 验证 | 结果 |
| --- | --- |
| Notifications 单元测试（`FullyQualifiedName~Notifications`） | 98 项通过，0 失败 |
| `NotificationTemplatesView` Vitest | 6 项通过，0 失败 |
| `notifications-templates-intents` OpenAPI 夹具测试 | 通过 |
| OpenAPI 客户端生成 | 通过 |
| Notifications 模块 `dotnet build` | 通过 |

## 3. 保留边界

- 页面真实栈 E2E、双库 Integration、Linux Native AOT 与人工验收未在本切片执行。
- 非 inbox 外部渠道在 prepare 阶段仍使用默认语言模板版本；仅 inbox 按收件人偏好投影。
- `FindTemplateByKey` 在多语言并存时语义模糊，新流程应使用 `NotificationTemplateSelector`。

规则演进结论：未命中规则升级候选。Skill 演进结论：沿用 `fullnet-module-delivery`，无新 Skill 缺口。
