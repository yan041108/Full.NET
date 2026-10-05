#if FULLNET_AOT_COMPILE || FULLNET_AI_ROW_READER_TESTS
using System.Data.Common;
using System.Globalization;

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
        registrar.Register<AiKnowledgeBaseRecord>(ReadKnowledgeBase);
        registrar.Register<AiKnowledgeDocumentRecord>(ReadKnowledgeDocument);
        registrar.Register<AiKnowledgeImportRecord>(ReadKnowledgeImport);
        registrar.Register<AiKnowledgeVersionRecord>(ReadKnowledgeVersion);
        registrar.Register<AiKnowledgeIndexTaskRecord>(ReadKnowledgeIndexTask);
        registrar.Register<AiKnowledgeIndexWorkerRecord>(ReadKnowledgeWorker);
        registrar.Register<AiKnowledgeIndexDispatchRecord>(ReadKnowledgeIndexDispatch);
        registrar.Register<AiKnowledgeIndexBatchRecord>(ReadKnowledgeIndexBatch);
        registrar.Register<AiKnowledgeIndexSessionRecord>(ReadKnowledgeIndexSession);
        registrar.Register<AiKnowledgeIndexVectorRecord>(ReadKnowledgeIndexVector);
        registrar.Register<AiKnowledgeIndexTextManifestRecord>(ReadKnowledgeTextManifest);
        registrar.Register<AiKnowledgeIndexVectorReceiptRecord>(ReadKnowledgeVectorReceipt);
        registrar.Register<AiKnowledgeIndexActivationRecord>(ReadKnowledgeActivation);
        registrar.Register<AiModelConfigRecord>(ReadModelConfig);
    }
#endif

    // 后台入口只恢复作用域和来源标识，不读取会话密钥、正文或模型凭据。
    internal static AiKnowledgeIndexWorkerRecord ReadKnowledgeWorker(DbDataReader reader) => new()
    {
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

    // 派发快照来自有效租约，固定来源元数据与模型版本，不物化正文或凭据。
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
        FileId = reader.GetGuid(reader.GetOrdinal("FileId")),
        ContentHash = Text(reader, "ContentHash"),
        ContentType = Text(reader, "ContentType"),
        SizeBytes = Integer(reader, "SizeBytes"),
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
    internal static AiKnowledgeVersionRecord ReadKnowledgeVersion(DbDataReader reader) => new()
    {
        Id = reader.GetGuid(reader.GetOrdinal("Id")),
        DocumentId = reader.GetGuid(reader.GetOrdinal("DocumentId")),
        FileId = reader.GetGuid(reader.GetOrdinal("FileId")),
        VersionNumber = checked((int)Integer(reader, "VersionNumber")),
        OriginalFileName = Text(reader, "OriginalFileName"),
        ContentType = Text(reader, "ContentType"),
        ContentHash = Text(reader, "ContentHash"),
        SizeBytes = Integer(reader, "SizeBytes"),
        UploadedByUserId = reader.GetGuid(reader.GetOrdinal("UploadedByUserId")),
        CreatedAtUtc = Timestamp(reader, "CreatedAtUtc"),
    };

    internal static AiKnowledgeImportRecord ReadKnowledgeImport(DbDataReader reader) => new()
    {
        Id = reader.GetGuid(reader.GetOrdinal("Id")),
        DocumentId = reader.GetGuid(reader.GetOrdinal("DocumentId")),
        VersionId = reader.GetGuid(reader.GetOrdinal("VersionId")),
        RequestKey = reader.GetGuid(reader.GetOrdinal("RequestKey")),
        ExpectedDocumentVersion = checked((int)Integer(reader, "ExpectedDocumentVersion")),
        OriginalFileName = Text(reader, "OriginalFileName"),
        ContentType = Text(reader, "ContentType"),
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
