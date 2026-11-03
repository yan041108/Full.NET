using System.Text.Json;
using Full.NET.Abstractions.Time;
using Full.NET.Data.Abstractions;
using Full.NET.Data.CodeGeneration.Generation;
using Full.NET.Modules.CodeGeneration.Contracts;
using Full.NET.Modules.CodeGeneration.Git;
using Full.NET.Modules.CodeGeneration.Persistence;
using Full.NET.Modules.CodeGeneration.Serialization;

namespace Full.NET.Modules.CodeGeneration.Features.ManageHostRuns;

/// <summary>文件日志为恢复权威，数据库摘要可从冻结记录重建；数据库失联不丢弃原始操作或创建第二次运行。</summary>
internal static class CodeGenerationWorkspaceRecovery
{
    public static async Task RecoverAsync(GenerationWorkspaceLease lease, string root, ICommandExecutor commands, IQueryExecutor queries,
        CodeGenerationGitWorkspaceService git, IClock clock, CodeGenerationArtifactStore archives, CancellationToken cancellationToken)
    {
        var operation = await GenerationWorkspaceJournal.ReadPendingAsync(lease, cancellationToken).ConfigureAwait(false);
        if (operation is null) return;
        if (operation.Client != "host") throw Conflict("存在 CLI 未完成操作，须由受控 CLI 恢复，不能重新生成覆盖。");
        var context = ReadContext(operation);
        var expected = context.Run;
        if (operation.Kind == "apply") await archives.SaveOperationAsync(root, operation, context, cancellationToken).ConfigureAwait(false);
        var run = await queries.QuerySingleOrDefaultAsync<CodeGenerationRunRecord>(CodeGenerationRunSql.FindById,
            CodeGenerationSqlParameters.Create(("Id", operation.Id)), cancellationToken).ConfigureAwait(false);
        if (run is null)
        {
            var inserted = await commands.ExecuteAsync(CodeGenerationRunSql.Insert, Parameters(expected), cancellationToken).ConfigureAwait(false);
            if (inserted != 1) throw new InvalidOperationException("恢复运行插入未命中唯一记录。");
            run = expected;
        }
        if (!Matches(run, expected) || run.Status is not (CodeGenerationRunStatuses.Running or CodeGenerationRunStatuses.Succeeded))
            throw Conflict("操作日志与运行摘要不一致，保留证据并停止恢复。");
        var plan = GenerationWorkspaceJournal.GetPlan(operation);
        if (operation.Kind == "apply")
        {
            var checkpointPath = Path.Combine(root, GenerationRollbackCheckpointStore.RootRelativePath, operation.Id.ToString("N"));
            if (!Directory.Exists(checkpointPath))
            {
                if (operation.Phase != "prepared") throw Conflict("文件已进入提交阶段但缺少检查点，停止自动恢复。");
                await GenerationWorkspaceRecoveryBudget.EnsureAvailableAsync(lease, plan, cancellationToken).ConfigureAwait(false);
                await GenerationRollbackCheckpointStore.CreateAsync(root, operation.Id, plan, cancellationToken).ConfigureAwait(false);
            }
            var checkpoint = await GenerationRollbackCheckpointStore.ReadAsync(root, operation.Id, cancellationToken).ConfigureAwait(false);
            if (checkpoint.AppliedManifest.ToJson() != plan.NextManifest!.ToJson()
                || checkpoint.PreviousManifest?.ToJson() != plan.PreviousManifest?.ToJson()
                || checkpoint.PreviousContents.Any(item => operation.Files.FirstOrDefault(file => file.RelativePath == item.Key) is { } file
                    && file.Before != item.Value)) throw Conflict("已有检查点与冻结计划不一致。");
        }
        await GenerationWorkspaceJournal.CommitAsync(lease, cancellationToken).ConfigureAwait(false);
        if (run.Status == CodeGenerationRunStatuses.Running)
        {
            var count = await commands.ExecuteAsync(operation.Kind == "apply" ? CodeGenerationRunSql.CompleteApply : CodeGenerationRunSql.CompleteRollback,
                CodeGenerationSqlParameters.Create(("Id", operation.Id), ("FinishedAtUtc", clock.UtcNow),
                    ("ArtifactCount", expected.ArtifactCount), ("ManifestSha256", expected.ManifestSha256)), CancellationToken.None).ConfigureAwait(false);
            if (count != 1) throw new InvalidOperationException("恢复运行终态未命中唯一 running 记录。");
        }
        await git.PublishFrozenAsync(context.PublicationContext, CancellationToken.None).ConfigureAwait(false);
        await GenerationWorkspaceJournal.CompleteAsync(lease, CancellationToken.None).ConfigureAwait(false);
    }

    internal static string SerializeContext(CodeGenerationWorkspaceRunContext context) => JsonSerializer.Serialize(context,
        CodeGenerationJsonSerializerContext.Default.CodeGenerationWorkspaceRunContext);

    internal static CodeGenerationWorkspaceRunContext ReadContext(GenerationWorkspaceOperation operation)
    {
        var context = JsonSerializer.Deserialize(operation.Context ?? "null", CodeGenerationJsonSerializerContext.Default.CodeGenerationWorkspaceRunContext);
        if (context is null || context.SchemaVersion != 1 || context.Run is null || context.Run.Id != operation.Id
            || context.Run.OperationKind != operation.Kind || context.Run.Status != "running"
            || context.Run.RequestedByUserId == Guid.Empty || context.Run.ModuleKey is null || context.Run.EntityKey is null
            || !GenerationContentHash.IsValid(context.Run.SchemaSha256) || !GenerationContentHash.IsValid(context.Run.ManifestSha256)
            || context.Run.ArtifactCount < 0 || context.Run.ErrorCode is not null || string.IsNullOrWhiteSpace(context.GeneratorVersion)
            || (operation.Kind == "apply" && (context.PreviewRunId is null || context.CanonicalSchemaJson is null))
            || (operation.Kind == "rollback" && context.Run.SourceApplyRunId is null)) throw Conflict("冻结运行上下文无效。");
        if (context.Run.ManifestSha256 != CodeGenerationRunSummary.ComputeManifestSha256(GenerationWorkspaceJournal.GetPlan(operation).NextManifest!)
            || (operation.Kind == "apply" && GenerationContentHash.Compute(context.CanonicalSchemaJson!) != context.Run.SchemaSha256))
            throw Conflict("冻结输入与运行摘要不一致。");
        if (CodeGenerationGitWorkspaceService.ParsePublication(context.PublicationContext).RunId != operation.Id)
            throw Conflict("冻结发布与工作区操作不一致。");
        return context;
    }

    private static object Parameters(CodeGenerationRunRecord run) => CodeGenerationSqlParameters.Create(
        ("Id", run.Id), ("TemplateId", run.TemplateId), ("TemplateVersion", run.TemplateVersion), ("SourceApplyRunId", run.SourceApplyRunId),
        ("OperationKind", run.OperationKind), ("Status", run.Status), ("ModuleKey", run.ModuleKey), ("EntityKey", run.EntityKey),
        ("SchemaSha256", run.SchemaSha256), ("ArtifactCount", run.ArtifactCount), ("ManifestSha256", run.ManifestSha256),
        ("ErrorCode", run.ErrorCode), ("RequestedByUserId", run.RequestedByUserId), ("StartedAtUtc", run.StartedAtUtc), ("FinishedAtUtc", run.FinishedAtUtc));

    private static bool Matches(CodeGenerationRunRecord actual, CodeGenerationRunRecord expected) => actual.Id == expected.Id
        && actual.OperationKind == expected.OperationKind && actual.RequestedByUserId == expected.RequestedByUserId
        && actual.TemplateId == expected.TemplateId && actual.TemplateVersion == expected.TemplateVersion
        && actual.SourceApplyRunId == expected.SourceApplyRunId && actual.ModuleKey == expected.ModuleKey && actual.EntityKey == expected.EntityKey
        && actual.SchemaSha256 == expected.SchemaSha256 && actual.ManifestSha256 == expected.ManifestSha256
        && actual.ArtifactCount == expected.ArtifactCount
        // MySQL datetime(6) 只持久化微秒，SQL Server datetimeoffset(7) 保留 100ns；恢复资格使用双库共同精度。
        && actual.StartedAtUtc.UtcTicks / TimeSpan.TicksPerMicrosecond == expected.StartedAtUtc.UtcTicks / TimeSpan.TicksPerMicrosecond;

    private static GenerationWorkspaceConflictException Conflict(string message) => new(message, ".fullnet/codegeneration-active-operation.json");
}
