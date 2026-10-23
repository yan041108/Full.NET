using Full.NET.Data.Abstractions;

namespace Full.NET.Modules.Ai.Persistence;

/// <summary>持久数据仍受 AI 来源和原会话守卫；预算已结算不代替当前授权。</summary>
internal static class AiKnowledgeIndexDocumentVectorSql
{
    private const string Joins = " FROM fn_ai_knowledge_index_batch AS batch INNER JOIN fn_ai_knowledge_index_task ON fn_ai_knowledge_index_task.Id=batch.IndexTaskId INNER JOIN fn_ai_knowledge_index_session AS binding ON binding.IndexTaskId=fn_ai_knowledge_index_task.Id INNER JOIN fn_ai_operation_budget AS ledger ON ledger.Id=batch.Id";
    private const string Held = " WHERE fn_ai_knowledge_index_task.Id=@TaskId AND fn_ai_knowledge_index_task.StateKey='running' AND fn_ai_knowledge_index_task.LeaseToken=@LeaseToken AND fn_ai_knowledge_index_task.LeaseExpiresAtUtc>@Now AND batch.BatchOrdinal=@BatchOrdinal AND ledger.ScopeKey=@ScopeKey AND ledger.ModelConfigId=fn_ai_knowledge_index_task.ModelConfigId AND ledger.RequestHash=batch.RequestHash AND ledger.UsageStatus IN ('known','unknown') AND ledger.Outcome='succeeded' AND binding.ActorUserId=@OwnerUserId AND binding.SessionId=@SessionId AND binding.SessionKind=@SessionKind AND binding.SecurityStamp=@SecurityStamp AND binding.ActorScope=@ActorScope AND binding.EffectiveScope=@EffectiveScope AND EXISTS (SELECT 1 FROM fn_ai_knowledge_version AS source WHERE source.Id=fn_ai_knowledge_index_task.VersionId AND source.FileId=@FileId AND source.ContentHash=@ContentHash AND source.ContentType=@ContentType AND source.ParsingPolicyKey=@ParsingPolicyKey AND source.SizeBytes=@SizeBytes)";
    private const string Insert = "INSERT INTO fn_ai_knowledge_index_vector (Id,ProviderKey,ModelId,Dimensions,FormatKey,DistanceKey,PayloadHash,Payload,CreatedAtUtc) SELECT batch.Id,@ProviderKey,@ModelId,@Dimensions,@FormatKey,@DistanceKey,@PayloadHash,@Payload,@Now" + Joins + Held + " AND batch.Id=@OperationId AND batch.RequestHash=@RequestHash AND batch.InputCount=@InputCount AND ledger.ProviderKey=@ProviderKey AND ledger.ModelId=@ModelId";
    private const string Read = "SELECT vector.Id,batch.IndexTaskId,batch.BatchOrdinal,batch.RequestHash,batch.InputCount,fn_ai_knowledge_index_task.ModelConfigId,fn_ai_knowledge_index_task.ModelConfigVersion,vector.ProviderKey,vector.ModelId,vector.Dimensions,vector.FormatKey,vector.DistanceKey,vector.PayloadHash,vector.Payload,vector.CreatedAtUtc" + Joins + " INNER JOIN fn_ai_knowledge_index_vector AS vector ON vector.Id=batch.Id" + Held + " AND vector.ProviderKey=ledger.ProviderKey AND vector.ModelId=ledger.ModelId";
    private const string Host = " AND binding.TenantId IS NULL" + AiKnowledgeIndexTaskSql.OwnerExists + AiKnowledgeIndexTaskSql.Host + AiKnowledgeIndexTaskSql.Live + ")";
    private const string Tenant = " AND binding.TenantId=@TenantId" + AiKnowledgeIndexTaskSql.OwnerExists + AiKnowledgeIndexTaskSql.Tenant + AiKnowledgeIndexTaskSql.Live + ")";
    // 既有账本摘要使用数据库默认排序规则；SQL Server 列间比较显式采用批次的二进制规则。
    private const string SqlServerHash = "ledger.RequestHash COLLATE Latin1_General_100_BIN2=batch.RequestHash";

    //多格式文档必须绑定完整清单的实际原字节和已结算操作；不能只信任应用先前读取的摘要。
    private const string DocumentManifestHash = "CAST(manifest.PayloadHash AS BINARY)=CAST(@DocumentManifestHash AS BINARY) AND CAST(manifest.PayloadHash AS BINARY)=CAST(UPPER(SHA2(manifest.Payload,256)) AS BINARY)";
    private const string DocumentManifestSqlServerHash = "manifest.PayloadHash COLLATE Latin1_General_100_BIN2=@DocumentManifestHash AND manifest.PayloadHash COLLATE Latin1_General_100_BIN2=CONVERT(varchar(64),HASHBYTES('SHA2_256',manifest.Payload),2)";
    private const string DocumentHeld = " AND ledger.SettledAtUtc IS NOT NULL AND EXISTS (SELECT 1 FROM fn_ai_knowledge_document_manifest AS manifest WHERE manifest.Id=fn_ai_knowledge_index_task.Id AND CAST(manifest.FormatKey AS BINARY)=CAST('knowledge-document-manifest-v1' AS BINARY) AND OCTET_LENGTH(manifest.FormatKey)=30 AND OCTET_LENGTH(manifest.Payload) BETWEEN 8 AND 16777215 AND " + DocumentManifestHash + ")";
    private const string DocumentInsert = Insert + DocumentHeld + " AND CAST(@FormatKey AS BINARY)=CAST('knowledge-document-vector-batch-v1' AS BINARY) AND OCTET_LENGTH(@FormatKey)=34";
    private const string DocumentRead = Read + DocumentHeld + " AND CAST(vector.FormatKey AS BINARY)=CAST('knowledge-document-vector-batch-v1' AS BINARY) AND OCTET_LENGTH(vector.FormatKey)=34";
    private static string DocumentSqlServer(string sql) => sql.Replace("ledger.RequestHash=batch.RequestHash", SqlServerHash)
        .Replace(DocumentManifestHash, DocumentManifestSqlServerHash).Replace("OCTET_LENGTH(", "DATALENGTH(")
        .Replace("DATALENGTH(@FormatKey)", "DATALENGTH(CONVERT(varchar(64),@FormatKey))")
        .Replace("CAST(manifest.FormatKey AS BINARY)=CAST('knowledge-document-manifest-v1' AS BINARY)", "manifest.FormatKey COLLATE Latin1_General_100_BIN2='knowledge-document-manifest-v1'")
        .Replace("CAST(@FormatKey AS BINARY)=CAST('knowledge-document-vector-batch-v1' AS BINARY)", "@FormatKey COLLATE Latin1_General_100_BIN2='knowledge-document-vector-batch-v1'")
        .Replace("CAST(vector.FormatKey AS BINARY)=CAST('knowledge-document-vector-batch-v1' AS BINARY)", "vector.FormatKey COLLATE Latin1_General_100_BIN2='knowledge-document-vector-batch-v1'");
    internal static readonly SqlStatement InsertDocumentHost = new("ai.knowledge_index_document_vector.insert_host", DocumentInsert + Host + " FOR SHARE", SqlDataScope.HostOnly);
    internal static readonly SqlStatement InsertDocumentTenant = new("ai.knowledge_index_document_vector.insert_tenant", DocumentInsert + Tenant + " FOR SHARE", SqlDataScope.TenantRequired, SqlTenantBinding.CurrentTenantId);
    internal static readonly SqlStatement InsertDocumentSqlServerHost = new("ai.knowledge_index_document_vector.insert_sqlserver_host", DocumentSqlServer(DocumentInsert).Replace("INNER JOIN fn_ai_knowledge_index_task ON", "INNER JOIN fn_ai_knowledge_index_task WITH(UPDLOCK,HOLDLOCK) ON") + Host, SqlDataScope.HostOnly);
    internal static readonly SqlStatement InsertDocumentSqlServerTenant = new("ai.knowledge_index_document_vector.insert_sqlserver_tenant", DocumentSqlServer(DocumentInsert).Replace("INNER JOIN fn_ai_knowledge_index_task ON", "INNER JOIN fn_ai_knowledge_index_task WITH(UPDLOCK,HOLDLOCK) ON") + Tenant, SqlDataScope.TenantRequired, SqlTenantBinding.CurrentTenantId);
    internal static readonly SqlStatement FindDocumentHost = new("ai.knowledge_index_document_vector.find_host", DocumentRead + Host, SqlDataScope.HostOnly);
    internal static readonly SqlStatement FindDocumentTenant = new("ai.knowledge_index_document_vector.find_tenant", DocumentRead + Tenant, SqlDataScope.TenantRequired, SqlTenantBinding.CurrentTenantId);
    internal static readonly SqlStatement FindDocumentSqlServerHost = new("ai.knowledge_index_document_vector.find_sqlserver_host", DocumentSqlServer(DocumentRead) + Host, SqlDataScope.HostOnly);
    internal static readonly SqlStatement FindDocumentSqlServerTenant = new("ai.knowledge_index_document_vector.find_sqlserver_tenant", DocumentSqlServer(DocumentRead) + Tenant, SqlDataScope.TenantRequired, SqlTenantBinding.CurrentTenantId);

    // 读取上限加一；原始批次数也必须相等，未保存或未结算的额外批次不能被关联过滤隐藏。
    // 在最终数据库读取点复算载荷摘要；仅改字节而保留旧摘要也必须拒绝，结果不返回载荷。
    private const string MySqlPayloadHash = "CAST(vector.PayloadHash AS BINARY)=CAST(UPPER(SHA2(vector.Payload,256)) AS BINARY)";
    private const string SqlServerPayloadHash = "vector.PayloadHash COLLATE Latin1_General_100_BIN2=CONVERT(varchar(64),HASHBYTES('SHA2_256',vector.Payload),2)";
    private static readonly string Receipts = "SELECT batch.Id,batch.BatchOrdinal,batch.RequestHash,batch.InputCount,vector.PayloadHash,vector.ProviderKey,vector.ModelId,vector.Dimensions,vector.FormatKey,vector.DistanceKey" + Joins
        + " INNER JOIN fn_ai_knowledge_index_vector AS vector ON vector.Id=batch.Id" + Held.Replace(" AND batch.BatchOrdinal=@BatchOrdinal", string.Empty)
        + " AND vector.ProviderKey=ledger.ProviderKey AND vector.ModelId=ledger.ModelId"
        + " AND " + MySqlPayloadHash
        + " AND (SELECT COUNT(*) FROM fn_ai_knowledge_index_batch AS all_batches WHERE all_batches.IndexTaskId=fn_ai_knowledge_index_task.Id)=@ExpectedBatchCount";

    //多格式文档全量回执追加完整清单及成功结算约束，复用原始批次数守卫与实际载荷 SHA 检查。
    private static readonly string DocumentReceipts = Receipts + DocumentHeld
        + " AND CAST(vector.FormatKey AS BINARY)=CAST('knowledge-document-vector-batch-v1' AS BINARY) AND OCTET_LENGTH(vector.FormatKey)=34";
    private static readonly string DocumentSqlServerReceipts = DocumentSqlServer(DocumentReceipts)
        .Replace("SELECT batch.Id,", "SELECT TOP (2049) batch.Id,").Replace(MySqlPayloadHash, SqlServerPayloadHash);
    internal static readonly SqlStatement ReceiptsDocumentHost = new("ai.knowledge_index_document_vector.receipts_host", DocumentReceipts + Host + " ORDER BY batch.BatchOrdinal LIMIT 2049", SqlDataScope.HostOnly);
    internal static readonly SqlStatement ReceiptsDocumentTenant = new("ai.knowledge_index_document_vector.receipts_tenant", DocumentReceipts + Tenant + " ORDER BY batch.BatchOrdinal LIMIT 2049", SqlDataScope.TenantRequired, SqlTenantBinding.CurrentTenantId);
    internal static readonly SqlStatement ReceiptsDocumentSqlServerHost = new("ai.knowledge_index_document_vector.receipts_sqlserver_host", DocumentSqlServerReceipts + Host + " ORDER BY batch.BatchOrdinal", SqlDataScope.HostOnly);
    internal static readonly SqlStatement ReceiptsDocumentSqlServerTenant = new("ai.knowledge_index_document_vector.receipts_sqlserver_tenant", DocumentSqlServerReceipts + Tenant + " ORDER BY batch.BatchOrdinal", SqlDataScope.TenantRequired, SqlTenantBinding.CurrentTenantId);
}
