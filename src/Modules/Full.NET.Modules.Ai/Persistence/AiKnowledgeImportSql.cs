using Full.NET.Data.Abstractions;

namespace Full.NET.Modules.Ai.Persistence;

/// <summary>导入日志只访问 AI 表；所有者管理允许在文档删除后继续文件补偿。</summary>
internal static class AiKnowledgeImportSql
{
    private const string Host = "knowledge.TenantId IS NULL";
    private const string Tenant = "knowledge.TenantId = @TenantId";
    private const string Owner = "knowledge.OwnerUserId = @OwnerUserId";
    private const string Joins = " FROM fn_ai_knowledge_import AS operation INNER JOIN fn_ai_knowledge_document AS document ON document.Id = operation.DocumentId INNER JOIN fn_ai_knowledge_base AS knowledge ON knowledge.Id = document.KnowledgeBaseId WHERE document.Id = @DocumentId AND document.KnowledgeBaseId = @KnowledgeBaseId AND " + Owner + " AND ";
    private const string Columns = "operation.Id, operation.DocumentId, operation.VersionId, operation.RequestKey, operation.ExpectedDocumentVersion, operation.OriginalFileName, operation.ContentType, operation.ContentHash, operation.SizeBytes, operation.ActorUserId, operation.FileId, operation.StateKey, operation.FailureCode, operation.LeaseToken, operation.LeaseExpiresAtUtc, operation.CreatedAtUtc, operation.UpdatedAtUtc";
    private const string Read = "SELECT " + Columns + Joins;
    private const string OwnerExists = " AND EXISTS (SELECT 1 FROM fn_ai_knowledge_document AS document INNER JOIN fn_ai_knowledge_base AS knowledge ON knowledge.Id = document.KnowledgeBaseId WHERE document.Id = fn_ai_knowledge_import.DocumentId AND document.Id = @DocumentId AND document.KnowledgeBaseId = @KnowledgeBaseId AND " + Owner + " AND ";
    private const string Insert = "INSERT INTO fn_ai_knowledge_import (Id, DocumentId, VersionId, RequestKey, ExpectedDocumentVersion, OriginalFileName, ContentType, ContentHash, SizeBytes, ActorUserId, StateKey, CreatedAtUtc, UpdatedAtUtc) SELECT @OperationId, document.Id, @VersionId, @RequestKey, @Version, @OriginalFileName, @ContentType, @ContentHash, @SizeBytes, @OwnerUserId, 'awaiting_upload', @Now, @Now FROM fn_ai_knowledge_document AS document INNER JOIN fn_ai_knowledge_base AS knowledge ON knowledge.Id = document.KnowledgeBaseId WHERE document.Id = @DocumentId AND document.KnowledgeBaseId = @KnowledgeBaseId AND document.IsDeleted = 0 AND knowledge.IsEnabled = 1 AND document.Version = @Version AND " + Owner + " AND ";
    private const string Acquire = "UPDATE fn_ai_knowledge_import SET LeaseToken = @LeaseToken, LeaseExpiresAtUtc = @LeaseExpiresAtUtc, UpdatedAtUtc = @Now WHERE Id = @OperationId AND StateKey NOT IN ('completed','failed') AND (LeaseToken IS NULL OR LeaseExpiresAtUtc <= @Now)" + OwnerExists;
    private const string SaveFile = "UPDATE fn_ai_knowledge_import SET FileId = @FileId, StateKey = CASE WHEN StateKey = 'cleanup_pending' THEN StateKey ELSE 'file_uploaded' END, UpdatedAtUtc = @Now" + " WHERE Id = @OperationId AND LeaseToken = @LeaseToken AND LeaseExpiresAtUtc > @Now AND FileId IS NULL" + OwnerExists;
    // 成功路径只能从指定前态迁移；删除补偿不可被仍持有租约的执行者复活。
    private const string SetState = "UPDATE fn_ai_knowledge_import SET StateKey = @StateKey, FailureCode = @FailureCode, UpdatedAtUtc = @Now"
        + " WHERE Id = @OperationId AND LeaseToken = @LeaseToken AND LeaseExpiresAtUtc > @Now"
        + " AND ((@StateKey = 'pending_confirmation' AND StateKey = 'file_uploaded')"
        + " OR (@StateKey = 'completed' AND StateKey = 'pending_confirmation')"
        + " OR (@StateKey = 'failed' AND StateKey = 'cleanup_pending')"
        + " OR (@StateKey = 'cleanup_pending' AND StateKey IN ('awaiting_upload','file_uploaded','pending_confirmation','cleanup_pending')))" + OwnerExists;
    private const string ReleaseLease = "UPDATE fn_ai_knowledge_import SET LeaseToken = NULL, LeaseExpiresAtUtc = NULL WHERE Id = @OperationId AND LeaseToken = @LeaseToken" + OwnerExists;
    private const string Cleanup = "UPDATE fn_ai_knowledge_import SET StateKey = 'cleanup_pending', FailureCode = 'ai.knowledge.document_not_found', UpdatedAtUtc = @Now WHERE DocumentId = @DocumentId AND StateKey <> 'failed'" + OwnerExists;
    // 对账判断引用而非下载资格：禁用知识库仍保留文件，删除墓碑及终止阶段才撤销引用。
    private const string Reference = "SELECT COUNT(*) FROM fn_ai_knowledge_import AS operation INNER JOIN fn_ai_knowledge_document AS document ON document.Id = operation.DocumentId INNER JOIN fn_ai_knowledge_base AS knowledge ON knowledge.Id = document.KnowledgeBaseId WHERE operation.VersionId = @VersionId AND operation.FileId = @FileId AND document.IsDeleted = 0 AND operation.StateKey IN ('file_uploaded','pending_confirmation','completed') AND ";
    // 租户 Files 已在服务端版本资源下登记所有权：未知上传结果期间保护待绑定对象，结束或删除才撤销。
    private const string TenantReference = "SELECT COUNT(*) FROM fn_ai_knowledge_import AS operation INNER JOIN fn_ai_knowledge_document AS document ON document.Id = operation.DocumentId INNER JOIN fn_ai_knowledge_base AS knowledge ON knowledge.Id = document.KnowledgeBaseId WHERE operation.VersionId = @VersionId AND document.IsDeleted = 0 AND ((operation.FileId = @FileId AND operation.StateKey IN ('file_uploaded','pending_confirmation','completed')) OR (operation.FileId IS NULL AND operation.StateKey = 'awaiting_upload')) AND ";
    private const string AnyDocument = "SELECT document.Id, document.KnowledgeBaseId, document.Title, document.Description, document.CreatedAtUtc, document.UpdatedAtUtc, document.Version FROM fn_ai_knowledge_document AS document INNER JOIN fn_ai_knowledge_base AS knowledge ON knowledge.Id = document.KnowledgeBaseId WHERE document.Id = @DocumentId AND document.KnowledgeBaseId = @KnowledgeBaseId AND " + Owner + " AND ";

    internal static readonly SqlStatement FindHost = new("ai.knowledge_import.find_host", Read + Host + " AND operation.Id = @OperationId", SqlDataScope.HostOnly);
    internal static readonly SqlStatement FindTenant = new("ai.knowledge_import.find_tenant", Read + Tenant + " AND operation.Id = @OperationId", SqlDataScope.TenantRequired, SqlTenantBinding.CurrentTenantId);
    internal static readonly SqlStatement FindKeyHost = new("ai.knowledge_import.find_key_host", Read + Host + " AND operation.RequestKey = @RequestKey", SqlDataScope.HostOnly);
    internal static readonly SqlStatement FindKeyTenant = new("ai.knowledge_import.find_key_tenant", Read + Tenant + " AND operation.RequestKey = @RequestKey", SqlDataScope.TenantRequired, SqlTenantBinding.CurrentTenantId);
    internal static readonly SqlStatement FindAnyDocumentHost = new("ai.knowledge_import.document_host", AnyDocument + Host, SqlDataScope.HostOnly);
    internal static readonly SqlStatement FindAnyDocumentTenant = new("ai.knowledge_import.document_tenant", AnyDocument + Tenant, SqlDataScope.TenantRequired, SqlTenantBinding.CurrentTenantId);
    internal static readonly SqlStatement InsertHost = new("ai.knowledge_import.insert_host", Insert + Host, SqlDataScope.HostOnly);
    internal static readonly SqlStatement InsertTenant = new("ai.knowledge_import.insert_tenant", Insert + Tenant, SqlDataScope.TenantRequired, SqlTenantBinding.CurrentTenantId);
    internal static readonly SqlStatement AcquireHost = new("ai.knowledge_import.acquire_host", Acquire + Host + ")", SqlDataScope.HostOnly);
    internal static readonly SqlStatement AcquireTenant = new("ai.knowledge_import.acquire_tenant", Acquire + Tenant + ")", SqlDataScope.TenantRequired, SqlTenantBinding.CurrentTenantId);
    internal static readonly SqlStatement SaveFileHost = new("ai.knowledge_import.save_file_host", SaveFile + Host + ")", SqlDataScope.HostOnly);
    internal static readonly SqlStatement SaveFileTenant = new("ai.knowledge_import.save_file_tenant", SaveFile + Tenant + ")", SqlDataScope.TenantRequired, SqlTenantBinding.CurrentTenantId);
    internal static readonly SqlStatement SetStateHost = new("ai.knowledge_import.set_state_host", SetState + Host + ")", SqlDataScope.HostOnly);
    internal static readonly SqlStatement SetStateTenant = new("ai.knowledge_import.set_state_tenant", SetState + Tenant + ")", SqlDataScope.TenantRequired, SqlTenantBinding.CurrentTenantId);
    internal static readonly SqlStatement ReleaseLeaseHost = new("ai.knowledge_import.release_lease_host", ReleaseLease + Host + ")", SqlDataScope.HostOnly);
    internal static readonly SqlStatement ReleaseLeaseTenant = new("ai.knowledge_import.release_lease_tenant", ReleaseLease + Tenant + ")", SqlDataScope.TenantRequired, SqlTenantBinding.CurrentTenantId);
    internal static readonly SqlStatement CleanupHost = new("ai.knowledge_import.cleanup_host", Cleanup + Host + ")", SqlDataScope.HostOnly);
    internal static readonly SqlStatement CleanupTenant = new("ai.knowledge_import.cleanup_tenant", Cleanup + Tenant + ")", SqlDataScope.TenantRequired, SqlTenantBinding.CurrentTenantId);
    internal static readonly SqlStatement ListCleanupSqlServerHost = new("ai.knowledge_import.cleanup_sqlserver_host", Read + Host + " AND operation.StateKey = 'cleanup_pending' ORDER BY operation.CreatedAtUtc, operation.Id OFFSET 0 ROWS FETCH NEXT @PageSize ROWS ONLY", SqlDataScope.HostOnly);
    internal static readonly SqlStatement ListCleanupSqlServerTenant = new("ai.knowledge_import.cleanup_sqlserver_tenant", Read + Tenant + " AND operation.StateKey = 'cleanup_pending' ORDER BY operation.CreatedAtUtc, operation.Id OFFSET 0 ROWS FETCH NEXT @PageSize ROWS ONLY", SqlDataScope.TenantRequired, SqlTenantBinding.CurrentTenantId);
    internal static readonly SqlStatement ListCleanupMySqlHost = new("ai.knowledge_import.cleanup_mysql_host", Read + Host + " AND operation.StateKey = 'cleanup_pending' ORDER BY operation.CreatedAtUtc, operation.Id LIMIT @PageSize", SqlDataScope.HostOnly);
    internal static readonly SqlStatement ListCleanupMySqlTenant = new("ai.knowledge_import.cleanup_mysql_tenant", Read + Tenant + " AND operation.StateKey = 'cleanup_pending' ORDER BY operation.CreatedAtUtc, operation.Id LIMIT @PageSize", SqlDataScope.TenantRequired, SqlTenantBinding.CurrentTenantId);
    internal static readonly SqlStatement ReferenceHost = new("ai.knowledge_import.reference_host", Reference + Host, SqlDataScope.HostOnly);
    internal static readonly SqlStatement ReferenceTenant = new("ai.knowledge_import.reference_tenant", TenantReference + Tenant, SqlDataScope.TenantRequired, SqlTenantBinding.CurrentTenantId);

}
