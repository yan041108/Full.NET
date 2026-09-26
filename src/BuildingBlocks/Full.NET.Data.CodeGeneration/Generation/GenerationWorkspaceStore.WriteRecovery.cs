namespace Full.NET.Data.CodeGeneration.Generation;

public static partial class GenerationWorkspaceStore
{
    private const string WriteRecoveryDirectoryRelativePath = ".fullnet/codegeneration-write-recovery";

    /// <summary>阶段证据先落盘，再通过无覆盖 rename 声明旧文件，避免覆盖声明边界的人工改动。</summary>
    private static void CommitArtifact(string fullRoot, StagedFile staged, List<ClaimedWrite> writes)
    {
        var action = staged.Action!;
        var prefix = $"{WriteRecoveryDirectoryRelativePath}/{Guid.NewGuid():N}";
        GenerationWorkspacePath.EnsureParentDirectory(fullRoot, prefix + ".pending");
        var write = new ClaimedWrite(action, prefix);
        writes.Add(write);
        WriteDeleteRecoveryMetadata(GenerationWorkspacePath.Resolve(fullRoot, prefix + ".pending"),
            $"{action.Kind}\n{action.RelativePath}\n{action.ExistingSha256}\n{action.DesiredSha256}\n");
        write.HasEvidence = true;
        var target = GenerationWorkspacePath.Resolve(fullRoot, action.RelativePath);
        if (action.Kind == GenerationWriteActionKind.Update)
        {
            File.Move(target, GenerationWorkspacePath.Resolve(fullRoot, prefix + ".old"));
            write.HasOriginal = true;
            if (!StringComparer.Ordinal.Equals(GenerationContentHash.Compute(ReadStrictText(
                    GenerationWorkspacePath.Resolve(fullRoot, prefix + ".old"))), action.ExistingSha256))
            {
                throw Conflict(action.RelativePath, "产物在更新声明边界发生变化，禁止覆盖。");
            }
        }

        // Create 和 Update 都要求目录项为空；编辑器重新占用时不覆盖它。
        File.Move(staged.TemporaryPath, GenerationWorkspacePath.Resolve(fullRoot, action.RelativePath));
        write.NewCommitted = true;
    }

    /// <summary>旧备份也需复验，防止保留的写句柄修改旧文件后被成功清理吞掉。</summary>
    private static void ValidateClaimedWritesBeforeManifest(string fullRoot, IEnumerable<ClaimedWrite> writes)
    {
        foreach (var write in writes.Where(item => item.HasOriginal))
        {
            if (!StringComparer.Ordinal.Equals(GenerationContentHash.Compute(ReadStrictText(
                    GenerationWorkspacePath.Resolve(fullRoot, write.Prefix + ".old"))), write.Action.ExistingSha256))
            {
                throw Conflict(write.Action.RelativePath, "旧产物恢复备份在清单提交前发生变化。");
            }
        }
    }

    /// <summary>逆序尝试全部写入恢复；单个目标冲突不得阻止其他目标恢复。</summary>
    private static Exception? RestoreClaimedWrites(string fullRoot, IReadOnlyList<ClaimedWrite> writes)
    {
        var errors = new List<Exception>();
        for (var index = writes.Count - 1; index >= 0; index--)
        {
            var write = writes[index];
            if (!write.HasEvidence) continue;
            try
            {
                var target = GenerationWorkspacePath.Resolve(fullRoot, write.Action.RelativePath);
                var displaced = GenerationWorkspacePath.Resolve(fullRoot, write.Prefix + ".new");
                if (write.NewCommitted)
                {
                    File.Move(target, displaced);
                    var originalRestored = false;
                    try
                    {
                        using var displacedLease = OpenRecoveryReadLease(displaced);
                        if (!StringComparer.Ordinal.Equals(ReadRecoveryHash(displacedLease), write.Action.DesiredSha256))
                        {
                            throw Conflict(write.Action.RelativePath, "已提交产物被人工修改，无法自动恢复。");
                        }

                        if (write.HasOriginal)
                        {
                            File.Move(GenerationWorkspacePath.Resolve(fullRoot, write.Prefix + ".old"),
                                GenerationWorkspacePath.Resolve(fullRoot, write.Action.RelativePath));
                            originalRestored = true;
                        }

                        File.Delete(displaced);
                    }
                    catch
                    {
                        // 活跃写句柄、非法编码与摘要漂移都应尽力移回原位；已恢复旧文件时不再动它。
                        if (!originalRestored && File.Exists(displaced))
                        {
                            File.Move(displaced, GenerationWorkspacePath.Resolve(fullRoot, write.Action.RelativePath));
                        }
                        throw;
                    }
                }
                else if (write.HasOriginal)
                {
                    File.Move(GenerationWorkspacePath.Resolve(fullRoot, write.Prefix + ".old"),
                        GenerationWorkspacePath.Resolve(fullRoot, write.Action.RelativePath));
                }

                File.Delete(GenerationWorkspacePath.Resolve(fullRoot, write.Prefix + ".pending"));
            }
            catch (Exception exception)
            {
                errors.Add(exception);
            }
        }

        return errors.Count == 0 ? null : new AggregateException(errors);
    }

    /// <summary>清单提交后只清理备份，不回退已提交状态；清理失败保留证据并失败关闭。</summary>
    private static void CleanupCommittedWrites(string fullRoot, IEnumerable<ClaimedWrite> writes)
    {
        foreach (var write in writes)
        {
            try
            {
                if (write.HasOriginal)
                {
                    var original = GenerationWorkspacePath.Resolve(fullRoot, write.Prefix + ".old");
                    var cleanup = GenerationWorkspacePath.Resolve(fullRoot, write.Prefix + ".cleanup");
                    File.Move(original, cleanup);
                    try
                    {
                        // 持有拒绝写入但允许删除的句柄，校验与删除之间不能再被旧写句柄修改。
                        using var lease = OpenRecoveryReadLease(cleanup);
                        if (!StringComparer.Ordinal.Equals(ReadRecoveryHash(lease), write.Action.ExistingSha256))
                        {
                            throw Conflict(write.Action.RelativePath, "清单已提交，但旧备份被修改，禁止清理。");
                        }

                        File.Delete(cleanup);
                    }
                    catch
                    {
                        // 只做无覆盖恢复；重新占用 .old 时保留两份证据，不覆盖任何新内容。
                        File.Move(cleanup, GenerationWorkspacePath.Resolve(fullRoot, write.Prefix + ".old"));
                        throw;
                    }
                }
                File.Delete(GenerationWorkspacePath.Resolve(fullRoot, write.Prefix + ".pending"));
            }
            catch (Exception exception)
            {
                throw new GenerationWorkspaceConflictException("生成清单已提交，但写入恢复证据清理失败。",
                    WriteRecoveryDirectoryRelativePath, exception);
            }
        }
    }

    /// <summary>拒绝并发写句柄；声明后的恢复文件在摘要校验及清理期间只允许读取或目录项移动。</summary>
    private static FileStream OpenRecoveryReadLease(string path) => new(path, FileMode.Open, FileAccess.Read, FileShare.Delete);

    /// <summary>在持有的句柄上严格解码并计算摘要，不通过另一次路径读取引入替换窗口。</summary>
    private static string ReadRecoveryHash(FileStream stream)
    {
        var bytes = new byte[checked((int)stream.Length)];
        stream.ReadExactly(bytes);
        return GenerationContentHash.Compute(DecodeStrictText(bytes));
    }

    /// <summary>包括进程中断在内的未完成写入证据阻断所有入口，不自动猜测磁盘与清单的关系。</summary>
    private static void RejectPendingWriteRecovery(string fullRoot)
    {
        var directory = GenerationWorkspacePath.Resolve(fullRoot, WriteRecoveryDirectoryRelativePath);
        if (Directory.Exists(directory) && Directory.EnumerateFileSystemEntries(directory).Any())
        {
            throw Conflict(WriteRecoveryDirectoryRelativePath, "检测到未完成的产物写入恢复证据，必须人工审查。");
        }
        if (File.Exists(directory)) throw Conflict(WriteRecoveryDirectoryRelativePath, "写入恢复目录被普通文件占用。");
    }

    /// <summary>记录本次已声明的写入及提交阶段；落盘 pending 与旧文件承担进程中断时的恢复证据。</summary>
    private sealed class ClaimedWrite(GenerationWriteAction action, string prefix)
    {
        public GenerationWriteAction Action { get; } = action;
        public string Prefix { get; } = prefix;
        public bool HasEvidence { get; set; }
        public bool HasOriginal { get; set; }
        public bool NewCommitted { get; set; }
    }
}
