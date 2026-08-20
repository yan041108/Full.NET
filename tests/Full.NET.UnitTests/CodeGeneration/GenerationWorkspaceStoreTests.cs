using System.Security.Cryptography;
using System.Text;
using Full.NET.Data.CodeGeneration.Generation;

namespace Full.NET.UnitTests.CodeGeneration;

[TestClass]
public sealed class GenerationWorkspaceStoreTests
{
    [TestMethod]
    public async Task Capture_reads_expected_files_and_previous_manifest()
    {
        using var workspace = TemporaryWorkspace.Create();
        var existing = Artifact("backend/existing.g.cs", "existing");
        var missing = Artifact("backend/missing.g.cs", "missing");
        workspace.Write(existing.RelativePath, existing.Content);
        workspace.Write("unrelated.txt", "ignore");
        workspace.Write(
            GenerationWorkspaceStore.ManifestRelativePath,
            GenerationManifest.Create(
                [new(existing.RelativePath, Hash(existing.Content))])
                .ToJson());

        var snapshot = await GenerationWorkspaceStore.CaptureAsync(
            workspace.RootPath,
            [missing, existing]);

        Assert.AreEqual(1, snapshot.ExistingFiles.Count);
        Assert.AreEqual(
            existing.Content,
            snapshot.ExistingFiles[existing.RelativePath]);
        Assert.IsFalse(snapshot.ExistingFiles.ContainsKey(missing.RelativePath));
        Assert.IsNotNull(snapshot.PreviousManifest);
        Assert.IsTrue(snapshot.PreviousManifest.TryGetSha256(
            existing.RelativePath,
            out var sha256));
        Assert.AreEqual(Hash(existing.Content), sha256);

        var plan = GenerationWritePlanner.Plan(
            [existing],
            snapshot.ExistingFiles,
            snapshot.PreviousManifest);
        Assert.AreSame(snapshot.PreviousManifest, plan.PreviousManifest);
    }

    [TestMethod]
    public async Task Openapi_contract_round_trips_through_workspace_store()
    {
        using var workspace = TemporaryWorkspace.Create();
        var artifact = new GeneratedArtifact(
            "contracts/openapi/products.generated.openapi.json",
            GeneratedArtifactKind.OpenApiContract,
            "{\"openapi\":\"3.1.0\"}\n");
        var snapshot = await GenerationWorkspaceStore.CaptureAsync(
            workspace.RootPath,
            [artifact]);
        var plan = GenerationWritePlanner.Plan(
            [artifact],
            snapshot.ExistingFiles,
            snapshot.PreviousManifest);

        await GenerationWorkspaceStore.ApplyAsync(workspace.RootPath, plan);

        Assert.AreEqual(artifact.Content, workspace.Read(artifact.RelativePath));
        var manifest = GenerationManifest.Parse(workspace.Read(
            GenerationWorkspaceStore.ManifestRelativePath));
        Assert.IsTrue(manifest.TryGetSha256(artifact.RelativePath, out var sha256));
        Assert.AreEqual(Hash(artifact.Content), sha256);
    }

    [TestMethod]
    public async Task Capture_rejects_missing_root_invalid_utf8_and_bom()
    {
        using var workspace = TemporaryWorkspace.Create();
        var invalidPath = "backend/invalid.g.cs";
        workspace.WriteBytes(invalidPath, [0xC3, 0x28]);
        var bomPath = "backend/bom.g.cs";
        workspace.WriteBytes(
            bomPath,
            [0xEF, 0xBB, 0xBF, (byte)'x']);

        await Assert.ThrowsExactlyAsync<DirectoryNotFoundException>(() =>
            GenerationWorkspaceStore.CaptureAsync(
                Path.Combine(workspace.RootPath, "missing"),
                [Artifact("backend/item.g.cs", "content")]));
        await Assert.ThrowsExactlyAsync<DecoderFallbackException>(() =>
            GenerationWorkspaceStore.CaptureAsync(
                workspace.RootPath,
                [Artifact(invalidPath, "content")]));
        await Assert.ThrowsExactlyAsync<DecoderFallbackException>(() =>
            GenerationWorkspaceStore.CaptureAsync(
                workspace.RootPath,
                [Artifact(bomPath, "content")]));
        await Assert.ThrowsExactlyAsync<ArgumentException>(() =>
            GenerationWorkspaceStore.CaptureAsync(
                workspace.RootPath,
                [Artifact(".fullnet/owned.g.cs", "content")]));

        using var manifestBomWorkspace = TemporaryWorkspace.Create();
        var manifestJson = GenerationManifest.Create([]).ToJson();
        manifestBomWorkspace.WriteBytes(
            GenerationWorkspaceStore.ManifestRelativePath,
            [0xEF, 0xBB, 0xBF, .. Encoding.UTF8.GetBytes(manifestJson)]);
        await Assert.ThrowsExactlyAsync<DecoderFallbackException>(() =>
            GenerationWorkspaceStore.CaptureAsync(
                manifestBomWorkspace.RootPath,
                []));

        using var desiredBomWorkspace = TemporaryWorkspace.Create();
        var desiredBom = Artifact(
            "backend/desired-bom.g.cs",
            "\uFEFFcontent");
        var desiredBomSnapshot =
            await GenerationWorkspaceStore.CaptureAsync(
                desiredBomWorkspace.RootPath,
                [desiredBom]);
        var desiredBomPlan = GenerationWritePlanner.Plan(
            [desiredBom],
            desiredBomSnapshot.ExistingFiles,
            desiredBomSnapshot.PreviousManifest);

        await Assert.ThrowsExactlyAsync<EncoderFallbackException>(() =>
            GenerationWorkspaceStore.ApplyAsync(
                desiredBomWorkspace.RootPath,
                desiredBomPlan));
        Assert.IsFalse(File.Exists(
            desiredBomWorkspace.PathOf(desiredBom.RelativePath)));
    }

    [TestMethod]
    public async Task Capture_rejects_actual_path_casing_alias()
    {
        using var workspace = TemporaryWorkspace.Create();
        workspace.Write("Backend/item.g.cs", "content");

        await Assert.ThrowsExactlyAsync<GenerationWorkspaceConflictException>(
            () => GenerationWorkspaceStore.CaptureAsync(
                workspace.RootPath,
                [Artifact("backend/item.g.cs", "content")]));
    }

    [TestMethod]
    public async Task Capture_rejects_reparse_point_parent()
    {
        using var workspace = TemporaryWorkspace.Create();
        var actualDirectory = Path.Combine(workspace.RootPath, "actual");
        var linkDirectory = Path.Combine(workspace.RootPath, "linked");
        Directory.CreateDirectory(actualDirectory);
        File.WriteAllText(
            Path.Combine(actualDirectory, "item.g.cs"),
            "content",
            new UTF8Encoding(false, true));

        try
        {
            Directory.CreateSymbolicLink(linkDirectory, actualDirectory);
        }
        catch (Exception exception)
            when (exception is UnauthorizedAccessException
                or PlatformNotSupportedException
                or IOException)
        {
            Assert.Inconclusive(
                $"当前文件系统不能创建目录符号链接：{exception.Message}");
            return;
        }

        await Assert.ThrowsExactlyAsync<GenerationWorkspaceConflictException>(
            () => GenerationWorkspaceStore.CaptureAsync(
                workspace.RootPath,
                [Artifact("linked/item.g.cs", "content")]));

        Directory.Delete(linkDirectory);
        var danglingLink = Path.Combine(workspace.RootPath, "dangling");
        try
        {
            File.CreateSymbolicLink(
                danglingLink,
                Path.Combine(workspace.RootPath, "missing-target"));
        }
        catch (Exception exception)
            when (exception is UnauthorizedAccessException
                or PlatformNotSupportedException
                or IOException)
        {
            Assert.Inconclusive(
                $"当前文件系统不能创建悬空符号链接：{exception.Message}");
            return;
        }

        await Assert.ThrowsExactlyAsync<GenerationWorkspaceConflictException>(
            () => GenerationWorkspaceStore.CaptureAsync(
                workspace.RootPath,
                [Artifact("dangling", "content")]));
    }

    [TestMethod]
    public async Task Pending_manifest_recovery_blocks_capture_and_apply()
    {
        using var workspace = TemporaryWorkspace.Create();
        var artifact = Artifact("backend/item.g.cs", "desired");
        var snapshot = await GenerationWorkspaceStore.CaptureAsync(
            workspace.RootPath,
            [artifact]);
        var plan = GenerationWritePlanner.Plan(
            [artifact],
            snapshot.ExistingFiles,
            snapshot.PreviousManifest);
        var recoveryRelativePath =
            $".fullnet/codegeneration-manifest-{Guid.NewGuid():N}.recovery";
        var recoveryContent = GenerationManifest.Create(
            [new(artifact.RelativePath, Hash("previous"))]).ToJson();
        workspace.Write(recoveryRelativePath, recoveryContent);

        var captureException =
            await Assert.ThrowsExactlyAsync<
                GenerationWorkspaceConflictException>(() =>
                GenerationWorkspaceStore.CaptureAsync(
                    workspace.RootPath,
                    [artifact]));
        var applyException =
            await Assert.ThrowsExactlyAsync<
                GenerationWorkspaceConflictException>(() =>
                GenerationWorkspaceStore.ApplyAsync(
                    workspace.RootPath,
                    plan));

        Assert.AreEqual(
            recoveryRelativePath,
            captureException.RelativePath);
        Assert.AreEqual(
            recoveryRelativePath,
            applyException.RelativePath);
        Assert.AreEqual(
            recoveryContent,
            workspace.Read(recoveryRelativePath));
        Assert.IsFalse(File.Exists(workspace.PathOf(
            artifact.RelativePath)));
        Assert.AreEqual(0, workspace.FindTemporaryFiles().Length);

        using var unrelatedWorkspace = TemporaryWorkspace.Create();
        unrelatedWorkspace.Write(
            ".fullnet/codegeneration-manifest-not-a-guid.recovery",
            "unrelated");
        unrelatedWorkspace.Write(
            ".fullnet/editor-session.recovery",
            "unrelated");

        var unrelatedSnapshot =
            await GenerationWorkspaceStore.CaptureAsync(
                unrelatedWorkspace.RootPath,
                [artifact]);

        Assert.AreEqual(0, unrelatedSnapshot.ExistingFiles.Count);
        Assert.IsNull(unrelatedSnapshot.PreviousManifest);

        using var casingWorkspace = TemporaryWorkspace.Create();
        var casingRecoveryPath =
            $".fullnet/CODEGENERATION-MANIFEST-{Guid.NewGuid():N}.RECOVERY";
        casingWorkspace.Write(
            casingRecoveryPath,
            recoveryContent);

        await Assert.ThrowsExactlyAsync<GenerationWorkspaceConflictException>(
            () => GenerationWorkspaceStore.CaptureAsync(
                casingWorkspace.RootPath,
                [artifact]));
    }

    [TestMethod]
    public async Task Pending_delete_recovery_blocks_capture_and_apply()
    {
        using var workspace = TemporaryWorkspace.Create();
        var artifact = Artifact("backend/item.g.cs", "desired");
        var snapshot = await GenerationWorkspaceStore.CaptureAsync(
            workspace.RootPath,
            [artifact]);
        var plan = GenerationWritePlanner.Plan(
            [artifact],
            snapshot.ExistingFiles,
            snapshot.PreviousManifest);
        var identifier = Guid.NewGuid().ToString("N");
        var recoveryRelativePath =
            $".fullnet/codegeneration-delete-recovery/"
            + $"{identifier}.recovery";
        var metadataRelativePath =
            $".fullnet/codegeneration-delete-recovery/"
            + $"{identifier}.path";
        workspace.Write(recoveryRelativePath, "previous-content");
        workspace.Write(metadataRelativePath, artifact.RelativePath);

        var captureException =
            await Assert.ThrowsExactlyAsync<
                GenerationWorkspaceConflictException>(() =>
                GenerationWorkspaceStore.CaptureAsync(
                    workspace.RootPath,
                    [artifact]));
        var applyException =
            await Assert.ThrowsExactlyAsync<
                GenerationWorkspaceConflictException>(() =>
                GenerationWorkspaceStore.ApplyAsync(
                    workspace.RootPath,
                    plan));

        Assert.AreEqual(
            ".fullnet/codegeneration-delete-recovery",
            Path.GetDirectoryName(
                    captureException.RelativePath!)
                ?.Replace('\\', '/'));
        Assert.AreEqual(
            captureException.RelativePath,
            applyException.RelativePath);
        Assert.AreEqual(
            "previous-content",
            workspace.Read(recoveryRelativePath));
        Assert.AreEqual(
            artifact.RelativePath,
            workspace.Read(metadataRelativePath));

        using var orphanWorkspace = TemporaryWorkspace.Create();
        var orphanIdentifier = Guid.NewGuid().ToString("N");
        orphanWorkspace.Write(
            $".fullnet/codegeneration-delete-recovery/"
            + $"{orphanIdentifier}.committed",
            artifact.RelativePath);
        await Assert.ThrowsExactlyAsync<GenerationWorkspaceConflictException>(
            () => GenerationWorkspaceStore.CaptureAsync(
                orphanWorkspace.RootPath,
                [artifact]));

        foreach (var malformedName in new[]
                 {
                     "not-a-guid.recovery",
                     $"{Guid.NewGuid():N}.unknown",
                 })
        {
            using var malformedWorkspace = TemporaryWorkspace.Create();
            malformedWorkspace.Write(
                $".fullnet/codegeneration-delete-recovery/{malformedName}",
                "invalid-state");
            await Assert.ThrowsExactlyAsync<
                GenerationWorkspaceConflictException>(() =>
                GenerationWorkspaceStore.CaptureAsync(
                    malformedWorkspace.RootPath,
                    [artifact]));
        }

        using var casingWorkspace = TemporaryWorkspace.Create();
        var casingIdentifier =
            "abcdef0123456789abcdef0123456789"
            .ToUpperInvariant();
        casingWorkspace.Write(
            $".fullnet/codegeneration-delete-recovery/"
            + $"{casingIdentifier}.recovery",
            "previous-content");
        casingWorkspace.Write(
            $".fullnet/codegeneration-delete-recovery/"
            + $"{casingIdentifier}.committed",
            artifact.RelativePath);
        await Assert.ThrowsExactlyAsync<GenerationWorkspaceConflictException>(
            () => GenerationWorkspaceStore.CaptureAsync(
                casingWorkspace.RootPath,
                [artifact]));
    }

    [TestMethod]
    public async Task Apply_creates_updates_and_keeps_unchanged_before_manifest()
    {
        using var workspace = TemporaryWorkspace.Create();
        var create = Artifact("backend/create.g.cs", "created");
        var update = Artifact("backend/update.g.cs", "updated");
        var unchanged = Artifact("backend/unchanged.g.cs", "same");
        workspace.Write(update.RelativePath, "old");
        workspace.Write(unchanged.RelativePath, unchanged.Content);
        workspace.Write(
            GenerationWorkspaceStore.ManifestRelativePath,
            GenerationManifest.Create(
            [
                new(update.RelativePath, Hash("old")),
                new(unchanged.RelativePath, Hash(unchanged.Content)),
            ]).ToJson());
        var snapshot = await GenerationWorkspaceStore.CaptureAsync(
            workspace.RootPath,
            [create, update, unchanged]);
        var plan = GenerationWritePlanner.Plan(
            [create, update, unchanged],
            snapshot.ExistingFiles,
            snapshot.PreviousManifest);

        await GenerationWorkspaceStore.ApplyAsync(
            workspace.RootPath,
            plan);

        Assert.AreEqual(create.Content, workspace.Read(create.RelativePath));
        Assert.AreEqual(update.Content, workspace.Read(update.RelativePath));
        Assert.AreEqual(
            unchanged.Content,
            workspace.Read(unchanged.RelativePath));
        Assert.AreEqual(
            plan.NextManifest!.ToJson(),
            workspace.Read(GenerationWorkspaceStore.ManifestRelativePath));
        Assert.AreEqual(0, workspace.FindTemporaryFiles().Length);
    }

    [TestMethod]
    public async Task Capture_and_apply_deletes_only_unmodified_stale_owned_file()
    {
        using var workspace = TemporaryWorkspace.Create();
        var current = Artifact("backend/current.g.cs", "current");
        var stalePath = "backend/stale.g.cs";
        workspace.Write(current.RelativePath, current.Content);
        workspace.Write(stalePath, "stale");
        workspace.Write(
            GenerationWorkspaceStore.ManifestRelativePath,
            GenerationManifest.Create(
            [
                new(current.RelativePath, Hash(current.Content)),
                new(stalePath, Hash("stale")),
            ]).ToJson());

        var snapshot = await GenerationWorkspaceStore.CaptureAsync(
            workspace.RootPath,
            [current]);
        Assert.AreEqual("stale", snapshot.ExistingFiles[stalePath]);
        var plan = GenerationWritePlanner.Plan(
            [current],
            snapshot.ExistingFiles,
            snapshot.PreviousManifest);

        await GenerationWorkspaceStore.ApplyAsync(
            workspace.RootPath,
            plan);

        Assert.IsFalse(File.Exists(workspace.PathOf(stalePath)));
        Assert.AreEqual(current.Content, workspace.Read(current.RelativePath));
        Assert.AreEqual(
            plan.NextManifest!.ToJson(),
            workspace.Read(GenerationWorkspaceStore.ManifestRelativePath));
        Assert.AreEqual(
            1,
            workspace.FindDeleteRecoveryFiles().Length);
        Assert.AreEqual(
            0,
            workspace.FindDeleteRecoveryMetadataFiles(".path").Length);
        var committedMetadata =
            workspace.FindDeleteRecoveryMetadataFiles(".committed")
                .Single();
        Assert.AreEqual(
            stalePath,
            File.ReadAllText(
                committedMetadata,
                new UTF8Encoding(false, true)));

        var nextSnapshot = await GenerationWorkspaceStore.CaptureAsync(
            workspace.RootPath,
            [current]);
        var nextPlan = GenerationWritePlanner.Plan(
            [current],
            nextSnapshot.ExistingFiles,
            nextSnapshot.PreviousManifest);
        await GenerationWorkspaceStore.ApplyAsync(
            workspace.RootPath,
            nextPlan);

        Assert.IsTrue(nextPlan.CanApply);
        Assert.AreEqual(
            1,
            workspace.FindDeleteRecoveryFiles().Length);
        Assert.AreEqual(0, workspace.FindTemporaryFiles().Length);
    }

    [TestMethod]
    public async Task Apply_changed_stale_delete_target_fails_before_writes()
    {
        using var workspace = TemporaryWorkspace.Create();
        var create = Artifact("backend/create.g.cs", "created");
        var stalePath = "backend/stale.g.cs";
        workspace.Write(stalePath, "stale");
        var previousManifest = GenerationManifest.Create(
            [new(stalePath, Hash("stale"))]);
        workspace.Write(
            GenerationWorkspaceStore.ManifestRelativePath,
            previousManifest.ToJson());
        var snapshot = await GenerationWorkspaceStore.CaptureAsync(
            workspace.RootPath,
            [create]);
        var plan = GenerationWritePlanner.Plan(
            [create],
            snapshot.ExistingFiles,
            snapshot.PreviousManifest);
        workspace.Write(stalePath, "concurrent-user-edit");

        await Assert.ThrowsExactlyAsync<GenerationWorkspaceConflictException>(
            () => GenerationWorkspaceStore.ApplyAsync(
                workspace.RootPath,
                plan));

        Assert.AreEqual("concurrent-user-edit", workspace.Read(stalePath));
        Assert.IsFalse(File.Exists(workspace.PathOf(create.RelativePath)));
        Assert.AreEqual(
            previousManifest.ToJson(),
            workspace.Read(GenerationWorkspaceStore.ManifestRelativePath));
        Assert.AreEqual(0, workspace.FindTemporaryFiles().Length);
    }

    [TestMethod]
    public async Task Apply_replacement_at_delete_claim_survives_conflict()
    {
        using var workspace = TemporaryWorkspace.Create();
        var stalePath = "backend/stale.g.cs";
        var previousManifest = GenerationManifest.Create(
            [new(stalePath, Hash("stale"))]);
        workspace.Write(stalePath, "stale");
        workspace.Write(
            GenerationWorkspaceStore.ManifestRelativePath,
            previousManifest.ToJson());
        var snapshot = await GenerationWorkspaceStore.CaptureAsync(
            workspace.RootPath,
            []);
        var plan = GenerationWritePlanner.Plan(
            [],
            snapshot.ExistingFiles,
            snapshot.PreviousManifest);

        await Assert.ThrowsExactlyAsync<GenerationWorkspaceConflictException>(
            () => GenerationWorkspaceStore.ApplyForTestingAsync(
                workspace.RootPath,
                plan,
                () => Task.CompletedTask,
                beforeDeleteClaim: () =>
                {
                    workspace.Write(stalePath, "user-replacement");
                    return Task.CompletedTask;
                }));

        Assert.AreEqual("user-replacement", workspace.Read(stalePath));
        Assert.AreEqual(
            previousManifest.ToJson(),
            workspace.Read(GenerationWorkspaceStore.ManifestRelativePath));
    }

    [TestMethod]
    public async Task Apply_invalid_utf8_replacement_at_delete_claim_is_restored()
    {
        using var workspace = TemporaryWorkspace.Create();
        var stalePath = "backend/stale.g.cs";
        var previousManifest = GenerationManifest.Create(
            [new(stalePath, Hash("stale"))]);
        workspace.Write(stalePath, "stale");
        workspace.Write(
            GenerationWorkspaceStore.ManifestRelativePath,
            previousManifest.ToJson());
        var snapshot = await GenerationWorkspaceStore.CaptureAsync(
            workspace.RootPath,
            []);
        var plan = GenerationWritePlanner.Plan(
            [],
            snapshot.ExistingFiles,
            snapshot.PreviousManifest);
        var replacement = new byte[] { 0xC3, 0x28 };

        await Assert.ThrowsExactlyAsync<GenerationWorkspaceConflictException>(
            () => GenerationWorkspaceStore.ApplyForTestingAsync(
                workspace.RootPath,
                plan,
                () => Task.CompletedTask,
                beforeDeleteClaim: () =>
                {
                    workspace.WriteBytes(stalePath, replacement);
                    return Task.CompletedTask;
                }));

        CollectionAssert.AreEqual(
            replacement,
            File.ReadAllBytes(workspace.PathOf(stalePath)));
    }

    [TestMethod]
    public async Task Apply_restore_failure_does_not_skip_other_delete_claims()
    {
        using var workspace = TemporaryWorkspace.Create();
        var firstPath = "backend/first.g.cs";
        var secondPath = "backend/second.g.cs";
        workspace.Write(firstPath, "first");
        workspace.Write(secondPath, "second");
        var previousManifest = GenerationManifest.Create(
        [
            new(firstPath, Hash("first")),
            new(secondPath, Hash("second")),
        ]);
        workspace.Write(
            GenerationWorkspaceStore.ManifestRelativePath,
            previousManifest.ToJson());
        var snapshot = await GenerationWorkspaceStore.CaptureAsync(
            workspace.RootPath,
            []);
        var plan = GenerationWritePlanner.Plan(
            [],
            snapshot.ExistingFiles,
            snapshot.PreviousManifest);

        await Assert.ThrowsExactlyAsync<GenerationWorkspaceConflictException>(
            () => GenerationWorkspaceStore.ApplyForTestingAsync(
                workspace.RootPath,
                plan,
                () => Task.CompletedTask,
                afterArtifactCommit: committedCount =>
                {
                    if (committedCount == 2)
                    {
                        workspace.Write(
                            secondPath,
                            "user-replacement");
                    }

                    return Task.CompletedTask;
                }));

        Assert.AreEqual("first", workspace.Read(firstPath));
        Assert.AreEqual("user-replacement", workspace.Read(secondPath));
    }

    [TestMethod]
    public async Task Apply_open_handle_edit_after_delete_claim_is_restored()
    {
        using var workspace = TemporaryWorkspace.Create();
        var stalePath = "backend/stale.g.cs";
        var previousManifest = GenerationManifest.Create(
            [new(stalePath, Hash("stale"))]);
        workspace.Write(stalePath, "stale");
        workspace.Write(
            GenerationWorkspaceStore.ManifestRelativePath,
            previousManifest.ToJson());
        var snapshot = await GenerationWorkspaceStore.CaptureAsync(
            workspace.RootPath,
            []);
        var plan = GenerationWritePlanner.Plan(
            [],
            snapshot.ExistingFiles,
            snapshot.PreviousManifest);
        FileStream? retainedHandle = null;
        try
        {
            await Assert.ThrowsExactlyAsync<
                GenerationWorkspaceConflictException>(() =>
                    GenerationWorkspaceStore.ApplyForTestingAsync(
                        workspace.RootPath,
                        plan,
                        () => Task.CompletedTask,
                        afterArtifactCommit: _ =>
                        {
                            retainedHandle = OpenRetainedWriteHandle(
                                workspace.FindDeleteRecoveryFiles().Single());
                            WriteRetainedHandle(
                                retainedHandle,
                                "post-claim-user-edit");
                            return Task.CompletedTask;
                    }));

            await retainedHandle!.DisposeAsync();
            retainedHandle = null;
            Assert.AreEqual(
                "post-claim-user-edit",
                workspace.Read(stalePath));
            Assert.AreEqual(
                previousManifest.ToJson(),
                workspace.Read(
                    GenerationWorkspaceStore.ManifestRelativePath));
        }
        finally
        {
            if (retainedHandle is not null)
            {
                await retainedHandle.DisposeAsync();
            }
        }
    }

    [TestMethod]
    public async Task Apply_open_handle_edit_after_manifest_commit_remains_in_tombstone()
    {
        using var workspace = TemporaryWorkspace.Create();
        var stalePath = "backend/stale.g.cs";
        var previousManifest = GenerationManifest.Create(
            [new(stalePath, Hash("stale"))]);
        workspace.Write(stalePath, "stale");
        workspace.Write(
            GenerationWorkspaceStore.ManifestRelativePath,
            previousManifest.ToJson());
        var snapshot = await GenerationWorkspaceStore.CaptureAsync(
            workspace.RootPath,
            []);
        var plan = GenerationWritePlanner.Plan(
            [],
            snapshot.ExistingFiles,
            snapshot.PreviousManifest);
        FileStream? retainedHandle = null;
        try
        {
            await GenerationWorkspaceStore.ApplyForTestingAsync(
                workspace.RootPath,
                plan,
                () => Task.CompletedTask,
                beforeManifestRecoveryCleanup: () =>
                {
                    retainedHandle = OpenRetainedWriteHandle(
                        workspace.FindDeleteRecoveryFiles().Single());
                    WriteRetainedHandle(
                        retainedHandle,
                        "post-manifest-user-edit");
                    return Task.CompletedTask;
                });

            await retainedHandle!.DisposeAsync();
            retainedHandle = null;
            Assert.IsFalse(File.Exists(workspace.PathOf(stalePath)));
            Assert.AreEqual(
                plan.NextManifest!.ToJson(),
                workspace.Read(
                    GenerationWorkspaceStore.ManifestRelativePath));
            var recoveryPath = workspace.FindRecoveryFiles().Single();
            Assert.AreEqual(
                "post-manifest-user-edit",
                File.ReadAllText(
                    recoveryPath,
                    new UTF8Encoding(false, true)));
            Assert.AreEqual(
                1,
                workspace.FindDeleteRecoveryMetadataFiles(
                    ".committed").Length);
            Assert.AreEqual(
                0,
                workspace.FindDeleteRecoveryMetadataFiles(".path").Length);
        }
        finally
        {
            if (retainedHandle is not null)
            {
                await retainedHandle.DisposeAsync();
            }
        }
    }

    [TestMethod]
    public async Task Apply_cancellation_before_first_commit_writes_nothing()
    {
        using var workspace = TemporaryWorkspace.Create();
        var artifact = Artifact("backend/item.g.cs", "desired");
        var snapshot = await GenerationWorkspaceStore.CaptureAsync(
            workspace.RootPath,
            [artifact]);
        var plan = GenerationWritePlanner.Plan(
            [artifact],
            snapshot.ExistingFiles,
            snapshot.PreviousManifest);
        using var cancellation = new CancellationTokenSource();

        await Assert.ThrowsExactlyAsync<OperationCanceledException>(() =>
            GenerationWorkspaceStore.ApplyForTestingAsync(
                workspace.RootPath,
                plan,
                () => Task.CompletedTask,
                cancellationToken: cancellation.Token,
                beforeFirstArtifactCommit: () =>
                {
                    cancellation.Cancel();
                    return Task.CompletedTask;
                }));

        Assert.IsFalse(File.Exists(workspace.PathOf(
            artifact.RelativePath)));
        Assert.IsFalse(File.Exists(workspace.PathOf(
            GenerationWorkspaceStore.ManifestRelativePath)));
        Assert.AreEqual(0, workspace.FindTemporaryFiles().Length);
    }

    [TestMethod]
    public async Task Apply_missing_delete_recovery_preserves_path_evidence()
    {
        using var workspace = TemporaryWorkspace.Create();
        var stalePath = "backend/stale.g.cs";
        var previousManifest = GenerationManifest.Create(
            [new(stalePath, Hash("stale"))]);
        workspace.Write(stalePath, "stale");
        workspace.Write(
            GenerationWorkspaceStore.ManifestRelativePath,
            previousManifest.ToJson());
        var snapshot = await GenerationWorkspaceStore.CaptureAsync(
            workspace.RootPath,
            []);
        var plan = GenerationWritePlanner.Plan(
            [],
            snapshot.ExistingFiles,
            snapshot.PreviousManifest);

        await Assert.ThrowsExactlyAsync<GenerationWorkspaceConflictException>(
            () => GenerationWorkspaceStore.ApplyForTestingAsync(
                workspace.RootPath,
                plan,
                () => Task.CompletedTask,
                afterArtifactCommit: _ =>
                {
                    File.Delete(
                        workspace.FindDeleteRecoveryFiles().Single());
                    return Task.CompletedTask;
                }));

        Assert.IsFalse(File.Exists(workspace.PathOf(stalePath)));
        var metadataPath =
            workspace.FindDeleteRecoveryMetadataFiles(".path").Single();
        Assert.AreEqual(
            stalePath,
            File.ReadAllText(
                metadataPath,
                new UTF8Encoding(false, true)));
        await Assert.ThrowsExactlyAsync<GenerationWorkspaceConflictException>(
            () => GenerationWorkspaceStore.CaptureAsync(
                workspace.RootPath,
                []));
    }

    [TestMethod]
    public async Task Apply_missing_recovery_after_manifest_reports_incomplete_tombstone()
    {
        using var workspace = TemporaryWorkspace.Create();
        var stalePath = "backend/stale.g.cs";
        var previousManifest = GenerationManifest.Create(
            [new(stalePath, Hash("stale"))]);
        workspace.Write(stalePath, "stale");
        workspace.Write(
            GenerationWorkspaceStore.ManifestRelativePath,
            previousManifest.ToJson());
        var snapshot = await GenerationWorkspaceStore.CaptureAsync(
            workspace.RootPath,
            []);
        var plan = GenerationWritePlanner.Plan(
            [],
            snapshot.ExistingFiles,
            snapshot.PreviousManifest);

        var exception =
            await Assert.ThrowsExactlyAsync<
                GenerationWorkspaceConflictException>(() =>
                GenerationWorkspaceStore.ApplyForTestingAsync(
                    workspace.RootPath,
                    plan,
                    () => Task.CompletedTask,
                    beforeManifestRecoveryCleanup: () =>
                    {
                        File.Delete(
                            workspace.FindDeleteRecoveryFiles().Single());
                        return Task.CompletedTask;
                    }));

        StringAssert.Contains(exception.Message, "生成清单已提交");
        Assert.IsFalse(File.Exists(workspace.PathOf(stalePath)));
        Assert.AreEqual(
            plan.NextManifest!.ToJson(),
            workspace.Read(GenerationWorkspaceStore.ManifestRelativePath));
        Assert.AreEqual(
            1,
            workspace.FindDeleteRecoveryMetadataFiles(".committed").Length);
        Assert.AreEqual(
            0,
            workspace.FindDeleteRecoveryMetadataFiles(".path").Length);
        await Assert.ThrowsExactlyAsync<GenerationWorkspaceConflictException>(
            () => GenerationWorkspaceStore.CaptureAsync(
                workspace.RootPath,
                []));
    }

    [TestMethod]
    public async Task Apply_changed_tombstone_path_after_manifest_reports_conflict()
    {
        using var workspace = TemporaryWorkspace.Create();
        var stalePath = "backend/stale.g.cs";
        var previousManifest = GenerationManifest.Create(
            [new(stalePath, Hash("stale"))]);
        workspace.Write(stalePath, "stale");
        workspace.Write(
            GenerationWorkspaceStore.ManifestRelativePath,
            previousManifest.ToJson());
        var snapshot = await GenerationWorkspaceStore.CaptureAsync(
            workspace.RootPath,
            []);
        var plan = GenerationWritePlanner.Plan(
            [],
            snapshot.ExistingFiles,
            snapshot.PreviousManifest);

        var exception =
            await Assert.ThrowsExactlyAsync<
                GenerationWorkspaceConflictException>(() =>
                GenerationWorkspaceStore.ApplyForTestingAsync(
                    workspace.RootPath,
                    plan,
                    () => Task.CompletedTask,
                    beforeManifestRecoveryCleanup: () =>
                    {
                        File.WriteAllText(
                            workspace
                                .FindDeleteRecoveryMetadataFiles(".path")
                                .Single(),
                            "backend/another.g.cs",
                            new UTF8Encoding(false, true));
                        return Task.CompletedTask;
                    }));

        StringAssert.Contains(exception.Message, "生成清单已提交");
        Assert.AreEqual(
            "backend/another.g.cs",
            File.ReadAllText(
                workspace
                    .FindDeleteRecoveryMetadataFiles(".committed")
                    .Single(),
                new UTF8Encoding(false, true)));
    }

    [TestMethod]
    public async Task Apply_manifest_cleanup_failure_reports_committed_state()
    {
        foreach (var cleanupException in new Exception[]
                 {
                     new IOException("locked"),
                     new UnauthorizedAccessException("denied"),
                 })
        {
            using var workspace = TemporaryWorkspace.Create();
            var artifact = Artifact("backend/item.g.cs", "new");
            var previousManifest = GenerationManifest.Create(
                [new(artifact.RelativePath, Hash("old"))]);
            workspace.Write(artifact.RelativePath, "old");
            workspace.Write(
                GenerationWorkspaceStore.ManifestRelativePath,
                previousManifest.ToJson());
            var snapshot = await GenerationWorkspaceStore.CaptureAsync(
                workspace.RootPath,
                [artifact]);
            var plan = GenerationWritePlanner.Plan(
                [artifact],
                snapshot.ExistingFiles,
                snapshot.PreviousManifest);

            var exception =
                await Assert.ThrowsExactlyAsync<
                    GenerationWorkspaceConflictException>(() =>
                    GenerationWorkspaceStore.ApplyForTestingAsync(
                        workspace.RootPath,
                        plan,
                        () => Task.CompletedTask,
                        beforeManifestRecoveryCleanup: () =>
                            throw cleanupException));

            StringAssert.Contains(exception.Message, "已提交");
            Assert.AreEqual(
                plan.NextManifest!.ToJson(),
                workspace.Read(
                    GenerationWorkspaceStore.ManifestRelativePath));
            Assert.AreEqual(1, workspace.FindRecoveryFiles().Length);
        }
    }

    [TestMethod]
    public async Task Capture_rejects_manifest_owned_internal_path()
    {
        using var workspace = TemporaryWorkspace.Create();
        workspace.Write(".fullnet/owned.g.cs", "owned");
        workspace.Write(
            GenerationWorkspaceStore.ManifestRelativePath,
            GenerationManifest.Create(
                [new(".fullnet/owned.g.cs", Hash("owned"))]).ToJson());

        await Assert.ThrowsExactlyAsync<
            GenerationWorkspaceConflictException>(() =>
                GenerationWorkspaceStore.CaptureAsync(
                    workspace.RootPath,
                    []));

        Assert.AreEqual("owned", workspace.Read(".fullnet/owned.g.cs"));

        using var exactWorkspace = TemporaryWorkspace.Create();
        await Assert.ThrowsExactlyAsync<ArgumentException>(() =>
            GenerationWorkspaceStore.CaptureAsync(
                exactWorkspace.RootPath,
                [Artifact(".fullnet", "owned")]));
        exactWorkspace.Write(
            GenerationWorkspaceStore.ManifestRelativePath,
            GenerationManifest.Create(
                [new(".fullnet", Hash("owned"))]).ToJson());
        await Assert.ThrowsExactlyAsync<
            GenerationWorkspaceConflictException>(() =>
                GenerationWorkspaceStore.CaptureAsync(
                    exactWorkspace.RootPath,
                    []));
    }

    [TestMethod]
    public async Task Apply_cancellation_after_first_commit_finishes_manifest()
    {
        using var workspace = TemporaryWorkspace.Create();
        var first = Artifact("backend/first.g.cs", "first");
        var second = Artifact("backend/second.g.cs", "second");
        var snapshot = await GenerationWorkspaceStore.CaptureAsync(
            workspace.RootPath,
            [first, second]);
        var plan = GenerationWritePlanner.Plan(
            [first, second],
            snapshot.ExistingFiles,
            snapshot.PreviousManifest);
        using var cancellation = new CancellationTokenSource();

        await GenerationWorkspaceStore.ApplyForTestingAsync(
            workspace.RootPath,
            plan,
            () => Task.CompletedTask,
            cancellationToken: cancellation.Token,
            afterArtifactCommit: committedCount =>
            {
                if (committedCount == 1)
                {
                    cancellation.Cancel();
                }

                return Task.CompletedTask;
            });

        Assert.IsTrue(cancellation.IsCancellationRequested);
        Assert.AreEqual(first.Content, workspace.Read(first.RelativePath));
        Assert.AreEqual(second.Content, workspace.Read(second.RelativePath));
        Assert.AreEqual(
            plan.NextManifest!.ToJson(),
            workspace.Read(
                GenerationWorkspaceStore.ManifestRelativePath));
        Assert.AreEqual(0, workspace.FindTemporaryFiles().Length);
    }

    [TestMethod]
    public async Task Apply_rejects_conflict_or_cancellation_without_writes()
    {
        using var conflictWorkspace = TemporaryWorkspace.Create();
        var artifact = Artifact("backend/item.g.cs", "desired");
        conflictWorkspace.Write(artifact.RelativePath, "handwritten");
        var conflictPlan = GenerationWritePlanner.Plan(
            [artifact],
            new Dictionary<string, string>(StringComparer.Ordinal)
            {
                [artifact.RelativePath] = "handwritten",
            });

        await Assert.ThrowsExactlyAsync<InvalidOperationException>(() =>
            GenerationWorkspaceStore.ApplyAsync(
                conflictWorkspace.RootPath,
                conflictPlan));
        Assert.IsFalse(Directory.Exists(Path.Combine(
            conflictWorkspace.RootPath,
            ".fullnet")));
        Assert.AreEqual(
            "handwritten",
            conflictWorkspace.Read(artifact.RelativePath));

        using var canceledWorkspace = TemporaryWorkspace.Create();
        var canceledPlan = GenerationWritePlanner.Plan(
            [artifact],
            new Dictionary<string, string>(StringComparer.Ordinal));
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        await Assert.ThrowsExactlyAsync<OperationCanceledException>(() =>
            GenerationWorkspaceStore.ApplyAsync(
                canceledWorkspace.RootPath,
                canceledPlan,
                cancellation.Token));
        Assert.IsFalse(Directory.Exists(Path.Combine(
            canceledWorkspace.RootPath,
            ".fullnet")));
        Assert.IsFalse(File.Exists(canceledWorkspace.PathOf(
            artifact.RelativePath)));

        using var lockedWorkspace = TemporaryWorkspace.Create();
        var lockPath = lockedWorkspace.PathOf(
            ".fullnet/codegeneration.lock");
        Directory.CreateDirectory(Path.GetDirectoryName(lockPath)!);
        await using var heldLock = new FileStream(
            lockPath,
            FileMode.OpenOrCreate,
            FileAccess.ReadWrite,
            FileShare.None);
        await Assert.ThrowsExactlyAsync<GenerationWorkspaceConflictException>(
            () => GenerationWorkspaceStore.ApplyAsync(
                lockedWorkspace.RootPath,
                canceledPlan));
        Assert.IsFalse(File.Exists(lockedWorkspace.PathOf(
            artifact.RelativePath)));
    }

    [TestMethod]
    public async Task Apply_stale_target_fails_preflight_without_partial_writes()
    {
        using var workspace = TemporaryWorkspace.Create();
        var first = Artifact("backend/first.g.cs", "first-new");
        var second = Artifact("backend/second.g.cs", "second-new");
        workspace.Write(first.RelativePath, "first-old");
        workspace.Write(second.RelativePath, "second-old");
        workspace.Write(
            GenerationWorkspaceStore.ManifestRelativePath,
            GenerationManifest.Create(
            [
                new(first.RelativePath, Hash("first-old")),
                new(second.RelativePath, Hash("second-old")),
            ]).ToJson());
        var snapshot = await GenerationWorkspaceStore.CaptureAsync(
            workspace.RootPath,
            [first, second]);
        var plan = GenerationWritePlanner.Plan(
            [first, second],
            snapshot.ExistingFiles,
            snapshot.PreviousManifest);
        workspace.Write(second.RelativePath, "concurrent-user-edit");

        await Assert.ThrowsExactlyAsync<GenerationWorkspaceConflictException>(
            () => GenerationWorkspaceStore.ApplyAsync(
                workspace.RootPath,
                plan));

        Assert.AreEqual("first-old", workspace.Read(first.RelativePath));
        Assert.AreEqual(
            "concurrent-user-edit",
            workspace.Read(second.RelativePath));
        Assert.AreEqual(
            snapshot.PreviousManifest!.ToJson(),
            workspace.Read(GenerationWorkspaceStore.ManifestRelativePath));
        Assert.AreEqual(0, workspace.FindTemporaryFiles().Length);

        using var createWorkspace = TemporaryWorkspace.Create();
        var createFirst = Artifact("backend/create-first.g.cs", "first");
        var createSecond = Artifact("backend/create-second.g.cs", "second");
        var createSnapshot = await GenerationWorkspaceStore.CaptureAsync(
            createWorkspace.RootPath,
            [createFirst, createSecond]);
        var createPlan = GenerationWritePlanner.Plan(
            [createFirst, createSecond],
            createSnapshot.ExistingFiles,
            createSnapshot.PreviousManifest);
        createWorkspace.Write(createSecond.RelativePath, "concurrent-create");

        await Assert.ThrowsExactlyAsync<GenerationWorkspaceConflictException>(
            () => GenerationWorkspaceStore.ApplyAsync(
                createWorkspace.RootPath,
                createPlan));
        Assert.IsFalse(File.Exists(createWorkspace.PathOf(
            createFirst.RelativePath)));
        Assert.AreEqual(
            "concurrent-create",
            createWorkspace.Read(createSecond.RelativePath));
        Assert.AreEqual(0, createWorkspace.FindTemporaryFiles().Length);
    }

    [TestMethod]
    public async Task Apply_changed_manifest_rejects_stale_plan()
    {
        using var workspace = TemporaryWorkspace.Create();
        var artifact = Artifact("backend/item.g.cs", "same");
        var previousManifest = GenerationManifest.Create(
            [new(artifact.RelativePath, Hash(artifact.Content))]);
        workspace.Write(artifact.RelativePath, artifact.Content);
        workspace.Write(
            GenerationWorkspaceStore.ManifestRelativePath,
            previousManifest.ToJson());
        var snapshot = await GenerationWorkspaceStore.CaptureAsync(
            workspace.RootPath,
            [artifact]);
        var plan = GenerationWritePlanner.Plan(
            [artifact],
            snapshot.ExistingFiles,
            snapshot.PreviousManifest);
        var concurrentManifest = GenerationManifest.Create(
            [new("backend/other.g.cs", Hash("other"))]);
        workspace.Write(
            GenerationWorkspaceStore.ManifestRelativePath,
            concurrentManifest.ToJson());

        await Assert.ThrowsExactlyAsync<GenerationWorkspaceConflictException>(
            () => GenerationWorkspaceStore.ApplyAsync(
                workspace.RootPath,
                plan));

        Assert.AreEqual(
            concurrentManifest.ToJson(),
            workspace.Read(GenerationWorkspaceStore.ManifestRelativePath));
        Assert.AreEqual(artifact.Content, workspace.Read(artifact.RelativePath));

        using var commitRaceWorkspace = TemporaryWorkspace.Create();
        commitRaceWorkspace.Write(artifact.RelativePath, artifact.Content);
        commitRaceWorkspace.Write(
            GenerationWorkspaceStore.ManifestRelativePath,
            previousManifest.ToJson());
        var commitRaceSnapshot =
            await GenerationWorkspaceStore.CaptureAsync(
                commitRaceWorkspace.RootPath,
                [artifact]);
        var commitRacePlan = GenerationWritePlanner.Plan(
            [artifact],
            commitRaceSnapshot.ExistingFiles,
            commitRaceSnapshot.PreviousManifest);

        await Assert.ThrowsExactlyAsync<GenerationWorkspaceConflictException>(
            () => GenerationWorkspaceStore.ApplyForTestingAsync(
                commitRaceWorkspace.RootPath,
                commitRacePlan,
                () =>
                {
                    commitRaceWorkspace.Write(
                        GenerationWorkspaceStore.ManifestRelativePath,
                        concurrentManifest.ToJson());
                    return Task.CompletedTask;
                }));

        Assert.AreEqual(
            concurrentManifest.ToJson(),
            commitRaceWorkspace.Read(
                GenerationWorkspaceStore.ManifestRelativePath));
        Assert.AreEqual(0, commitRaceWorkspace.FindTemporaryFiles().Length);

        using var secondSaveWorkspace = TemporaryWorkspace.Create();
        secondSaveWorkspace.Write(artifact.RelativePath, artifact.Content);
        secondSaveWorkspace.Write(
            GenerationWorkspaceStore.ManifestRelativePath,
            previousManifest.ToJson());
        var secondSaveSnapshot =
            await GenerationWorkspaceStore.CaptureAsync(
                secondSaveWorkspace.RootPath,
                [artifact]);
        var secondSavePlan = GenerationWritePlanner.Plan(
            [artifact],
            secondSaveSnapshot.ExistingFiles,
            secondSaveSnapshot.PreviousManifest);
        var secondConcurrentManifest = GenerationManifest.Create(
            [new("backend/latest.g.cs", Hash("latest"))]);

        var secondSaveException =
            await Assert.ThrowsExactlyAsync<
                GenerationWorkspaceConflictException>(
                () => GenerationWorkspaceStore.ApplyForTestingAsync(
                secondSaveWorkspace.RootPath,
                secondSavePlan,
                () =>
                {
                    secondSaveWorkspace.Write(
                        GenerationWorkspaceStore.ManifestRelativePath,
                        concurrentManifest.ToJson());
                    return Task.CompletedTask;
                },
                () =>
                {
                    secondSaveWorkspace.Write(
                        GenerationWorkspaceStore.ManifestRelativePath,
                        secondConcurrentManifest.ToJson());
                    return Task.CompletedTask;
                }));

        Assert.AreEqual(
            secondConcurrentManifest.ToJson(),
            secondSaveWorkspace.Read(
                GenerationWorkspaceStore.ManifestRelativePath));
        var recoveryFiles = secondSaveWorkspace.FindRecoveryFiles();
        Assert.AreEqual(1, recoveryFiles.Length);
        Assert.AreEqual(
            concurrentManifest.ToJson(),
            File.ReadAllText(
                recoveryFiles[0],
                new UTF8Encoding(false, true)));
        StringAssert.Contains(
            secondSaveException.Message,
            ".fullnet/codegeneration-manifest-");
        Assert.IsFalse(
            secondSaveException.Message.Contains(
                secondSaveWorkspace.RootPath,
                StringComparison.OrdinalIgnoreCase));

        using var postClaimCancellationWorkspace =
            TemporaryWorkspace.Create();
        postClaimCancellationWorkspace.Write(
            artifact.RelativePath,
            artifact.Content);
        postClaimCancellationWorkspace.Write(
            GenerationWorkspaceStore.ManifestRelativePath,
            previousManifest.ToJson());
        var postClaimSnapshot =
            await GenerationWorkspaceStore.CaptureAsync(
                postClaimCancellationWorkspace.RootPath,
                [artifact]);
        var postClaimPlan = GenerationWritePlanner.Plan(
            [artifact],
            postClaimSnapshot.ExistingFiles,
            postClaimSnapshot.PreviousManifest);
        using var postClaimCancellation = new CancellationTokenSource();

        await GenerationWorkspaceStore.ApplyForTestingAsync(
            postClaimCancellationWorkspace.RootPath,
            postClaimPlan,
            () => Task.CompletedTask,
            () =>
            {
                postClaimCancellation.Cancel();
                return Task.CompletedTask;
            },
            postClaimCancellation.Token);

        Assert.AreEqual(
            postClaimPlan.NextManifest!.ToJson(),
            postClaimCancellationWorkspace.Read(
                GenerationWorkspaceStore.ManifestRelativePath));
        Assert.AreEqual(
            0,
            postClaimCancellationWorkspace.FindRecoveryFiles().Length);
    }

    private static GeneratedArtifact Artifact(
        string relativePath,
        string content)
    {
        return new GeneratedArtifact(
            relativePath,
            GeneratedArtifactKind.Backend,
            content);
    }

    private static string Hash(string content)
    {
        return Convert.ToHexString(
                SHA256.HashData(Encoding.UTF8.GetBytes(content)))
            .ToLowerInvariant();
    }

    private static FileStream OpenRetainedWriteHandle(string path)
    {
        return new FileStream(
            path,
            new FileStreamOptions
            {
                Mode = FileMode.Open,
                Access = FileAccess.ReadWrite,
                Share = FileShare.ReadWrite | FileShare.Delete,
            });
    }

    private static void WriteRetainedHandle(
        FileStream stream,
        string content)
    {
        var bytes = new UTF8Encoding(false, true).GetBytes(content);
        stream.Position = 0;
        stream.Write(bytes);
        stream.SetLength(bytes.Length);
        stream.Flush(flushToDisk: true);
    }

    private sealed class TemporaryWorkspace : IDisposable
    {
        private TemporaryWorkspace(string rootPath)
        {
            RootPath = rootPath;
        }

        public string RootPath { get; }

        public static TemporaryWorkspace Create()
        {
            var rootPath = Path.Combine(
                Path.GetTempPath(),
                $"fullnet-codegen-{Guid.NewGuid():N}");
            Directory.CreateDirectory(rootPath);
            return new TemporaryWorkspace(rootPath);
        }

        public string PathOf(string relativePath)
        {
            return Path.Combine(
                RootPath,
                relativePath.Replace('/', Path.DirectorySeparatorChar));
        }

        public void Write(string relativePath, string content)
        {
            var path = PathOf(relativePath);
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, content, new UTF8Encoding(false, true));
        }

        public void WriteBytes(string relativePath, byte[] content)
        {
            var path = PathOf(relativePath);
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllBytes(path, content);
        }

        public string Read(string relativePath)
        {
            return File.ReadAllText(
                PathOf(relativePath),
                new UTF8Encoding(false, true));
        }

        public string[] FindTemporaryFiles()
        {
            return Directory.GetFiles(
                RootPath,
                "*.tmp",
                SearchOption.AllDirectories);
        }

        public string[] FindRecoveryFiles()
        {
            return Directory.GetFiles(
                RootPath,
                "*.recovery",
                SearchOption.AllDirectories);
        }

        public string[] FindDeleteRecoveryFiles()
        {
            return Directory.GetFiles(
                PathOf(".fullnet/codegeneration-delete-recovery"),
                "*.recovery",
                SearchOption.TopDirectoryOnly);
        }

        public string[] FindDeleteRecoveryMetadataFiles(string suffix)
        {
            return Directory.GetFiles(
                PathOf(".fullnet/codegeneration-delete-recovery"),
                $"*{suffix}",
                SearchOption.TopDirectoryOnly);
        }

        public void Dispose()
        {
            if (Directory.Exists(RootPath))
            {
                Directory.Delete(RootPath, recursive: true);
            }
        }
    }
}
