namespace Full.NET.Modules.Workflow.Persistence;

/// <summary>不透明附件身份与安全名称快照，不包含签发方文件Id或存储定位。</summary>
internal sealed record WorkflowRemoteAttachmentNameRecord(Guid FileId, string OriginalFileName);
