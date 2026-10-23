#if FULLNET_AOT_COMPILE || FULLNET_AI_ROW_READER_TESTS
using System.Data.Common;
using System.Globalization;
using Full.NET.Agents.Runtime;
using Full.NET.Modules.Ai.Features.IncrementalKnowledgeIndexing;
using Full.NET.Modules.Ai.Contracts;

namespace Full.NET.Modules.Ai.Persistence;

/// <summary>仅进入原生编译闭包；测试链接同一源文件，普通业务模块不引入 ADO.NET 依赖。</summary>
internal static class AiBudgetRowReaders
{
#if FULLNET_AOT_COMPILE
    internal static void Register()
    {
        var registrar = new Full.NET.Data.Dapper.DapperAotMaterializerRegistrar();
        registrar.Register<AiOperationBudgetRecord>(ReadOperation);
        registrar.Register<AiOperationBudgetTotals>(ReadTotals);
        registrar.Register<AiModelPriceRecord>(ReadPrice);
        registrar.Register<AiTenantQuotaRecord>(ReadQuota);
        registrar.Register<AiMcpRemoteToolRecord>(ReadMcpRemoteTool);
        registrar.Register<AiAgentApprovalRecord>(ReadAgentApproval);
        registrar.Register<AiAgentRunRecord>(ReadAgentRun);
        registrar.Register<AgentCheckpointRecord>(ReadAgentCheckpoint);
        registrar.Register<AiKnowledgeBaseRecord>(ReadKnowledgeBase);
        registrar.Register<AiKnowledgeDocumentRecord>(ReadKnowledgeDocument);
        registrar.Register<AiKnowledgeImportRecord>(ReadKnowledgeImport);
        registrar.Register<AiKnowledgeVersionRecord>(ReadKnowledgeVersion);
        registrar.Register<AiKnowledgeSourceConfirmed>(ReadKnowledgeSourceConfirmed);
        registrar.Register<AiKnowledgeIndexChangeItem>(ReadKnowledgeIndexChange);
        registrar.Register<AiKnowledgeIndexGrantRecord>(ReadKnowledgeIndexGrant);
        registrar.Register<AiKnowledgeIndexGrantAnchor>(ReadKnowledgeIndexGrantAnchor);
        registrar.Register<AiKnowledgeIndexTaskRecord>(ReadKnowledgeIndexTask);
        registrar.Register<AiKnowledgeIndexEnqueueRecord>(ReadKnowledgeIndexEnqueue);
        registrar.Register<AiKnowledgeIndexStatusRecord>(ReadKnowledgeIndexStatus);
        registrar.Register<AiKnowledgeIndexWorkerRecord>(ReadKnowledgeWorker);
        registrar.Register<AiKnowledgeCleanupRecord>(ReadKnowledgeCleanup);
        registrar.Register<AiKnowledgeIndexDispatchRecord>(ReadKnowledgeIndexDispatch);
        registrar.Register<AiKnowledgeIndexBatchRecord>(ReadKnowledgeIndexBatch);
        registrar.Register<AiKnowledgeIndexSessionRecord>(ReadKnowledgeIndexSession);
        registrar.Register<AiKnowledgeIndexVectorRecord>(ReadKnowledgeIndexVector);
        registrar.Register<AiKnowledgeIndexTextManifestRecord>(ReadKnowledgeTextManifest);
        registrar.Register<AiKnowledgeIndexVectorReceiptRecord>(ReadKnowledgeVectorReceipt);
        registrar.Register<AiKnowledgeIndexActivationRecord>(ReadKnowledgeActivation);
        registrar.Register<AiModelConfigRecord>(ReadModelConfig);
        registrar.Register<AiPromptRecord>(ReadPrompt);
        registrar.Register<AiPromptVersionRecord>(ReadPromptVersion);
        registrar.Register<AiChatMessageRecord>(ReadChatMessage);
        registrar.Register<AiKnowledgeAnswerFeedbackRecord>(ReadAnswerFeedback);
        registrar.Register<AiKnowledgeAnswerFeedbackReviewRecord>(ReadAnswerFeedbackReview);
        registrar.Register<AiChatSessionRecord>(ReadChatSession);
    }
#endif

    /// <summary>运行完整投影与领取租约的四列投影共用静态物化，保留作用域、代次和原始绑定快照。</summary>
    internal static AiAgentRunRecord ReadAgentRun(DbDataReader reader)
    {
        var row = new AiAgentRunRecord
        {
            Id = reader.GetGuid(reader.GetOrdinal("Id")), Version = Integer(reader, "Version"),
            LeaseEpoch = Integer(reader, "LeaseEpoch"),
            LeaseExpiresAtUtc = Null(reader, "LeaseExpiresAtUtc") ? null : Timestamp(reader, "LeaseExpiresAtUtc")
        };
        // SelectLease 只返回四个 fencing 字段；完整查询的其余必需列不得静默缺省。
        if (reader.FieldCount == 4) return row;
        row.ScopeKey = Text(reader, "ScopeKey");
        row.TenantId = Null(reader, "TenantId") ? null : reader.GetGuid(reader.GetOrdinal("TenantId"));
        row.ActorUserId = reader.GetGuid(reader.GetOrdinal("ActorUserId"));
        row.SessionId = reader.GetGuid(reader.GetOrdinal("SessionId"));
        row.SessionKind = Text(reader, "SessionKind");
        row.DefinitionKey = Text(reader, "DefinitionKey");
        row.DefinitionVersion = checked((int)Integer(reader, "DefinitionVersion"));
        row.AuthorizationBindingId = reader.GetGuid(reader.GetOrdinal("AuthorizationBindingId"));
        row.SecurityStamp = Text(reader, "SecurityStamp");
        row.ActorScope = Text(reader, "ActorScope");
        row.EffectiveScope = Text(reader, "EffectiveScope");
        row.StatusKey = Text(reader, "StatusKey");
        row.BudgetJson = Text(reader, "BudgetJson");
        row.TraceParent = Null(reader, "TraceParent") ? null : Text(reader, "TraceParent");
        row.DeadlineAtUtc = Timestamp(reader, "DeadlineAtUtc");
        row.LeaseOwner = Null(reader, "LeaseOwner") ? null : Text(reader, "LeaseOwner");
        row.CreatedAtUtc = Timestamp(reader, "CreatedAtUtc");
        row.UpdatedAtUtc = Timestamp(reader, "UpdatedAtUtc");
        return row;
    }

    /// <summary>检查点保留原始密文、摘要与版本，由协调器校验后恢复，不在物化阶段改写。</summary>
    internal static AgentCheckpointRecord ReadAgentCheckpoint(DbDataReader reader) => new(
        reader.GetGuid(reader.GetOrdinal("CheckpointId")), reader.GetGuid(reader.GetOrdinal("RunId")),
        Integer(reader, "Sequence"), checked((int)Integer(reader, "FormatVersion")), Text(reader, "FrameworkVersion"),
        checked((int)Integer(reader, "DefinitionVersion")), Text(reader, "PayloadProtected"), Text(reader, "Checksum"));

    /// <summary>目录物化保留可信作用域与可空指针；MySQL 使用网络字节序 UUID。</summary>
    internal static AiPromptRecord ReadPrompt(DbDataReader reader) => new()
    {
        Id = reader.GetGuid(reader.GetOrdinal("Id")), ScopeKey = Text(reader, "ScopeKey"),
        TenantId = Null(reader, "TenantId") ? null : reader.GetGuid(reader.GetOrdinal("TenantId")),
        OwnerUserId = reader.GetGuid(reader.GetOrdinal("OwnerUserId")), ClientRequestId = reader.GetGuid(reader.GetOrdinal("ClientRequestId")),
        CreateHash = Text(reader, "CreateHash"), Name = Text(reader, "Name"),
        Revision = checked((int)Integer(reader, "Revision")), LastVersion = checked((int)Integer(reader, "LastVersion")),
        ActiveVersionId = Null(reader, "ActiveVersionId") ? null : reader.GetGuid(reader.GetOrdinal("ActiveVersionId")),
        Epoch = checked((int)Integer(reader, "Epoch")), CreatedAtUtc = Timestamp(reader, "CreatedAtUtc")
    };

    /// <summary>版本按原始 JSON 和精确摘要物化，不在 Reader 中修复或截断载荷。</summary>
    internal static AiPromptVersionRecord ReadPromptVersion(DbDataReader reader) => new()
    {
        Id = reader.GetGuid(reader.GetOrdinal("Id")), PromptId = reader.GetGuid(reader.GetOrdinal("PromptId")),
        ScopeKey = Text(reader, "ScopeKey"), TenantId = Null(reader, "TenantId") ? null : reader.GetGuid(reader.GetOrdinal("TenantId")),
        Version = checked((int)Integer(reader, "Version")), FormatVersion = checked((int)Integer(reader, "FormatVersion")),
        BodyJson = Text(reader, "BodyJson"), ModelConfigVersion = checked((int)Integer(reader, "ModelConfigVersion")),
        ProviderKey = Text(reader, "ProviderKey"), ModelId = Text(reader, "ModelId"), BodyHash = Text(reader, "BodyHash"),
        WasPublished = Integer(reader, "WasPublished") != 0, CreatedAtUtc = Timestamp(reader, "CreatedAtUtc")
    };

    /// <summary>标记独立于可空元数据读取，缺失元数据不能把派生正文降级为普通消息。</summary>
    internal static AiChatMessageRecord ReadChatMessage(DbDataReader reader) => new()
    {
        Id = reader.GetGuid(reader.GetOrdinal("Id")), SessionId = reader.GetGuid(reader.GetOrdinal("SessionId")),
        RoleKey = Text(reader, "RoleKey"), Content = Text(reader, "Content"), StatusKey = Text(reader, "StatusKey"),
        PromptTokens = Null(reader, "PromptTokens") ? null : checked((int)Integer(reader, "PromptTokens")),
        CompletionTokens = Null(reader, "CompletionTokens") ? null : checked((int)Integer(reader, "CompletionTokens")),
        CreatedAtUtc = Timestamp(reader, "CreatedAtUtc"), IsKnowledgeAnswer = Convert.ToBoolean(reader.GetValue(reader.GetOrdinal("IsKnowledgeAnswer")), CultureInfo.InvariantCulture),
        KnowledgeLineage = Null(reader, "KnowledgeLineage") ? null : Text(reader, "KnowledgeLineage"),
    };

    /// <summary>反馈静态物化，显式处理双库 UTC 与撤回字段空值。</summary>
    internal static AiKnowledgeAnswerFeedbackRecord ReadAnswerFeedback(DbDataReader reader) => new()
    {
        Id = reader.GetGuid(reader.GetOrdinal("Id")), MessageId = reader.GetGuid(reader.GetOrdinal("MessageId")),
        RatingKey = Null(reader, "RatingKey") ? null : Text(reader, "RatingKey"),
        ReasonKey = Null(reader, "ReasonKey") ? null : Text(reader, "ReasonKey"),
        CitationIdsJson = Text(reader, "CitationIdsJson"), LineageHash = Null(reader, "LineageHash") ? null : Text(reader, "LineageHash"),
        StatusKey = Text(reader, "StatusKey"), Version = checked((int)Integer(reader, "Version")), UpdatedAtUtc = Timestamp(reader, "UpdatedAtUtc"),
    };

    /// <summary>审核 JOIN 静态物化；未审核的 LEFT JOIN 字段保留空值，不能降级版本。</summary>
    internal static AiKnowledgeAnswerFeedbackReviewRecord ReadAnswerFeedbackReview(DbDataReader reader) => new()
    {
        FeedbackId = reader.GetGuid(reader.GetOrdinal("FeedbackId")), SessionId = reader.GetGuid(reader.GetOrdinal("SessionId")),
        MessageId = reader.GetGuid(reader.GetOrdinal("MessageId")), OwnerUserId = reader.GetGuid(reader.GetOrdinal("OwnerUserId")),
        RatingKey = Text(reader, "RatingKey"), ReasonKey = Text(reader, "ReasonKey"), CitationIdsJson = Text(reader, "CitationIdsJson"),
        LineageHash = Text(reader, "LineageHash"), FeedbackVersion = checked((int)Integer(reader, "FeedbackVersion")), KnowledgeLineage = Text(reader, "KnowledgeLineage"),
        ReviewFeedbackVersion = Null(reader, "ReviewFeedbackVersion") ? null : checked((int)Integer(reader, "ReviewFeedbackVersion")),
        ReviewLineageHash = Null(reader, "ReviewLineageHash") ? null : Text(reader, "ReviewLineageHash"),
        ReviewerUserId = Null(reader, "ReviewerUserId") ? null : reader.GetGuid(reader.GetOrdinal("ReviewerUserId")),
        DecisionKey = Null(reader, "DecisionKey") ? null : Text(reader, "DecisionKey"),
        ReviewVersion = Null(reader, "ReviewVersion") ? null : checked((int)Integer(reader, "ReviewVersion")),
    };

    /// <summary>会话所有权与租约静态物化，保持 Host 空租户和两库 UTC 类型。</summary>
    internal static AiChatSessionRecord ReadChatSession(DbDataReader reader) => new()
    {
        Id = reader.GetGuid(reader.GetOrdinal("Id")), OwnerUserId = reader.GetGuid(reader.GetOrdinal("OwnerUserId")),
        TenantId = Null(reader, "TenantId") ? null : reader.GetGuid(reader.GetOrdinal("TenantId")),
        ModelConfigId = reader.GetGuid(reader.GetOrdinal("ModelConfigId")), ModelName = Text(reader, "ModelName"), Title = Text(reader, "Title"),
        MessageCount = checked((int)Integer(reader, "MessageCount")), Version = checked((int)Integer(reader, "Version")),
        LastMessageAtUtc = Null(reader, "LastMessageAtUtc") ? null : Timestamp(reader, "LastMessageAtUtc"),
        IsGenerating = Convert.ToBoolean(reader.GetValue(reader.GetOrdinal("IsGenerating")), CultureInfo.InvariantCulture),
        CreatedAtUtc = Timestamp(reader, "CreatedAtUtc"), UpdatedAtUtc = Null(reader, "UpdatedAtUtc") ? null : Timestamp(reader, "UpdatedAtUtc"),
        GenerationId = Null(reader, "GenerationId") ? null : reader.GetGuid(reader.GetOrdinal("GenerationId")),
        GenerationExpiresAtUtc = Null(reader, "GenerationExpiresAtUtc") ? null : Timestamp(reader, "GenerationExpiresAtUtc"),
        GenerationCancellationRequested = Convert.ToBoolean(reader.GetValue(reader.GetOrdinal("GenerationCancellationRequested")), CultureInfo.InvariantCulture),
    };

    // LEFT JOIN 的任务、模型和指针允许为空；分类与生成批准列供处理策略复核，不在这里做业务判定。
    internal static AiKnowledgeIndexStatusRecord ReadKnowledgeIndexStatus(DbDataReader reader) => new()
    {
        KnowledgeBaseId = reader.GetGuid(reader.GetOrdinal("KnowledgeBaseId")), DocumentId = reader.GetGuid(reader.GetOrdinal("DocumentId")),
        VersionId = reader.GetGuid(reader.GetOrdinal("VersionId")), OwnerUserId = reader.GetGuid(reader.GetOrdinal("OwnerUserId")),
        DocumentVersion = checked((int)Integer(reader, "DocumentVersion")), KnowledgeBaseVersion = checked((int)Integer(reader, "KnowledgeBaseVersion")),
        DataClassification = Text(reader, "DataClassification"),
        GenerationModelConfigId = Null(reader, "GenerationModelConfigId") ? null : reader.GetGuid(reader.GetOrdinal("GenerationModelConfigId")),
        GenerationModelVersion = Null(reader, "GenerationModelVersion") ? null : checked((int)Integer(reader, "GenerationModelVersion")),
        IsDeleted = checked((int)Integer(reader, "IsDeleted")), CleanupPending = checked((int)Integer(reader, "CleanupPending")),
        HasDerivedData = checked((int)Integer(reader, "HasDerivedData")), IsCurrent = checked((int)Integer(reader, "IsCurrent")),
        TaskId = Null(reader, "TaskId") ? null : reader.GetGuid(reader.GetOrdinal("TaskId")), StateKey = Null(reader, "StateKey") ? null : Text(reader, "StateKey"),
        ActiveTaskId = Null(reader, "ActiveTaskId") ? null : reader.GetGuid(reader.GetOrdinal("ActiveTaskId")),
        ActiveLeaseToken = Null(reader, "ActiveLeaseToken") ? null : reader.GetGuid(reader.GetOrdinal("ActiveLeaseToken")),
        ActiveReceiptHash = Null(reader, "ActiveReceiptHash") ? null : Text(reader, "ActiveReceiptHash"),
        ActiveManifestHash = Null(reader, "ActiveManifestHash") ? null : Text(reader, "ActiveManifestHash"),
        ActivePlanHash = Null(reader, "ActivePlanHash") ? null : Text(reader, "ActivePlanHash"),
        FileId = reader.GetGuid(reader.GetOrdinal("FileId")), ContentHash = Text(reader, "ContentHash"), ContentType = Text(reader, "ContentType"), ParsingPolicyKey = Text(reader, "ParsingPolicyKey"), SizeBytes = Integer(reader, "SizeBytes"),
        ModelConfigId = Null(reader, "ModelConfigId") ? null : reader.GetGuid(reader.GetOrdinal("ModelConfigId")),
        ModelConfigVersion = Null(reader, "ModelConfigVersion") ? null : checked((int)Integer(reader, "ModelConfigVersion")),
        ProviderKey = Null(reader, "ProviderKey") ? null : Text(reader, "ProviderKey"), ModelId = Null(reader, "ModelId") ? null : Text(reader, "ModelId"),
    };

    // 入队从权威行推导来源及批准版本，不读取正文或冻结身份秘密。
    internal static AiKnowledgeIndexEnqueueRecord ReadKnowledgeIndexEnqueue(DbDataReader reader) => new()
    {
        KnowledgeBaseId = reader.GetGuid(reader.GetOrdinal("KnowledgeBaseId")), DocumentId = reader.GetGuid(reader.GetOrdinal("DocumentId")),
        VersionId = reader.GetGuid(reader.GetOrdinal("VersionId")), OwnerUserId = reader.GetGuid(reader.GetOrdinal("OwnerUserId")),
        DocumentVersion = checked((int)Integer(reader, "DocumentVersion")), KnowledgeBaseVersion = checked((int)Integer(reader, "KnowledgeBaseVersion")),
        ModelConfigId = reader.GetGuid(reader.GetOrdinal("ModelConfigId")), ModelConfigVersion = checked((int)Integer(reader, "ModelConfigVersion")),
        ContentType = Text(reader, "ContentType"), ParsingPolicyKey = Text(reader, "ParsingPolicyKey"),
    };

    // 清理不依赖会话；时间及 Kind 显式兼容两库的物理返回类型。
    internal static AiKnowledgeCleanupRecord ReadKnowledgeCleanup(DbDataReader reader) => new()
    {
        Id = reader.GetGuid(reader.GetOrdinal("Id")), Kind = checked((int)Integer(reader, "Kind")), CreatedAtUtc = Timestamp(reader, "CreatedAtUtc"),
        TenantId = Null(reader, "TenantId") ? null : reader.GetGuid(reader.GetOrdinal("TenantId")),
        KnowledgeBaseId = reader.GetGuid(reader.GetOrdinal("KnowledgeBaseId")), DocumentId = reader.GetGuid(reader.GetOrdinal("DocumentId")),
        OwnerUserId = reader.GetGuid(reader.GetOrdinal("OwnerUserId")),
    };

    // 后台入口只恢复作用域和来源标识，不读取会话密钥、正文或模型凭据。
    internal static AiKnowledgeIndexWorkerRecord ReadKnowledgeWorker(DbDataReader reader) => new()
    {
        ContentType = Text(reader, "ContentType"), ParsingPolicyKey = Text(reader, "ParsingPolicyKey"),
        TaskId = reader.GetGuid(reader.GetOrdinal("TaskId")), TenantId = Null(reader, "TenantId") ? null : reader.GetGuid(reader.GetOrdinal("TenantId")),
        KnowledgeBaseId = reader.GetGuid(reader.GetOrdinal("KnowledgeBaseId")), DocumentId = reader.GetGuid(reader.GetOrdinal("DocumentId")),
        VersionId = reader.GetGuid(reader.GetOrdinal("VersionId")), OwnerUserId = reader.GetGuid(reader.GetOrdinal("OwnerUserId")),
        DocumentVersion = checked((int)Integer(reader, "DocumentVersion")), KnowledgeBaseVersion = checked((int)Integer(reader, "KnowledgeBaseVersion")),
    };

    // 完成指针包含原始有界 JSON 与摘要，不读取文档正文、向量或身份令牌。
    internal static AiKnowledgeIndexActivationRecord ReadKnowledgeActivation(DbDataReader reader) => new()
    {
        Id = reader.GetGuid(reader.GetOrdinal("Id")), DocumentId = reader.GetGuid(reader.GetOrdinal("DocumentId")),
        IndexTaskId = reader.GetGuid(reader.GetOrdinal("IndexTaskId")), VersionId = reader.GetGuid(reader.GetOrdinal("VersionId")),
        PlanHash = Text(reader, "PlanHash"), ManifestHash = Text(reader, "ManifestHash"), ReceiptHash = Text(reader, "ReceiptHash"),
        ReceiptJson = Text(reader, "ReceiptJson"), LeaseToken = reader.GetGuid(reader.GetOrdinal("LeaseToken")), ActivatedAtUtc = Timestamp(reader, "ActivatedAtUtc"),
    };

    // 轻量回执仅物化稳定身份和摘要，不把整代次正文/向量带入集合复核。
    internal static AiKnowledgeIndexVectorReceiptRecord ReadKnowledgeVectorReceipt(DbDataReader reader) => new()
    {
        Id = reader.GetGuid(reader.GetOrdinal("Id")), BatchOrdinal = checked((int)Integer(reader, "BatchOrdinal")),
        RequestHash = Text(reader, "RequestHash"), InputCount = checked((int)Integer(reader, "InputCount")), PayloadHash = Text(reader, "PayloadHash"),
        ProviderKey = Text(reader, "ProviderKey"), ModelId = Text(reader, "ModelId"), Dimensions = checked((int)Integer(reader, "Dimensions")),
        FormatKey = Text(reader, "FormatKey"), DistanceKey = Text(reader, "DistanceKey"),
    };

    // 载荷和时间按静态路径读取，保持 SQL Server/MySQL 的物理类型差异显式可测。
    internal static AiKnowledgeIndexTextManifestRecord ReadKnowledgeTextManifest(DbDataReader reader) => new()
    {
        Id = reader.GetGuid(reader.GetOrdinal("Id")), FormatKey = Text(reader, "FormatKey"), PayloadHash = Text(reader, "PayloadHash"),
        Payload = reader.GetFieldValue<byte[]>(reader.GetOrdinal("Payload")), CreatedAtUtc = Timestamp(reader, "CreatedAtUtc"),
    };

    // 载荷按静态字节数组读取，模型/任务字段只由固定关联查询投影。
    internal static AiKnowledgeIndexVectorRecord ReadKnowledgeIndexVector(DbDataReader reader) => new()
    {
        Id = reader.GetGuid(reader.GetOrdinal("Id")), IndexTaskId = reader.GetGuid(reader.GetOrdinal("IndexTaskId")),
        BatchOrdinal = checked((int)Integer(reader, "BatchOrdinal")), RequestHash = Text(reader, "RequestHash"), InputCount = checked((int)Integer(reader, "InputCount")),
        ModelConfigId = reader.GetGuid(reader.GetOrdinal("ModelConfigId")), ModelConfigVersion = checked((int)Integer(reader, "ModelConfigVersion")),
        ProviderKey = Text(reader, "ProviderKey"), ModelId = Text(reader, "ModelId"), Dimensions = checked((int)Integer(reader, "Dimensions")),
        FormatKey = Text(reader, "FormatKey"), DistanceKey = Text(reader, "DistanceKey"), PayloadHash = Text(reader, "PayloadHash"),
        Payload = reader.GetFieldValue<byte[]>(reader.GetOrdinal("Payload")), CreatedAtUtc = Timestamp(reader, "CreatedAtUtc"),
    };

    // 原会话在恢复时保持冻结；可空租约只由关联任务投影，不物化令牌。
    internal static AiKnowledgeIndexSessionRecord ReadKnowledgeIndexSession(DbDataReader reader) => new()
    {
        Generation = checked((int)Integer(reader, "Generation")),
        Id = reader.GetGuid(reader.GetOrdinal("Id")), IndexTaskId = reader.GetGuid(reader.GetOrdinal("IndexTaskId")),
        ActorUserId = reader.GetGuid(reader.GetOrdinal("ActorUserId")),
        TenantId = Null(reader, "TenantId") ? null : reader.GetGuid(reader.GetOrdinal("TenantId")),
        SessionId = reader.GetGuid(reader.GetOrdinal("SessionId")), SessionKind = Text(reader, "SessionKind"),
        SecurityStamp = Text(reader, "SecurityStamp"), ActorScope = Text(reader, "ActorScope"), EffectiveScope = Text(reader, "EffectiveScope"),
        CreatedAtUtc = Timestamp(reader, "CreatedAtUtc"),
        LeaseExpiresAtUtc = Null(reader, "LeaseExpiresAtUtc") ? null : Timestamp(reader, "LeaseExpiresAtUtc"),
    };

    // 批次请求摘要和操作 ID 在租约接替后保持不变，不读取正文或凭据。
    internal static AiKnowledgeIndexBatchRecord ReadKnowledgeIndexBatch(DbDataReader reader) => new()
    {
        Id = reader.GetGuid(reader.GetOrdinal("Id")),
        IndexTaskId = reader.GetGuid(reader.GetOrdinal("IndexTaskId")),
        BatchOrdinal = checked((int)Integer(reader, "BatchOrdinal")),
        RequestHash = Text(reader, "RequestHash"),
        InputCount = checked((int)Integer(reader, "InputCount")),
        CreatedAtUtc = Timestamp(reader, "CreatedAtUtc"),
    };

    // 派发与激活共享固定投影；同次读取绑定来源、分类和模型目的地，不物化正文或凭据。
    internal static AiKnowledgeIndexDispatchRecord ReadKnowledgeIndexDispatch(DbDataReader reader) => new()
    {
        TaskId = reader.GetGuid(reader.GetOrdinal("TaskId")),
        DocumentId = reader.GetGuid(reader.GetOrdinal("DocumentId")),
        VersionId = reader.GetGuid(reader.GetOrdinal("VersionId")),
        DocumentVersion = checked((int)Integer(reader, "DocumentVersion")),
        KnowledgeBaseVersion = checked((int)Integer(reader, "KnowledgeBaseVersion")),
        ModelConfigId = reader.GetGuid(reader.GetOrdinal("ModelConfigId")),
        ModelConfigVersion = checked((int)Integer(reader, "ModelConfigVersion")),
        LeaseToken = reader.GetGuid(reader.GetOrdinal("LeaseToken")),
        LeaseExpiresAtUtc = Timestamp(reader, "LeaseExpiresAtUtc"),
        GrantExpiresAtUtc = Null(reader, "GrantExpiresAtUtc") ? null : Timestamp(reader, "GrantExpiresAtUtc"),
        FileId = reader.GetGuid(reader.GetOrdinal("FileId")),
        ContentHash = Text(reader, "ContentHash"),
        ContentType = Text(reader, "ContentType"), ParsingPolicyKey = Text(reader, "ParsingPolicyKey"),
        SizeBytes = Integer(reader, "SizeBytes"),
        DataClassification = Text(reader, "DataClassification"),
        ModelProviderKey = Text(reader, "ModelProviderKey"), ModelId = Text(reader, "ModelId"),
        ModelEndpointBaseUrl = Text(reader, "ModelEndpointBaseUrl"),
        ModelOrganizationId = Null(reader, "ModelOrganizationId") ? null : Text(reader, "ModelOrganizationId"),
    };

    // 任务记录不包含原文或凭据；固定列物化覆盖可空租约和终止状态。
    internal static AiKnowledgeIndexTaskRecord ReadKnowledgeIndexTask(DbDataReader reader) => new()
    {
        Id = reader.GetGuid(reader.GetOrdinal("Id")),
        DocumentId = reader.GetGuid(reader.GetOrdinal("DocumentId")),
        VersionId = reader.GetGuid(reader.GetOrdinal("VersionId")),
        DocumentVersion = checked((int)Integer(reader, "DocumentVersion")),
        KnowledgeBaseVersion = checked((int)Integer(reader, "KnowledgeBaseVersion")),
        ModelConfigId = reader.GetGuid(reader.GetOrdinal("ModelConfigId")),
        ModelConfigVersion = checked((int)Integer(reader, "ModelConfigVersion")),
        StateKey = Text(reader, "StateKey"),
        AttemptCount = checked((int)Integer(reader, "AttemptCount")),
        FailureCode = Null(reader, "FailureCode") ? null : Text(reader, "FailureCode"),
        LeaseToken = Null(reader, "LeaseToken") ? null : reader.GetGuid(reader.GetOrdinal("LeaseToken")),
        LeaseExpiresAtUtc = Null(reader, "LeaseExpiresAtUtc") ? null : Timestamp(reader, "LeaseExpiresAtUtc"),
        CreatedAtUtc = Timestamp(reader, "CreatedAtUtc"),
        UpdatedAtUtc = Timestamp(reader, "UpdatedAtUtc"),
    };

    // 知识库授权复用模型配置查询；依赖行也必须进入静态物化闭包。
    internal static AiModelConfigRecord ReadModelConfig(DbDataReader reader) => new()
    {
        Id = reader.GetGuid(reader.GetOrdinal("Id")),
        TenantId = Null(reader, "TenantId") ? null : reader.GetGuid(reader.GetOrdinal("TenantId")),
        Name = Text(reader, "Name"),
        ProviderKey = Text(reader, "ProviderKey"),
        EndpointBaseUrl = Text(reader, "EndpointBaseUrl"),
        ModelId = Text(reader, "ModelId"),
        ApiKeyProtected = Null(reader, "ApiKeyProtected") ? null : Text(reader, "ApiKeyProtected"),
        OrganizationId = Null(reader, "OrganizationId") ? null : Text(reader, "OrganizationId"),
        IsDefault = Convert.ToBoolean(reader.GetValue(reader.GetOrdinal("IsDefault")), CultureInfo.InvariantCulture),
        IsEnabled = Convert.ToBoolean(reader.GetValue(reader.GetOrdinal("IsEnabled")), CultureInfo.InvariantCulture),
        LastTestedAtUtc = Null(reader, "LastTestedAtUtc") ? null : Timestamp(reader, "LastTestedAtUtc"),
        LastTestStatusKey = Null(reader, "LastTestStatusKey") ? null : Text(reader, "LastTestStatusKey"),
        LastTestMessage = Null(reader, "LastTestMessage") ? null : Text(reader, "LastTestMessage"),
        CreatedAtUtc = Timestamp(reader, "CreatedAtUtc"),
        UpdatedAtUtc = Null(reader, "UpdatedAtUtc") ? null : Timestamp(reader, "UpdatedAtUtc"),
        Version = checked((int)Integer(reader, "Version")),
    };

    // 文件版本及恢复日志使用固定投影，原生路径禁止反射物化。
    /// <summary>恢复候选使用同一具体事件字段，不依赖反射构造或动态租户物化。</summary>
    internal static AiKnowledgeSourceConfirmed ReadKnowledgeSourceConfirmed(DbDataReader reader) => new(
        reader.GetGuid(reader.GetOrdinal("KnowledgeBaseId")), reader.GetGuid(reader.GetOrdinal("DocumentId")),
        reader.GetGuid(reader.GetOrdinal("VersionId")), reader.IsDBNull(reader.GetOrdinal("TenantId")) ? null : reader.GetGuid(reader.GetOrdinal("TenantId")));

    /// <summary>目录使用固定六列读取，不让公开记录依赖反射物化。</summary>
    internal static AiKnowledgeIndexChangeItem ReadKnowledgeIndexChange(DbDataReader reader) => new(
        reader.GetGuid(reader.GetOrdinal("DocumentId")), reader.GetString(reader.GetOrdinal("Title")),
        reader.GetGuid(reader.GetOrdinal("VersionId")), checked((int)Integer(reader, "VersionNumber")),
        Timestamp(reader, "CreatedAtUtc"), Timestamp(reader, "UpdatedAtUtc"));

    /// <summary>授权和任务累计额度使用固定列，原会话仅供服务端重新授权。</summary>
    internal static AiKnowledgeIndexGrantRecord ReadKnowledgeIndexGrant(DbDataReader reader) => new()
    {
        Id = reader.GetGuid(reader.GetOrdinal("Id")), TenantId = Null(reader, "TenantId") ? null : reader.GetGuid(reader.GetOrdinal("TenantId")),
        KnowledgeBaseId = reader.GetGuid(reader.GetOrdinal("KnowledgeBaseId")), OwnerUserId = reader.GetGuid(reader.GetOrdinal("OwnerUserId")),
        ClientRequestId = reader.GetGuid(reader.GetOrdinal("ClientRequestId")), KnowledgeBaseVersion = checked((int)Integer(reader, "KnowledgeBaseVersion")),
        ModelConfigId = reader.GetGuid(reader.GetOrdinal("ModelConfigId")), ModelConfigVersion = checked((int)Integer(reader, "ModelConfigVersion")),
        SessionJson = Text(reader, "SessionJson"), ExpiresAtUtc = Timestamp(reader, "ExpiresAtUtc"),
        DocumentLimit = checked((int)Integer(reader, "DocumentLimit")), AdmittedDocuments = checked((int)Integer(reader, "AdmittedDocuments")),
        InputTokenLimitPerDocument = Integer(reader, "InputTokenLimitPerDocument"),
        IsRevoked = Convert.ToBoolean(reader.GetValue(reader.GetOrdinal("IsRevoked")), CultureInfo.InvariantCulture),
        Version = checked((int)Integer(reader, "Version")), TaskId = Null(reader, "TaskId") ? null : reader.GetGuid(reader.GetOrdinal("TaskId")),
        LastBatchOrdinal = checked((int)Integer(reader, "LastBatchOrdinal")), ReservedInputTokens = Integer(reader, "ReservedInputTokens")
    };

    internal static AiKnowledgeIndexGrantAnchor ReadKnowledgeIndexGrantAnchor(DbDataReader reader) => new(
        reader.GetGuid(reader.GetOrdinal("Id")), Null(reader, "TenantId") ? null : reader.GetGuid(reader.GetOrdinal("TenantId")));

    internal static AiKnowledgeVersionRecord ReadKnowledgeVersion(DbDataReader reader) => new()
    {
        Id = reader.GetGuid(reader.GetOrdinal("Id")),
        DocumentId = reader.GetGuid(reader.GetOrdinal("DocumentId")),
        FileId = reader.GetGuid(reader.GetOrdinal("FileId")),
        VersionNumber = checked((int)Integer(reader, "VersionNumber")),
        OriginalFileName = Text(reader, "OriginalFileName"),
        ContentType = Text(reader, "ContentType"), ParsingPolicyKey = Text(reader, "ParsingPolicyKey"),
        ContentHash = Text(reader, "ContentHash"),
        SizeBytes = Integer(reader, "SizeBytes"),
        UploadedByUserId = reader.GetGuid(reader.GetOrdinal("UploadedByUserId")),
        CreatedAtUtc = Timestamp(reader, "CreatedAtUtc"),
        ProcessingStatus = Text(reader, "ProcessingStatus"),
    };

    internal static AiKnowledgeImportRecord ReadKnowledgeImport(DbDataReader reader) => new()
    {
        Id = reader.GetGuid(reader.GetOrdinal("Id")),
        DocumentId = reader.GetGuid(reader.GetOrdinal("DocumentId")),
        VersionId = reader.GetGuid(reader.GetOrdinal("VersionId")),
        RequestKey = reader.GetGuid(reader.GetOrdinal("RequestKey")),
        ExpectedDocumentVersion = checked((int)Integer(reader, "ExpectedDocumentVersion")),
        OriginalFileName = Text(reader, "OriginalFileName"),
        ContentType = Text(reader, "ContentType"), ParsingPolicyKey = Text(reader, "ParsingPolicyKey"),
        ContentHash = Text(reader, "ContentHash"),
        SizeBytes = Integer(reader, "SizeBytes"),
        ActorUserId = reader.GetGuid(reader.GetOrdinal("ActorUserId")),
        FileId = Null(reader, "FileId") ? null : reader.GetGuid(reader.GetOrdinal("FileId")),
        StateKey = Text(reader, "StateKey"),
        FailureCode = Null(reader, "FailureCode") ? null : Text(reader, "FailureCode"),
        LeaseToken = Null(reader, "LeaseToken") ? null : reader.GetGuid(reader.GetOrdinal("LeaseToken")),
        LeaseExpiresAtUtc = Null(reader, "LeaseExpiresAtUtc") ? null : Timestamp(reader, "LeaseExpiresAtUtc"),
        CreatedAtUtc = Timestamp(reader, "CreatedAtUtc"),
        UpdatedAtUtc = Timestamp(reader, "UpdatedAtUtc"),
    };

    // 草稿目录的原生物化只读明确投影，不使用反射回退。
    internal static AiKnowledgeDocumentRecord ReadKnowledgeDocument(DbDataReader reader) => new()
    {
        Id = reader.GetGuid(reader.GetOrdinal("Id")),
        KnowledgeBaseId = reader.GetGuid(reader.GetOrdinal("KnowledgeBaseId")),
        Title = Text(reader, "Title"),
        Description = Null(reader, "Description") ? null : Text(reader, "Description"),
        CreatedAtUtc = Timestamp(reader, "CreatedAtUtc"),
        UpdatedAtUtc = Null(reader, "UpdatedAtUtc") ? null : Timestamp(reader, "UpdatedAtUtc"),
        Version = checked((int)Integer(reader, "Version")),
    };

    internal static AiKnowledgeBaseRecord ReadKnowledgeBase(DbDataReader reader) => new()
    {
        Id = reader.GetGuid(reader.GetOrdinal("Id")),
        TenantId = Null(reader, "TenantId") ? null : reader.GetGuid(reader.GetOrdinal("TenantId")),
        OwnerUserId = reader.GetGuid(reader.GetOrdinal("OwnerUserId")),
        Name = Text(reader, "Name"),
        Description = Null(reader, "Description") ? null : Text(reader, "Description"),
        IsEnabled = Convert.ToBoolean(reader.GetValue(reader.GetOrdinal("IsEnabled")), CultureInfo.InvariantCulture),
        DataClassification = Text(reader, "DataClassification"),
        EmbeddingModelConfigId = Null(reader, "EmbeddingModelConfigId") ? null : reader.GetGuid(reader.GetOrdinal("EmbeddingModelConfigId")),
        EmbeddingModelVersion = Null(reader, "EmbeddingModelVersion") ? null : checked((int)Integer(reader, "EmbeddingModelVersion")),
        GenerationModelConfigId = Null(reader, "GenerationModelConfigId") ? null : reader.GetGuid(reader.GetOrdinal("GenerationModelConfigId")),
        GenerationModelVersion = Null(reader, "GenerationModelVersion") ? null : checked((int)Integer(reader, "GenerationModelVersion")),
        CreatedAtUtc = Timestamp(reader, "CreatedAtUtc"),
        UpdatedAtUtc = Null(reader, "UpdatedAtUtc") ? null : Timestamp(reader, "UpdatedAtUtc"),
        Version = checked((int)Integer(reader, "Version")),
    };

    internal static AiAgentApprovalRecord ReadAgentApproval(DbDataReader reader) => new()
    {
        Id = reader.GetGuid(reader.GetOrdinal("Id")),
        ScopeKey = Text(reader, "ScopeKey"),
        TenantId = Null(reader, "TenantId") ? null : reader.GetGuid(reader.GetOrdinal("TenantId")),
        RunId = reader.GetGuid(reader.GetOrdinal("RunId")),
        OperationId = reader.GetGuid(reader.GetOrdinal("OperationId")),
        SessionId = reader.GetGuid(reader.GetOrdinal("SessionId")),
        ToolName = Text(reader, "ToolName"),
        ToolVersion = checked((int)Integer(reader, "ToolVersion")),
        ArgumentsHash = Text(reader, "ArgumentsHash"),
        ArgumentsProtected = Text(reader, "ArgumentsProtected"),
        PolicyVersion = checked((int)Integer(reader, "PolicyVersion")),
        PresentationJson = Text(reader, "PresentationJson"),
        RequestedBy = reader.GetGuid(reader.GetOrdinal("RequestedBy")),
        ApproverId = Null(reader, "ApproverId") ? null : reader.GetGuid(reader.GetOrdinal("ApproverId")),
        DecisionKey = Text(reader, "DecisionKey"),
        ExpiresAtUtc = Timestamp(reader, "ExpiresAtUtc"),
        ConsumedAtUtc = Null(reader, "ConsumedAtUtc") ? null : Timestamp(reader, "ConsumedAtUtc"),
        Version = Integer(reader, "Version"),
        CreatedAtUtc = Timestamp(reader, "CreatedAtUtc"),
        UpdatedAtUtc = Timestamp(reader, "UpdatedAtUtc"),
    };

    internal static AiMcpRemoteToolRecord ReadMcpRemoteTool(DbDataReader reader) => new()
    {
        ConnectionId = reader.GetGuid(reader.GetOrdinal("ConnectionId")),
        ConnectionKey = Text(reader, "ConnectionKey"),
        EndpointUrl = Text(reader, "EndpointUrl"),
        LocalToolName = Text(reader, "LocalToolName"),
        RemoteToolName = Text(reader, "RemoteToolName"),
        ToolVersion = checked((int)Integer(reader, "ToolVersion")),
        InputSchemaJson = Text(reader, "InputSchemaJson"),
        InputSchemaHash = Text(reader, "InputSchemaHash"),
        SideEffectKey = Text(reader, "SideEffectKey"),
        PermissionCode = Text(reader, "PermissionCode"),
        ApprovalStatusKey = Text(reader, "ApprovalStatusKey"),
        ServiceTokenProtected = Text(reader, "ServiceTokenProtected"),
    };

    internal static AiOperationBudgetTotals ReadTotals(DbDataReader reader) => new()
    {
        MonthlyRequests = Integer(reader, "MonthlyRequests"), MonthlyTokens = Integer(reader, "MonthlyTokens"),
        RunRequests = Integer(reader, "RunRequests"), RunTokens = Integer(reader, "RunTokens")
    };

    internal static AiModelPriceRecord ReadPrice(DbDataReader reader) => new()
    {
        Id = reader.GetGuid(reader.GetOrdinal("Id")), Currency = Text(reader, "Currency"),
        InputPerMillion = Amount(reader, "InputPerMillion"), OutputPerMillion = Amount(reader, "OutputPerMillion"),
        CachedInputPerMillion = Amount(reader, "CachedInputPerMillion")
    };

    internal static AiOperationBudgetRecord ReadOperation(DbDataReader reader) => new()
    {
        Id = reader.GetGuid(reader.GetOrdinal("Id")), ModelConfigId = reader.GetGuid(reader.GetOrdinal("ModelConfigId")),
        RunId = Null(reader, "RunId") ? null : reader.GetGuid(reader.GetOrdinal("RunId")),
        RequestHash = Text(reader, "RequestHash"), QuotaMonthKey = Text(reader, "QuotaMonthKey"),
        ReservedTokens = Integer(reader, "ReservedTokens"), ReservedCost = Null(reader, "ReservedCost") ? null : Amount(reader, "ReservedCost"),
        Currency = Null(reader, "Currency") ? null : Text(reader, "Currency"),
        PriceVersionId = Null(reader, "PriceVersionId") ? null : reader.GetGuid(reader.GetOrdinal("PriceVersionId")),
        InputPerMillion = Null(reader, "InputPerMillion") ? null : Amount(reader, "InputPerMillion"),
        OutputPerMillion = Null(reader, "OutputPerMillion") ? null : Amount(reader, "OutputPerMillion"),
        CachedInputPerMillion = Null(reader, "CachedInputPerMillion") ? null : Amount(reader, "CachedInputPerMillion"),
        InputTokens = Null(reader, "InputTokens") ? null : Integer(reader, "InputTokens"),
        OutputTokens = Null(reader, "OutputTokens") ? null : Integer(reader, "OutputTokens"),
        CachedInputTokens = Null(reader, "CachedInputTokens") ? null : Integer(reader, "CachedInputTokens"),
        UsageStatus = Text(reader, "UsageStatus"), Outcome = Text(reader, "Outcome"),
        LegacyTracked = Convert.ToBoolean(reader.GetValue(reader.GetOrdinal("LegacyTracked")), CultureInfo.InvariantCulture)
    };

    private static AiTenantQuotaRecord ReadQuota(DbDataReader reader) => new()
    {
        Id = reader.GetGuid(reader.GetOrdinal("Id")), TenantId = reader.GetGuid(reader.GetOrdinal("TenantId")),
        MonthlyTokenLimit = Null(reader, "MonthlyTokenLimit") ? null : Integer(reader, "MonthlyTokenLimit"),
        MonthlyRequestLimit = Null(reader, "MonthlyRequestLimit") ? null : Integer(reader, "MonthlyRequestLimit"),
        UsedTokensThisMonth = Integer(reader, "UsedTokensThisMonth"), UsedRequestsThisMonth = Integer(reader, "UsedRequestsThisMonth"),
        QuotaMonthKey = Text(reader, "QuotaMonthKey"), IsEnabled = Convert.ToBoolean(reader.GetValue(reader.GetOrdinal("IsEnabled")), CultureInfo.InvariantCulture),
        CreatedAtUtc = Timestamp(reader, "CreatedAtUtc"), UpdatedAtUtc = Null(reader, "UpdatedAtUtc") ? null : Timestamp(reader, "UpdatedAtUtc"),
        Version = checked((int)Integer(reader, "Version"))
    };

    private static bool Null(DbDataReader reader, string name) => reader.IsDBNull(reader.GetOrdinal(name));
    private static string Text(DbDataReader reader, string name) => reader.GetString(reader.GetOrdinal(name));
    private static long Integer(DbDataReader reader, string name) => Convert.ToInt64(reader.GetValue(reader.GetOrdinal(name)), CultureInfo.InvariantCulture);
    private static decimal Amount(DbDataReader reader, string name) => reader.GetDecimal(reader.GetOrdinal(name));
    private static DateTimeOffset Timestamp(DbDataReader reader, string name) => reader.GetValue(reader.GetOrdinal(name)) switch
    {
        DateTimeOffset value => value,
        DateTime value => new DateTimeOffset(DateTime.SpecifyKind(value, DateTimeKind.Utc)),
        _ => throw new InvalidOperationException("Invalid AI budget timestamp type.")
    };
}
#endif
