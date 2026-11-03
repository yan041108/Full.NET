using System.Text;
using System.Text.Json;
using Full.NET.Data.CodeGeneration.Serialization;

namespace Full.NET.Data.CodeGeneration.Generation;

/// <summary>在整操作文件租约内提交冻结计划，保留进程中断后的前滚证据与工作区全局 LIFO 顺序。</summary>
public static class GenerationWorkspaceJournal
{
    private const string HeadPath = ".fullnet/codegeneration-head.json";
    private const string PendingPath = ".fullnet/codegeneration-active-operation.json";
    private const string OperationsPath = ".fullnet/codegeneration-operations";
    private const long MaximumStoredBytes = 64 * 1024 * 1024;
    private static readonly UTF8Encoding Utf8 = new(false, true);

    /// <summary>读取当前全局顺序；已有操作记录却丢失 head 时拒绝重新初始化。</summary>
    public static async Task<GenerationWorkspaceHead> ReadHeadAsync(GenerationWorkspaceLease lease, CancellationToken cancellationToken = default)
    {
        var root = lease.RequireRoot();
        var json = await GenerationWorkspaceMetadata.ReadAsync(root, HeadPath, cancellationToken);
        if (json is null)
        {
            var directory = GenerationWorkspacePath.Resolve(root, OperationsPath);
            if (Directory.Exists(directory) && Directory.EnumerateFileSystemEntries(directory).Any())
                throw Conflict(HeadPath, "工作区顺序记录缺失，禁止重新初始化。");
            return new(1, lease.WorkspaceId, 0, null, null);
        }
        var head = JsonSerializer.Deserialize(json, CodeGenerationToolchainJsonSerializerContext.Default.GenerationWorkspaceHead);
        if (head is null || head.SchemaVersion != 1 || head.WorkspaceId != lease.WorkspaceId || head.Revision < 0
            || (head.Revision == 0 && (head.TipOperationId is not null || head.LastOperationId is not null))
            || (head.Revision > 0 && head.LastOperationId is null)) throw Conflict(HeadPath, "工作区顺序记录无效。");
        return head;
    }

    /// <summary>读取未完成操作；调用方必须先恢复或显式完成，不允许新操作越过它。</summary>
    public static async Task<GenerationWorkspaceOperation?> ReadPendingAsync(GenerationWorkspaceLease lease, CancellationToken cancellationToken = default)
    {
        var json = await GenerationWorkspaceMetadata.ReadAsync(lease.RequireRoot(), PendingPath, cancellationToken);
        if (json is null)
        {
            GenerationWorkspaceOperation? orphan = null;
            foreach (var id in OperationIds(lease.RequireRoot()))
            {
                var candidate = await ReadOperationAsync(lease, id, cancellationToken);
                if (candidate is null || candidate.Phase == "completed") continue;
                if (orphan is not null) throw Conflict(OperationsPath, "存在多个未完成收据，禁止猜测活动顺序。");
                orphan = candidate;
            }
            // 收据先于活动指针发布；这一中断窗口不能被另一计划越过，恢复仍依靠完整收据。
            return orphan;
        }
        var operation = Parse(json, lease);
        var persisted = await ReadOperationAsync(lease, operation.Id, cancellationToken);
        if (persisted is null || Serialize(persisted with { Phase = operation.Phase }) != Serialize(operation))
            throw Conflict(PendingPath, "活动操作与耐久记录不一致，保留证据并停止。");
        return persisted;
    }

    /// <summary>读取已提交或正在恢复的操作收据，不根据内容相同推断新旧顺序。</summary>
    public static async Task<GenerationWorkspaceOperation?> ReadOperationAsync(GenerationWorkspaceLease lease, Guid operationId, CancellationToken cancellationToken = default)
    {
        if (operationId == Guid.Empty) throw new ArgumentException("操作标识不能为空。", nameof(operationId));
        var json = await GenerationWorkspaceMetadata.ReadAsync(lease.RequireRoot(), OperationPath(operationId), cancellationToken);
        if (json is null) return null;
        var operation = Parse(json, lease);
        if (operation.Id != operationId) throw Conflict(OperationPath(operationId), "操作标识不匹配。");
        return operation;
    }

    /// <summary>在任何文件改动前冻结旧/新正文。回滚只能弹出当前工作区顶端，不按实体划分顺序。</summary>
    public static async Task<GenerationWorkspaceOperation> PrepareAsync(GenerationWorkspaceLease lease, Guid operationId,
        Guid idempotencyKey, string kind, string client, string? context, GenerationWritePlan plan,
        Guid? sourceApplyId = null, CancellationToken cancellationToken = default)
    {
        var operation = await FreezeAsync(lease, operationId, idempotencyKey, kind, client, context, plan, sourceApplyId, cancellationToken);
        return await ActivateAsync(lease, operation, cancellationToken);
    }

    /// <summary>只冻结供调用方审查的计划，不占用活动槽、不建立运行记录或改动工作区顺序。</summary>
    public static async Task<GenerationWorkspaceOperation> FreezeAsync(GenerationWorkspaceLease lease, Guid operationId,
        Guid idempotencyKey, string kind, string client, string? context, GenerationWritePlan plan,
        Guid? sourceApplyId = null, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(plan);
        if (operationId == Guid.Empty || idempotencyKey == Guid.Empty || kind is not ("apply" or "rollback")
            || client is not ("host" or "cli") || !plan.CanApply || plan.NextManifest is null)
            throw new ArgumentException("操作身份、类别或计划无效。");
        var root = lease.RequireRoot();
        if (await ReadPendingAsync(lease, cancellationToken) is not null) throw Conflict(PendingPath, "必须先恢复未完成操作。");
        if (await ReadOperationAsync(lease, operationId, cancellationToken) is not null) throw Conflict(OperationPath(operationId), "操作标识已使用。");
        var head = await ReadHeadAsync(lease, cancellationToken);
        Guid? nextTip = operationId;
        if (kind == "rollback")
        {
            if (sourceApplyId is null || sourceApplyId != head.TipOperationId)
                throw Conflict(HeadPath, "回滚目标不是当前工作区全局顶端。");
            var source = await ReadOperationAsync(lease, sourceApplyId.Value, cancellationToken);
            if (source is not { Kind: "apply", Phase: "completed" }) throw Conflict(HeadPath, "回滚目标缺少已完成的工作区操作记录。");
            nextTip = source.PreviousHead.TipOperationId;
        }
        else if (sourceApplyId is not null) throw new ArgumentException("Apply 不能带回滚来源。");

        var snapshot = await GenerationWorkspaceStore.CapturePathsAsync(root,
            plan.Actions.Select(action => action.RelativePath).Concat(plan.PreviousManifest?.Artifacts.Select(entry => entry.RelativePath) ?? []), cancellationToken);
        if (snapshot.PreviousManifest?.ToJson() != plan.PreviousManifest?.ToJson()) throw Conflict(HeadPath, "计划清单已过期。");
        var files = new List<GenerationOperationFile>();
        var checks = new Dictionary<string, string?>(StringComparer.Ordinal);
        foreach (var entry in plan.PreviousManifest?.Artifacts ?? []) checks.Add(entry.RelativePath, entry.Sha256);
        foreach (var action in plan.Actions)
        {
            ValidateProductPath(action.RelativePath);
            snapshot.ExistingFiles.TryGetValue(action.RelativePath, out var before);
            if (Hash(before) != action.ExistingSha256) throw Conflict(action.RelativePath, "计划内容已过期。");
            if (action.Kind == GenerationWriteActionKind.Unchanged)
            {
                if (action.DesiredSha256 != Hash(before)) throw Conflict(action.RelativePath, "未变化动作摘要无效。");
                checks[action.RelativePath] = action.DesiredSha256;
                continue;
            }
            if (action.Kind is not (GenerationWriteActionKind.Create or GenerationWriteActionKind.Update or GenerationWriteActionKind.Delete)
                || (action.Kind == GenerationWriteActionKind.Create && before is not null)
                || (action.Kind is GenerationWriteActionKind.Update or GenerationWriteActionKind.Delete && before is null)
                || (action.Kind == GenerationWriteActionKind.Delete && action.Content is not null)
                || (action.Kind != GenerationWriteActionKind.Delete && action.Content is null)
                || action.DesiredSha256 != Hash(action.Content)) throw Conflict(action.RelativePath, "动作正文与摘要不一致。");
            files.Add(new(action.RelativePath, before, action.Content));
            checks.Remove(action.RelativePath);
        }
        var oldManifest = await ReadTextAsync(root, GenerationWorkspaceStore.ManifestRelativePath, cancellationToken);
        if (oldManifest is not null && GenerationManifest.Parse(oldManifest).ToJson() != plan.PreviousManifest?.ToJson())
            throw Conflict(GenerationWorkspaceStore.ManifestRelativePath, "原始清单已漂移。");
        files.Add(new(GenerationWorkspaceStore.ManifestRelativePath, oldManifest, plan.NextManifest.ToJson()));
        if (files.Count + checks.Count > 2048) throw new IOException("工作区操作超过 2048 个文件的恢复上限。");
        var nextHead = new GenerationWorkspaceHead(1, lease.WorkspaceId, checked(head.Revision + 1), nextTip, operationId);
        var operation = new GenerationWorkspaceOperation(1, operationId, idempotencyKey, kind, client, context, head, nextHead,
            "prepared", files.ToArray(), checks.OrderBy(item => item.Key, StringComparer.Ordinal).Select(item => new GenerationOperationCheck(item.Key, item.Value)).ToArray(), "");
        operation = operation with { PlanSha256 = ComputePlanHash(operation) };
        Validate(operation, lease);
        await ValidateCurrentFilesAsync(root, operation, cancellationToken);
        await GenerationWorkspaceRecoveryBudget.EnsureAvailableAsync(lease, plan, cancellationToken);
        return operation;
    }

    /// <summary>锁内复核客户端已审查的完整计划，耐久登记幂等键后才允许提交；旧 revision 或人工漂移一律拒绝。</summary>
    public static async Task<GenerationWorkspaceOperation> ActivateAsync(GenerationWorkspaceLease lease,
        GenerationWorkspaceOperation frozen, CancellationToken cancellationToken = default)
    {
        var root = lease.RequireRoot();
        // 复制并校验，调用方不能在异步写入期间通过数组引用改变已冻结的正文。
        var operation = Parse(Serialize(frozen), lease);
        if (operation.Phase != "prepared") throw Conflict(PendingPath, "只有已冻结的初始计划才能启动。");
        var active = await ReadPendingAsync(lease, cancellationToken);
        if (active is not null)
        {
            if (active.Id != operation.Id || active.IdempotencyKey != operation.IdempotencyKey || active.PlanSha256 != operation.PlanSha256)
                throw Conflict(PendingPath, "必须先恢复活动操作。");
            return active;
        }
        var existing = await FindByIdempotencyKeyAsync(lease, operation.IdempotencyKey, cancellationToken);
        if (existing is not null)
        {
            if (existing.Id != operation.Id || existing.PlanSha256 != operation.PlanSha256)
                throw Conflict(PendingPath, "同一幂等键不能提交不同计划。");
            if (existing.Phase == "completed") return existing;
            if (existing.Phase != "prepared") throw Conflict(PendingPath, "活动指针缺失但操作已进入提交阶段，保留证据并停止。");
        }
        var sameId = await ReadOperationAsync(lease, operation.Id, cancellationToken);
        if (sameId is not null && (sameId.IdempotencyKey != operation.IdempotencyKey || sameId.PlanSha256 != operation.PlanSha256))
            throw Conflict(PendingPath, "计划已经被另一个幂等请求使用。");
        var head = await ReadHeadAsync(lease, cancellationToken);
        if (head != operation.PreviousHead) throw Conflict(HeadPath, "已审查计划的工作区 revision 已过期。");
        foreach (var file in operation.Files)
            if (Hash(await ReadTextAsync(root, file.RelativePath, cancellationToken)) != Hash(file.Before))
                throw Conflict(file.RelativePath, "已审查计划的前置文件已漂移。");
        await ValidateCurrentFilesAsync(root, operation, cancellationToken);
        await GenerationWorkspaceRecoveryBudget.EnsureAvailableAsync(lease, GetPlan(operation), cancellationToken);
        var serialized = Serialize(operation);
        EnsureCapacity(root, Utf8.GetByteCount(serialized) * 3L + operation.Files.Sum(file => (long)Utf8.GetByteCount(file.After ?? "")));
        // 先建立初始 head，再写不可变收据和活动指针；崩溃时最多留下未开始的孤立收据。
        await SaveHeadAsync(root, head, cancellationToken);
        await GenerationWorkspaceMetadata.WriteAsync(root, OperationPath(operation.Id), serialized, cancellationToken);
        await GenerationWorkspaceMetadata.WriteAsync(root, PendingPath, serialized, cancellationToken);
        return operation;
    }

    /// <summary>按耐久幂等键查找真实操作；扫描受总量限制的操作目录，不把当前清单相同视作重放。</summary>
    public static async Task<GenerationWorkspaceOperation?> FindByIdempotencyKeyAsync(GenerationWorkspaceLease lease, Guid key,
        CancellationToken cancellationToken = default)
    {
        if (key == Guid.Empty) throw new ArgumentException("幂等键不能为空。", nameof(key));
        var root = lease.RequireRoot();
        GenerationWorkspaceOperation? found = null;
        foreach (var id in OperationIds(root))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var operation = await ReadOperationAsync(lease, id, cancellationToken) ?? throw Conflict(OperationsPath, "操作记录在扫描期间消失。");
            if (operation.IdempotencyKey != key) continue;
            if (found is not null) throw Conflict(OperationsPath, "幂等键存在重复操作，保留证据并停止。");
            found = operation;
        }
        return found;
    }

    /// <summary>持久化审查计划时使用相同的确定性格式，不能另建不受校验的正文格式。</summary>
    public static string SerializePlan(GenerationWorkspaceOperation operation) => Serialize(operation);

    /// <summary>读取已保存审查计划，校验版本、工作区身份、路径与全部正文摘要。</summary>
    public static GenerationWorkspaceOperation ParsePlan(GenerationWorkspaceLease lease, string json) => Parse(json, lease);

    /// <summary>持有租约后逐文件校验旧/新状态并前滚。首个文件提交后忽略请求取消，终态未完成时阻断新写入。</summary>
    public static Task<GenerationWorkspaceOperation> CommitAsync(GenerationWorkspaceLease lease, CancellationToken cancellationToken = default)
        => CommitCoreAsync(lease, cancellationToken, null);

    internal static async Task<GenerationWorkspaceOperation> CommitCoreAsync(GenerationWorkspaceLease lease,
        CancellationToken cancellationToken, Func<int, Task>? afterFileCommitted)
    {
        var root = lease.RequireRoot();
        var operation = await ReadPendingAsync(lease, cancellationToken) ?? throw Conflict(PendingPath, "不存在待恢复操作。");
        var head = await ReadHeadAsync(lease, cancellationToken);
        if (head != operation.PreviousHead && head != operation.NextHead) throw Conflict(HeadPath, "恢复操作与工作区顺序不一致。");
        await ValidateCurrentFilesAsync(root, operation, cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();
        var recovering = operation.Phase != "prepared";
        operation = await SetPhaseAsync(root, operation, "committing");
        try
        {
            for (var index = 0; index < operation.Files.Length; index++)
            {
                lease.RequireRoot();
                await CommitFileAsync(root, operation, index);
                if (afterFileCommitted is not null) await afterFileCommitted(index);
            }
            await SaveHeadAsync(root, operation.NextHead, CancellationToken.None);
            return await SetPhaseAsync(root, operation, recovering ? "recovered" : "locally_applied");
        }
        catch
        {
            try { await SetPhaseAsync(root, operation, "recovery_required"); }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException) { /* 保留上一份有效阶段，不覆盖原始提交错误。 */ }
            throw;
        }
    }

    /// <summary>仅在调用方数据库终态与发布收据保存完毕后解除阻断；失败时保留原记录供重试。</summary>
    public static async Task CompleteAsync(GenerationWorkspaceLease lease, CancellationToken cancellationToken = default)
    {
        var root = lease.RequireRoot();
        var operation = await ReadPendingAsync(lease, cancellationToken) ?? throw Conflict(PendingPath, "不存在待完成操作。");
        if (await ReadHeadAsync(lease, cancellationToken) != operation.NextHead) throw Conflict(HeadPath, "文件尚未提交，禁止完成操作。");
        await ValidateCurrentFilesAsync(root, operation, cancellationToken);
        foreach (var file in operation.Files)
            if (Hash(await ReadTextAsync(root, file.RelativePath, cancellationToken)) != Hash(file.After))
                throw Conflict(file.RelativePath, "终态前发现文件漂移。");
        var completed = operation with { Phase = "completed" };
        // 先完成收据，再删活动指针；读者允许这一唯一可恢复的阶段差异。
        await GenerationWorkspaceMetadata.WriteAsync(root, OperationPath(operation.Id), Serialize(completed), cancellationToken);
        for (var index = 0; index < operation.Files.Length; index++)
        {
            // 无变更计划与恢复重放可能从未创建暂存目录；只清理确实存在的声明文件。
            foreach (var kind in new[] { "old", "new" })
            {
                var stage = GenerationWorkspacePath.ResolveFile(root, StagePath(operation.Id, index, kind));
                if (File.Exists(stage)) File.Delete(stage);
            }
        }
        File.Delete(GenerationWorkspacePath.ResolveFile(root, PendingPath));
    }

    /// <summary>重建冻结计划，供恢复后的 Git 发布使用；不重新执行生成器或读取可变模板。</summary>
    public static GenerationWritePlan GetPlan(GenerationWorkspaceOperation operation)
    {
        ArgumentNullException.ThrowIfNull(operation);
        var manifest = operation.Files[^1];
        var actions = operation.Files[..^1].Select(file => new GenerationWriteAction(file.RelativePath,
            file.Before is null ? GenerationWriteActionKind.Create : file.After is null ? GenerationWriteActionKind.Delete : GenerationWriteActionKind.Update,
            file.After, Hash(file.Before), Hash(file.After))).Concat(operation.Checks.Select(check =>
            new GenerationWriteAction(check.RelativePath, GenerationWriteActionKind.Unchanged, null, check.Sha256, check.Sha256)));
        return new GenerationWritePlan(actions.OrderBy(action => action.RelativePath, StringComparer.Ordinal),
            manifest.Before is null ? null : GenerationManifest.Parse(manifest.Before), GenerationManifest.Parse(manifest.After!));
    }

    private static async Task ValidateCurrentFilesAsync(string root, GenerationWorkspaceOperation operation, CancellationToken cancellationToken)
    {
        foreach (var check in operation.Checks)
            if (Hash(await ReadTextAsync(root, check.RelativePath, cancellationToken)) != check.Sha256)
                throw Conflict(check.RelativePath, "未变更的受管文件发生漂移，停止恢复。");
        for (var index = 0; index < operation.Files.Length; index++)
        {
            var file = operation.Files[index];
            var current = await ReadTextAsync(root, file.RelativePath, cancellationToken);
            var claimed = await ReadTextAsync(root, StagePath(operation.Id, index, "old"), cancellationToken);
            if (claimed is not null && Hash(claimed) != Hash(file.Before)) throw Conflict(file.RelativePath, "原文件声明证据已漂移。");
            if (Hash(current) != Hash(file.Before) && Hash(current) != Hash(file.After)
                && !(current is null && claimed is not null)) throw Conflict(file.RelativePath, "文件既不等于旧版也不等于冻结新版，停止恢复。");
        }
    }

    private static async Task CommitFileAsync(string root, GenerationWorkspaceOperation operation, int index)
    {
        var file = operation.Files[index];
        var current = await ReadTextAsync(root, file.RelativePath, CancellationToken.None);
        if (Hash(current) == Hash(file.After)) return;
        var oldRelative = StagePath(operation.Id, index, "old");
        var oldPath = GenerationWorkspacePath.ResolveFile(root, oldRelative);
        GenerationWorkspacePath.EnsureParentDirectory(root, oldRelative);
        var target = GenerationWorkspacePath.ResolveFile(root, file.RelativePath);
        if (current is not null)
        {
            if (Hash(current) != Hash(file.Before) || File.Exists(oldPath)) throw Conflict(file.RelativePath, "文件声明前发生漂移。");
            File.Move(target, oldPath, overwrite: false);
            if (Hash(await ReadTextAsync(root, oldRelative, CancellationToken.None)) != Hash(file.Before))
            {
                if (!File.Exists(target)) File.Move(oldPath, target, overwrite: false);
                throw Conflict(file.RelativePath, "文件声明过程中发生漂移，保留原文。");
            }
        }
        else if (file.Before is not null && !File.Exists(oldPath)) throw Conflict(file.RelativePath, "原文件缺失且没有声明证据。");
        if (file.After is null) return;
        var pendingRelative = StagePath(operation.Id, index, "new");
        var pending = GenerationWorkspacePath.ResolveFile(root, pendingRelative);
        await using (var stream = new FileStream(pending, FileMode.Create, FileAccess.Write, FileShare.None, 4096, FileOptions.WriteThrough | FileOptions.Asynchronous))
        {
            await stream.WriteAsync(Utf8.GetBytes(file.After));
            await stream.FlushAsync();
            stream.Flush(flushToDisk: true);
        }
        GenerationWorkspacePath.EnsureParentDirectory(root, file.RelativePath);
        target = GenerationWorkspacePath.ResolveFile(root, file.RelativePath);
        File.Move(GenerationWorkspacePath.ResolveFile(root, pendingRelative), target, overwrite: false);
    }

    private static GenerationWorkspaceOperation Parse(string json, GenerationWorkspaceLease lease)
    {
        var operation = JsonSerializer.Deserialize(json, CodeGenerationToolchainJsonSerializerContext.Default.GenerationWorkspaceOperation)
            ?? throw Conflict(PendingPath, "操作记录为空。");
        Validate(operation, lease);
        return operation;
    }

    private static void Validate(GenerationWorkspaceOperation operation, GenerationWorkspaceLease lease)
    {
        if (operation.SchemaVersion != 1 || operation.Id == Guid.Empty || operation.IdempotencyKey == Guid.Empty
            || operation.Kind is not ("apply" or "rollback") || operation.Client is not ("host" or "cli")
            || operation.Phase is not ("prepared" or "committing" or "locally_applied" or "recovery_required" or "recovered" or "completed")
            || operation.Files is null || operation.Checks is null
            || operation.Files.Length == 0 || operation.Files.Length + operation.Checks.Length > 2048
            || operation.PreviousHead is null || operation.NextHead is null
            || operation.PreviousHead.SchemaVersion != 1 || operation.NextHead.SchemaVersion != 1
            || operation.PreviousHead.WorkspaceId != lease.WorkspaceId || operation.NextHead.WorkspaceId != lease.WorkspaceId
            || operation.PreviousHead.Revision < 0 || operation.NextHead.Revision != operation.PreviousHead.Revision + 1
            || operation.NextHead.LastOperationId != operation.Id || operation.PlanSha256 != ComputePlanHash(operation))
            throw Conflict(PendingPath, "操作记录版本、身份或摘要无效。");
        var paths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var file in operation.Files)
        {
            if (file.RelativePath != GenerationWorkspaceStore.ManifestRelativePath) ValidateProductPath(file.RelativePath);
            if (!paths.Add(file.RelativePath)) throw Conflict(file.RelativePath, "操作包含重复路径。");
        }
        foreach (var check in operation.Checks)
        {
            ValidateProductPath(check.RelativePath);
            if (!paths.Add(check.RelativePath) || check.Sha256 is not { Length: 64 }) throw Conflict(check.RelativePath, "操作检查摘要无效。");
        }
        if (operation.Files[^1].RelativePath != GenerationWorkspaceStore.ManifestRelativePath || operation.Files[^1].After is null)
            throw Conflict(PendingPath, "操作必须以有效清单提交结束。");
        _ = GenerationManifest.Parse(operation.Files[^1].After!);
    }

    private static void ValidateProductPath(string path)
    {
        GenerationArtifactPath.Validate(path, nameof(path));
        if (path.Split('/').Any(segment => segment.Equals(".fullnet", StringComparison.OrdinalIgnoreCase)
            || segment.Equals(".git", StringComparison.OrdinalIgnoreCase))) throw new ArgumentException("操作不能改写控制元数据。");
    }

    private static async Task<string?> ReadTextAsync(string root, string path, CancellationToken cancellationToken)
    {
        var fullPath = GenerationWorkspacePath.ResolveFile(root, path);
        if (!File.Exists(fullPath)) return null;
        if (new FileInfo(fullPath).Length > 16 * 1024 * 1024) throw new IOException("单文件超出恢复预算。");
        var bytes = await File.ReadAllBytesAsync(fullPath, cancellationToken);
        if (bytes.AsSpan().StartsWith(new byte[] { 0xEF, 0xBB, 0xBF })) throw new IOException("恢复文件不允许 BOM。");
        return Utf8.GetString(bytes);
    }

    private static void EnsureCapacity(string root, long required)
    {
        var directory = GenerationWorkspacePath.Resolve(root, OperationsPath);
        long used = 0;
        if (Directory.Exists(directory))
            foreach (var path in Directory.EnumerateFileSystemEntries(directory))
            {
                var validated = GenerationWorkspacePath.RevalidateFile(root, path);
                used = checked(used + new FileInfo(validated).Length);
            }
        if (required > 16 * 1024 * 1024 || checked(used + required) > MaximumStoredBytes)
            throw new IOException("操作恢复材料超过预算；请先归档不再处于回滚链中的已完成记录。");
    }

    private static string? Hash(string? text) => text is null ? null : GenerationContentHash.Compute(text);
    private static string ComputePlanHash(GenerationWorkspaceOperation operation) => GenerationContentHash.Compute(Serialize(operation with { Phase = "prepared", IdempotencyKey = Guid.Empty, PlanSha256 = "" }));
    private static string Serialize(GenerationWorkspaceOperation operation) => JsonSerializer.Serialize(operation, CodeGenerationToolchainJsonSerializerContext.Default.GenerationWorkspaceOperation);
    private static string OperationPath(Guid id) => $"{OperationsPath}/{id:N}.json";
    private static IEnumerable<Guid> OperationIds(string root)
    {
        var directory = GenerationWorkspacePath.Resolve(root, OperationsPath);
        if (!Directory.Exists(directory)) yield break;
        var count = 0;
        foreach (var path in Directory.EnumerateFileSystemEntries(directory, "*.json"))
        {
            if (++count > 8192) throw new IOException("操作记录超出条目上限。");
            if (!Guid.TryParseExact(Path.GetFileNameWithoutExtension(path), "N", out var id))
                throw Conflict(OperationsPath, "操作目录包含未知记录，停止顺序与幂等检查。");
            yield return id;
        }
    }
    private static async Task<GenerationWorkspaceOperation> SetPhaseAsync(string root, GenerationWorkspaceOperation operation, string phase)
    {
        var next = operation with { Phase = phase };
        var json = Serialize(next);
        // 收据先行；活动指针可落后一个阶段，读取时只允许阶段不同，正文和计划摘要必须完全一致。
        await GenerationWorkspaceMetadata.WriteAsync(root, OperationPath(operation.Id), json, CancellationToken.None);
        await GenerationWorkspaceMetadata.WriteAsync(root, PendingPath, json, CancellationToken.None);
        return next;
    }
    private static string StagePath(Guid id, int index, string suffix) => $".fullnet/codegeneration-operation-stage/{id:N}/{index:D4}.{suffix}";
    private static Task SaveHeadAsync(string root, GenerationWorkspaceHead head, CancellationToken cancellationToken) =>
        GenerationWorkspaceMetadata.WriteAsync(root, HeadPath, JsonSerializer.Serialize(head, CodeGenerationToolchainJsonSerializerContext.Default.GenerationWorkspaceHead), cancellationToken);
    private static GenerationWorkspaceConflictException Conflict(string path, string message) => new(message, path);
}
