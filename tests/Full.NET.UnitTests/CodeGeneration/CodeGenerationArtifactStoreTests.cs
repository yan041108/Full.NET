using Full.NET.Abstractions.Time;
using Full.NET.Data.Abstractions;
using Full.NET.Data.CodeGeneration.Generation;
using Full.NET.Data.CodeGeneration.Packaging;
using Full.NET.Modules.CodeGeneration.Configuration;
using Full.NET.Modules.CodeGeneration.Contracts;
using Full.NET.Modules.CodeGeneration.Features.ManageHostRuns;
using Full.NET.Modules.CodeGeneration.Persistence;
using Microsoft.Extensions.Options;
using NSubstitute;

namespace Full.NET.UnitTests.CodeGeneration;

/// <summary>历史包读取实际字节，不调用当前生成器或模板；覆盖独立保留和容量失败。</summary>
[TestClass]
public sealed class CodeGenerationArtifactStoreTests
{
    [TestMethod]
    public async Task Inline_preview_download_uses_the_saved_old_generator_bytes_with_no_template_or_apply_workspace()
    {
        var clock = new Clock();
        using var storage = new CodeGenerationArtifactTestStore(clock);
        var id = Guid.CreateVersion7();
        GeneratedArtifact[] artifacts = [new("backend/Old.g.cs", GeneratedArtifactKind.Backend, "old-generator-result\n")];
        var manifest = CodeGenerationRunSummary.ComputeManifestSha256(artifacts);
        await storage.Store.SaveAsync(id, "{}", "old-generator", manifest, clock.UtcNow, "historical.zip", artifacts, default);
        var query = Substitute.For<IQueryExecutor>();
        query.QuerySingleOrDefaultAsync<CodeGenerationRunRecord>(CodeGenerationRunSql.FindById, Arg.Any<object?>(), Arg.Any<CancellationToken>())
            .Returns(new CodeGenerationRunRecord { Id = id, OperationKind = "preview", Status = "succeeded", SchemaSha256 = GenerationContentHash.Compute("{}"), ManifestSha256 = manifest });
        var result = await new CodeGenerationArtifactDownloadService(query, storage.Store).DownloadAsync(id);
        Assert.IsTrue(result.IsSuccess, result.Error?.Code);
        CollectionAssert.AreEqual(GeneratedArtifactZip.Create(artifacts), result.Value!.Content);
        Assert.AreEqual("historical.zip", result.Value.FileName);
        Assert.AreEqual("old-generator", storage.Records[id].GeneratorVersion);
    }

    [TestMethod]
    public async Task Expired_package_is_explicit_even_when_the_storage_is_offline_and_new_saves_sweep_only_expired_bytes()
    {
        var clock = new Clock();
        using var storage = new CodeGenerationArtifactTestStore(clock);
        var id = Guid.CreateVersion7();
        await Save(storage.Store, id, clock.UtcNow);
        var old = Directory.GetFiles(Path.Combine(storage.Root, ".fullnet", "codegeneration-archives")).Single();
        clock.UtcNow = clock.UtcNow.AddDays(31);
        storage.Options.RootDirectory = string.Empty;
        var expired = await Assert.ThrowsExactlyAsync<CodeGenerationArchiveException>(() => storage.Store.ReadAsync(id, default));
        Assert.AreEqual(CodeGenerationRunErrorCodes.ArchiveExpired, expired.Code);
        storage.Options.RootDirectory = storage.Root;
        await Save(storage.Store, Guid.CreateVersion7(), clock.UtcNow);
        Assert.IsFalse(File.Exists(old));
        Assert.IsTrue(storage.Records.ContainsKey(id));
        Assert.AreEqual(1, Directory.GetFiles(Path.Combine(storage.Root, ".fullnet", "codegeneration-archives")).Length);
    }

    [TestMethod]
    public async Task Tampering_is_preserved_and_replay_does_not_overwrite_or_extend_retention()
    {
        var clock = new Clock();
        using var storage = new CodeGenerationArtifactTestStore(clock);
        var id = Guid.CreateVersion7();
        await Save(storage.Store, id, clock.UtcNow);
        var expiry = storage.Records[id].ExpiresAtUtc;
        storage.Options.RetentionDays = 90;
        await Save(storage.Store, id, clock.UtcNow);
        Assert.AreEqual(expiry, storage.Records[id].ExpiresAtUtc);
        var path = Directory.GetFiles(Path.Combine(storage.Root, ".fullnet", "codegeneration-archives")).Single();
        await File.WriteAllTextAsync(path, "human-corruption");
        var read = await Assert.ThrowsExactlyAsync<CodeGenerationArchiveException>(() => storage.Store.ReadAsync(id, default));
        var replay = await Assert.ThrowsExactlyAsync<CodeGenerationArchiveException>(() => Save(storage.Store, id, clock.UtcNow));
        Assert.AreEqual(CodeGenerationRunErrorCodes.ArchiveCorrupt, read.Code);
        Assert.AreEqual(CodeGenerationRunErrorCodes.ArchiveCorrupt, replay.Code);
        Assert.AreEqual("human-corruption", await File.ReadAllTextAsync(path));
    }

    [TestMethod]
    public async Task Capacity_and_live_lease_refuse_before_a_second_archive_metadata_row()
    {
        var clock = new Clock();
        using var storage = new CodeGenerationArtifactTestStore(clock);
        storage.Options.MaximumPackages = 1;
        await Save(storage.Store, Guid.CreateVersion7(), clock.UtcNow);
        var full = await Assert.ThrowsExactlyAsync<CodeGenerationArchiveException>(() => Save(storage.Store, Guid.CreateVersion7(), clock.UtcNow));
        Assert.AreEqual(CodeGenerationRunErrorCodes.ArchiveCapacity, full.Code);
        await using var lease = await GenerationWorkspaceLease.TryAcquireAsync(storage.Root);
        Assert.IsNotNull(lease);
        var busy = await Assert.ThrowsExactlyAsync<CodeGenerationArchiveException>(() => Save(storage.Store, Guid.CreateVersion7(), clock.UtcNow));
        Assert.AreEqual(CodeGenerationRunErrorCodes.ArchiveBusy, busy.Code);
        Assert.AreEqual(1, storage.Records.Count);
    }

    [TestMethod]
    public void Configuration_rejects_public_or_overlapping_apply_roots()
    {
        var clock = new Clock();
        using var storage = new CodeGenerationArtifactTestStore(clock);
        var validator = new CodeGenerationArtifactOptionsValidator(Options.Create(new CodeGenerationApplyOptions { WorkspaceRoot = storage.Root }));
        Assert.IsTrue(validator.Validate(null, storage.Options).Failed);
        var publicRoot = Directory.CreateDirectory(Path.Combine(storage.Root, "wwwroot", "archives")).FullName;
        Assert.IsTrue(validator.Validate(null, new() { RootDirectory = publicRoot }).Failed);
    }

    private static Task Save(CodeGenerationArtifactStore store, Guid id, DateTimeOffset created) => store.SaveAsync(id, "{}", "frozen-v1",
        new string('a', 64), created, "saved.zip", [new("readme.txt", GeneratedArtifactKind.Report, "saved")], default);

    private sealed class Clock : IClock
    {
        public DateTimeOffset UtcNow { get; set; } = new(2026, 10, 26, 0, 0, 0, TimeSpan.Zero);
    }
}
