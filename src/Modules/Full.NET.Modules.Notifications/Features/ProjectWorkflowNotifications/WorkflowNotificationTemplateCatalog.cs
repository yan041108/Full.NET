using Full.NET.Modules.Notifications.Contracts;
using Full.NET.Localization;

namespace Full.NET.Modules.Notifications.Features.ProjectWorkflowNotifications;

/// <summary>工作流系统内建站内信模板目录；只保存安全默认内容，不替代租户后续发布的自定义版本。</summary>
internal static class WorkflowNotificationTemplateCatalog
{
    private static readonly IReadOnlyDictionary<string, WorkflowNotificationTemplateDefinition> Definitions =
        new Dictionary<string, WorkflowNotificationTemplateDefinition>(StringComparer.Ordinal)
        {
            ["workflow.activity.completed"] = Create("workflow.activity.completed", "工作流活动已完成",
                "业务 {businessType}（{businessId}）的一个工作流活动已完成。", includeTodoId: false),
            ["workflow.activity.rejected"] = Create("workflow.activity.rejected", "工作流活动已驳回",
                "业务 {businessType}（{businessId}）的一个工作流活动已驳回。", includeTodoId: false),
            ["workflow.todo.manual_reminder"] = Create("workflow.todo.manual_reminder", "工作流催办提醒",
                "发起人提醒您处理业务 {businessType}（{businessId}）的工作流待办。", includeTodoId: true, includeManualSequence: true),
            ["workflow.todo.assigned"] = Create(
                "workflow.todo.assigned",
                "您有新的审批待办",
                "业务 {businessType}（{businessId}）有新的审批待办，请及时处理。",
                includeTodoId: true),
            ["workflow.todo.reminder"] = Create(
                "workflow.todo.reminder",
                "审批待办催办",
                "业务 {businessType}（{businessId}）的审批待办已逾期，请及时处理。",
                includeTodoId: true,
                includeReminderSequence: true),
            ["workflow.todo.escalation"] = Create(
                "workflow.todo.escalation",
                "审批待办升级提醒",
                "业务 {businessType}（{businessId}）的审批待办已达到升级时限。",
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

    /// <summary>闭合英文变体与默认变体具有相同参数边界，管理员已有内容不由系统覆盖。</summary>
    public static WorkflowNotificationTemplateDefinition English(WorkflowNotificationTemplateDefinition definition)
    {
        var (subject, body) = definition.TemplateKey switch
        {
            "workflow.activity.completed" => ("Workflow activity completed", "A workflow activity for business {businessType} ({businessId}) has completed."),
            "workflow.activity.rejected" => ("Workflow activity rejected", "A workflow activity for business {businessType} ({businessId}) was rejected."),
            "workflow.todo.assigned" => ("New workflow task", "Business {businessType} ({businessId}) has a new workflow task for you."),
            "workflow.todo.reminder" => ("Overdue workflow task", "Your workflow task for business {businessType} ({businessId}) is overdue."),
            "workflow.todo.manual_reminder" => ("Workflow reminder", "The initiator reminds you to handle the workflow task for business {businessType} ({businessId})."),
            "workflow.todo.escalation" => ("Workflow task escalation", "The workflow task for business {businessType} ({businessId}) has reached its escalation deadline."),
            "workflow.instance.completed" => ("Workflow completed", "The workflow for business {businessType} ({businessId}) has completed."),
            "workflow.instance.rejected" => ("Workflow rejected", "The workflow for business {businessType} ({businessId}) was rejected."),
            "workflow.instance.cancelled" => ("Workflow cancelled", "The workflow for business {businessType} ({businessId}) was cancelled."),
            _ => throw new InvalidOperationException("notifications.template_not_found"),
        };
        return definition with { LocaleTag = LocaleCatalog.English, Subject = subject, Body = new(body) };
    }

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
        bool includeTodoId,
        bool includeReminderSequence = false,
        bool includeManualSequence = false)
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

        if (includeReminderSequence)
        {
            parameters.Add(new NotificationTemplateParameterDefinition("reminderSequence", "integer", true, null));
        }

        if (includeManualSequence)
        {
            parameters.Add(new("manualRequestId", "string", true, 36));
            parameters.Add(new("manualSequence", "integer", true, null));
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
    NotificationTemplateParameterSchema ParameterSchema,
    string LocaleTag = LocaleCatalog.DefaultLocale);
