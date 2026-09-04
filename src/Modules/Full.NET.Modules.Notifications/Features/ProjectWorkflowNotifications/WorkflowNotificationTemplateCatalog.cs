using Full.NET.Modules.Notifications.Contracts;

namespace Full.NET.Modules.Notifications.Features.ProjectWorkflowNotifications;

/// <summary>工作流系统内建站内信模板目录；只保存安全默认内容，不替代租户后续发布的自定义版本。</summary>
internal static class WorkflowNotificationTemplateCatalog
{
    private static readonly IReadOnlyDictionary<string, WorkflowNotificationTemplateDefinition> Definitions =
        new Dictionary<string, WorkflowNotificationTemplateDefinition>(StringComparer.Ordinal)
        {
            ["workflow.todo.assigned"] = Create(
                "workflow.todo.assigned",
                "您有新的审批待办",
                "业务 {businessType}（{businessId}）有新的审批待办，请及时处理。",
                includeTodoId: true),
            ["workflow.instance.completed"] = Create(
                "workflow.instance.completed",
                "审批已完成",
                "业务 {businessType}（{businessId}）的审批流程已完成。",
                includeTodoId: false),
            ["workflow.instance.rejected"] = Create(
                "workflow.instance.rejected",
                "审批已驳回",
                "业务 {businessType}（{businessId}）的审批流程已驳回。",
                includeTodoId: false),
            ["workflow.instance.cancelled"] = Create(
                "workflow.instance.cancelled",
                "审批已取消",
                "业务 {businessType}（{businessId}）的审批流程已取消。",
                includeTodoId: false),
        };

    /// <summary>按稳定模板键查找内建定义。</summary>
    /// <param name="templateKey">Workflow 事件映射产生的稳定模板键。</param>
    /// <param name="definition">找到时返回内建模板定义。</param>
    /// <returns>模板键属于闭合目录时返回 <see langword="true"/>。</returns>
    public static bool TryGet(
        string templateKey,
        out WorkflowNotificationTemplateDefinition? definition) =>
        Definitions.TryGetValue(templateKey, out definition);

    /// <summary>创建一条参数边界与 Workflow 事件载荷一致的模板定义。</summary>
    /// <param name="templateKey">稳定模板键。</param>
    /// <param name="subject">默认标题。</param>
    /// <param name="body">默认正文。</param>
    /// <param name="includeTodoId">是否要求待办标识参数。</param>
    /// <returns>可由 Notifications 编译器规范化的模板定义。</returns>
    private static WorkflowNotificationTemplateDefinition Create(
        string templateKey,
        string subject,
        string body,
        bool includeTodoId)
    {
        var parameters = new List<NotificationTemplateParameterDefinition>
        {
            new("instanceId", "string", true, 36),
            new("businessType", "string", true, 128),
            new("businessId", "string", true, 256),
        };
        if (includeTodoId)
        {
            parameters.Add(new NotificationTemplateParameterDefinition("todoId", "string", true, 36));
        }

        return new WorkflowNotificationTemplateDefinition(
            templateKey,
            subject,
            new NotificationTemplateBody(body),
            new NotificationTemplateParameterSchema(1, parameters));
    }
}

/// <summary>工作流内建模板的可编译内容。</summary>
/// <param name="TemplateKey">稳定模板键。</param>
/// <param name="Subject">默认标题。</param>
/// <param name="Body">默认正文。</param>
/// <param name="ParameterSchema">闭合参数模式。</param>
internal sealed record WorkflowNotificationTemplateDefinition(
    string TemplateKey,
    string Subject,
    NotificationTemplateBody Body,
    NotificationTemplateParameterSchema ParameterSchema);
