using System.Globalization;
using System.Security.Cryptography;
using System.Text;

using Full.NET.Abstractions.Time;
using Full.NET.Data.Abstractions;
using Full.NET.Data.CodeGeneration.Generation;
using Full.NET.Data.CodeGeneration.Packaging;
using Full.NET.Modules.CodeGeneration.Configuration;
using Full.NET.Modules.CodeGeneration.Contracts;
using Full.NET.Modules.CodeGeneration.Persistence;

using Microsoft.Extensions.Options;

namespace Full.NET.Modules.CodeGeneration.Features.ManageHostRuns;

/// <summary>先耐久保存当次包，再记录不可变元数据；独立租约、容量和到期清理不依赖 Apply 开关或检查点。</summary>
internal sealed class CodeGenerationArtifactStore(IQueryExecutor queries, ICommandExecutor commands,
    IOptions<CodeGenerationArtifactOptions> options, IClock clock)
{
    internal const int MaximumPackageBytes = 8 * 1024 * 1024;
    private const string DirectoryPath = ".fullnet/codegeneration-archives/";

    public async Task SaveOperationAsync(string workspaceRoot, GenerationWorkspaceOperation operation, CodeGenerationWorkspaceRunContext context, CancellationToken token)
    {
        var saved = await FindAsync(operation.Id, token).ConfigureAwait(false);
        if (saved is not null)
        {
            Validate(saved);
            if (saved.SchemaJson != context.CanonicalSchemaJson || saved.GeneratorVersion != context.GeneratorVersion
                || saved.ManifestSha256 != context.Run.ManifestSha256) throw new CodeGenerationArchiveException(CodeGenerationRunErrorCodes.ArchiveCorrupt);
            if (saved.ExpiresAtUtc > clock.UtcNow) await ReadAsync(operation.Id, token).ConfigureAwait(false);
            return;
        }
        var snapshot = await GenerationWorkspaceStore.CapturePathsAsync(workspaceRoot, operation.Checks.Select(check => check.RelativePath), token).ConfigureAwait(false);
        var artifacts = new List<GeneratedArtifact>();
        foreach (var check in operation.Checks)
        {
            snapshot.ExistingFiles.TryGetValue(check.RelativePath, out var content);
            if ((content is null ? null : GenerationContentHash.Compute(content)) != check.Sha256)
                throw new GenerationWorkspaceConflictException("冻结归档的未变化文件发生漂移。", check.RelativePath);
            if (content is not null) artifacts.Add(new(check.RelativePath, GeneratedArtifactKind.Report, content));
        }
        artifacts.AddRange(operation.Files.Where(file => file.After is not null).Select(file => new GeneratedArtifact(file.RelativePath, GeneratedArtifactKind.Report, file.After!)));
        await SaveAsync(operation.Id, context.CanonicalSchemaJson!, context.GeneratorVersion, context.Run.ManifestSha256!, context.Run.StartedAtUtc,
            $"{context.Run.ModuleKey}-{context.Run.EntityKey}-{operation.Id:N}.zip", artifacts, token).ConfigureAwait(false);
    }

    public async Task SaveAsync(Guid id, string schemaJson, string generatorVersion, string manifestSha256,
        DateTimeOffset createdAtUtc, string fileName, IReadOnlyList<GeneratedArtifact> artifacts, CancellationToken token)
    {
        if (artifacts.Sum(artifact => (long)Encoding.UTF8.GetByteCount(artifact.Content) + Encoding.UTF8.GetByteCount(artifact.RelativePath) * 2L + 256) > MaximumPackageBytes)
            throw new CodeGenerationArchiveException(CodeGenerationRunErrorCodes.ArchiveCapacity);
        var bytes = GeneratedArtifactZip.Create(artifacts);
        if (bytes.Length > MaximumPackageBytes) throw new CodeGenerationArchiveException(CodeGenerationRunErrorCodes.ArchiveCapacity);
        var record = new CodeGenerationArchiveRecord { Id = id, SchemaJson = schemaJson, SchemaSha256 = GenerationContentHash.Compute(schemaJson),
            GeneratorVersion = generatorVersion, ManifestSha256 = manifestSha256, ZipSha256 = Hash(bytes), FileName = fileName,
            ByteLength = bytes.Length, ExpiresAtUtc = new DateTimeOffset(createdAtUtc.UtcTicks / 10 * 10, TimeSpan.Zero).AddDays(options.Value.RetentionDays) };
        Validate(record);
        await using var lease = await AcquireAsync(token).ConfigureAwait(false);
        var saved = await FindAsync(id, token).ConfigureAwait(false);
        if (saved is not null)
        {
            Validate(saved);
            // 保留期配置可变，但已经登记的到期时刻不可被恢复者延长。
            if (saved.SchemaJson != record.SchemaJson || saved.GeneratorVersion != record.GeneratorVersion || saved.ManifestSha256 != record.ManifestSha256
                || saved.ZipSha256 != record.ZipSha256 || saved.FileName != record.FileName || saved.ByteLength != record.ByteLength)
                throw new CodeGenerationArchiveException(CodeGenerationRunErrorCodes.ArchiveCorrupt);
            record = saved;
        }
        await GenerationWorkspaceMetadata.ReadAsync(options.Value.RootDirectory, DirectoryPath + ".guard", token).ConfigureAwait(false);
        SweepExpired();
        var path = PackagePath(record);
        if (record.ExpiresAtUtc > clock.UtcNow)
        {
            var existing = await GenerationWorkspaceMetadata.ReadAsync(options.Value.RootDirectory, path, token).ConfigureAwait(false);
            if (existing is not null && existing != Convert.ToBase64String(bytes))
                throw new CodeGenerationArchiveException(CodeGenerationRunErrorCodes.ArchiveCorrupt);
            if (existing is null)
            {
                EnsureCapacity(bytes.Length * 4L / 3 + 4096);
                await GenerationWorkspaceMetadata.CreateAsync(options.Value.RootDirectory, path, Convert.ToBase64String(bytes), token).ConfigureAwait(false);
            }
        }
        if (saved is null)
        {
            var count = await commands.ExecuteAsync(CodeGenerationArchiveSql.Insert, CodeGenerationSqlParameters.Create(
                ("Id", record.Id), ("SchemaJson", record.SchemaJson), ("SchemaSha256", record.SchemaSha256), ("GeneratorVersion", record.GeneratorVersion),
                ("ManifestSha256", record.ManifestSha256), ("ZipSha256", record.ZipSha256), ("FileName", record.FileName),
                ("ByteLength", record.ByteLength), ("ExpiresAtUtc", record.ExpiresAtUtc)), token).ConfigureAwait(false);
            if (count != 1) throw new IOException("代码生成归档元数据插入未命中唯一行。");
        }
    }

    public Task<CodeGenerationArchiveRecord?> FindAsync(Guid id, CancellationToken token) =>
        queries.QuerySingleOrDefaultAsync<CodeGenerationArchiveRecord>(CodeGenerationArchiveSql.Find,
            CodeGenerationSqlParameters.Create(("Id", id)), token);

    public async Task<(CodeGenerationArchiveRecord Record, byte[] Bytes)> ReadAsync(Guid id, CancellationToken token)
    {
        var record = await FindAsync(id, token).ConfigureAwait(false)
            ?? throw new CodeGenerationArchiveException(CodeGenerationRunErrorCodes.ArchiveUnavailable);
        Validate(record);
        if (record.ExpiresAtUtc <= clock.UtcNow) throw new CodeGenerationArchiveException(CodeGenerationRunErrorCodes.ArchiveExpired);
        await using var lease = await AcquireAsync(token).ConfigureAwait(false);
        var content = await GenerationWorkspaceMetadata.ReadAsync(options.Value.RootDirectory, PackagePath(record), token).ConfigureAwait(false)
            ?? throw new CodeGenerationArchiveException(CodeGenerationRunErrorCodes.ArchiveUnavailable);
        byte[] bytes;
        try { bytes = Convert.FromBase64String(content); }
        catch (FormatException) { throw new CodeGenerationArchiveException(CodeGenerationRunErrorCodes.ArchiveCorrupt); }
        if (bytes.LongLength != record.ByteLength || Hash(bytes) != record.ZipSha256)
            throw new CodeGenerationArchiveException(CodeGenerationRunErrorCodes.ArchiveCorrupt);
        return (record, bytes);
    }

    private async Task<GenerationWorkspaceLease> AcquireAsync(CancellationToken token)
    {
        if (string.IsNullOrEmpty(options.Value.RootDirectory) || !Directory.Exists(options.Value.RootDirectory))
            throw new CodeGenerationArchiveException(CodeGenerationRunErrorCodes.ArchiveUnavailable);
        return await GenerationWorkspaceLease.TryAcquireAsync(options.Value.RootDirectory, token).ConfigureAwait(false)
            ?? throw new CodeGenerationArchiveException(CodeGenerationRunErrorCodes.ArchiveBusy);
    }

    private void SweepExpired()
    {
        var root = Path.Combine(options.Value.RootDirectory, ".fullnet", "codegeneration-archives");
        if (!Directory.Exists(root)) return;
        // 文件名仅包含已经冻结的到期微秒与 UUID；未知文件不删除，由容量门阻断无界增长。
        foreach (var file in Directory.EnumerateFiles(root, "*.b64").Take(options.Value.MaximumPackages + 1))
        {
            var name = Path.GetFileNameWithoutExtension(file);
            var separator = name.IndexOf('-');
            if (separator < 1 || !long.TryParse(name.AsSpan(0, separator), NumberStyles.None, CultureInfo.InvariantCulture, out var ticks)
                || !Guid.TryParseExact(name[(separator + 1)..], "N", out _) || ticks > clock.UtcNow.UtcTicks) continue;
            GenerationWorkspaceMetadata.Delete(options.Value.RootDirectory, DirectoryPath + Path.GetFileName(file));
        }
    }

    private void EnsureCapacity(long additional)
    {
        long total = 0;
        var count = 0;
        // 不跟随控制目录链接；每个文件均通过共享受控读取边界重新解析。
        var root = Path.Combine(options.Value.RootDirectory, ".fullnet", "codegeneration-archives");
        if (!Directory.Exists(root)) return;
        foreach (var file in Directory.EnumerateFileSystemEntries(root))
        {
            if ((File.GetAttributes(file) & (FileAttributes.ReparsePoint | FileAttributes.Directory)) != 0) throw new IOException("归档包含不受控目录或链接。");
            total += new FileInfo(file).Length;
            if (++count >= options.Value.MaximumPackages || total + additional > options.Value.MaximumBytes)
                throw new CodeGenerationArchiveException(CodeGenerationRunErrorCodes.ArchiveCapacity);
        }
    }

    private static string PackagePath(CodeGenerationArchiveRecord record) => DirectoryPath + record.ExpiresAtUtc.UtcTicks.ToString(CultureInfo.InvariantCulture) + $"-{record.Id:N}.b64";
    private static string Hash(byte[] bytes) => Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
    private static void Validate(CodeGenerationArchiveRecord record)
    {
        if (record.Id == Guid.Empty || record.ByteLength is < 1 or > MaximumPackageBytes || !GenerationContentHash.IsValid(record.ZipSha256)
            || !GenerationContentHash.IsValid(record.ManifestSha256) || GenerationContentHash.Compute(record.SchemaJson) != record.SchemaSha256
            || record.SchemaJson.Length > 1024 * 1024 || string.IsNullOrWhiteSpace(record.GeneratorVersion) || record.GeneratorVersion.Length > 64
            || record.FileName.Length is < 5 or > 256 || record.FileName != Path.GetFileName(record.FileName)
            || !record.FileName.EndsWith(".zip", StringComparison.Ordinal) || record.FileName.Contains('\\') || record.FileName.Contains('/'))
            throw new CodeGenerationArchiveException(CodeGenerationRunErrorCodes.ArchiveCorrupt);
    }
}

/// <summary>归档失败使用稳定机器码，不将存储路径或源码泄漏给调用方。</summary>
internal sealed class CodeGenerationArchiveException(string code) : IOException("代码生成归档不可用。")
{
    public string Code { get; } = code;
}
