using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Full.NET.Data.CodeGeneration.Generation;

namespace Full.NET.UnitTests.CodeGeneration;

[TestClass]
public sealed class GenerationWritePlannerTests
{
    [TestMethod]
    public void Manifest_round_trips_deterministically_across_culture_and_order()
    {
        var originalCulture = CultureInfo.CurrentCulture;
        var entries = new[]
        {
            new GenerationManifestEntry("z/item.generated.ts", Hash("z")),
            new GenerationManifestEntry("a/item.g.cs", Hash("a")),
        };

        try
        {
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("tr-TR");
            var first = GenerationManifest.Create(entries).ToJson();

            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("en-US");
            var second = GenerationManifest.Create(entries.Reverse()).ToJson();
            var roundTrip = GenerationManifest.Parse(first).ToJson();

            Assert.AreEqual(first, second);
            Assert.AreEqual(first, roundTrip);
            StringAssert.Contains(first, "\"relativePath\"");
            StringAssert.Contains(first, "\"sha256\"");
            Assert.IsFalse(first.Contains("\"RelativePath\"", StringComparison.Ordinal));
            CollectionAssert.AreEqual(
                new[] { "a/item.g.cs", "z/item.generated.ts" },
                GenerationManifest.Parse(first).Artifacts
                    .Select(artifact => artifact.RelativePath)
                    .ToArray());
        }
        finally
        {
            CultureInfo.CurrentCulture = originalCulture;
        }
    }

    [TestMethod]
    public void Manifest_rejects_unsafe_duplicate_or_invalid_entries()
    {
        var invalidPaths = new[]
        {
            string.Empty,
            "/root/item.g.cs",
            "C:/root/item.g.cs",
            @"backend\item.g.cs",
            "backend//item.g.cs",
            "./backend/item.g.cs",
            "backend/../item.g.cs",
        };

        foreach (var path in invalidPaths)
        {
            Assert.ThrowsExactly<ArgumentException>(() =>
                GenerationManifest.Create([new(path, Hash("content"))]));
        }

        Assert.ThrowsExactly<ArgumentException>(() => GenerationManifest.Create(
        [
            new("backend/item.g.cs", Hash("first")),
            new("backend/item.g.cs", Hash("second")),
        ]));
        Assert.ThrowsExactly<ArgumentException>(() => GenerationManifest.Create(
        [
            new("backend/item.g.cs", Hash("first")),
            new("backend/ITEM.g.cs", Hash("second")),
        ]));
        Assert.ThrowsExactly<ArgumentException>(() =>
            GenerationManifest.Create([new("backend/item.g.cs", "not-a-sha256")]));
        Assert.ThrowsExactly<ArgumentException>(() => GenerationManifest.Parse(
            """{"schemaVersion":2,"artifacts":[]}"""));
    }

    [TestMethod]
    public void Plan_rejects_non_portable_paths_and_file_system_aliases()
    {
        var invalidPaths = new[]
        {
            "backend/item.g.cs.",
            "backend/item.g.cs ",
            "backend/CON.g.cs",
            "backend/CONIN$.g.cs",
            "backend/CONOUT$.g.cs",
            "backend/COM¹.g.cs",
            "backend/COM².g.cs",
            "backend/COM³.g.cs",
            "backend/LPT¹.g.cs",
            "backend/LPT².g.cs",
            "backend/LPT³.g.cs",
            "backend/item?.g.cs",
            "backend/cafe\u0301.g.cs",
        };

        foreach (var path in invalidPaths)
        {
            Assert.ThrowsExactly<ArgumentException>(() =>
                GenerationWritePlanner.Plan(
                    [Artifact(path, "content")],
                    new Dictionary<string, string>(StringComparer.Ordinal)));
        }

        Assert.ThrowsExactly<ArgumentException>(() =>
            GenerationWritePlanner.Plan(
                [
                    Artifact("backend/item.g.cs", "first"),
                    Artifact("backend/ITEM.g.cs", "second"),
                ],
                new Dictionary<string, string>(StringComparer.Ordinal)));
        Assert.ThrowsExactly<ArgumentException>(() =>
            GenerationWritePlanner.Plan(
                [Artifact("backend/item.g.cs", "desired")],
                new Dictionary<string, string>(StringComparer.Ordinal)
                {
                    ["backend/item.g.cs"] = "first",
                    ["backend/ITEM.g.cs"] = "second",
                }));

        var aliasPlan = GenerationWritePlanner.Plan(
            [Artifact("backend/item.g.cs", "desired")],
            new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["backend/ITEM.g.cs"] = "desired",
            });
        Assert.AreEqual(
            GenerationWriteActionKind.Conflict,
            aliasPlan.Actions.Single().Kind);
        Assert.IsFalse(aliasPlan.CanApply);
    }

    [TestMethod]
    public void Plan_rejects_invalid_utf16_instead_of_hashing_replacement_bytes()
    {
        Assert.ThrowsExactly<EncoderFallbackException>(() =>
            GenerationWritePlanner.Plan(
                [Artifact("backend/item.g.cs", "\uD800")],
                new Dictionary<string, string>(StringComparer.Ordinal)));
    }

    [TestMethod]
    public void Plan_missing_files_creates_sorted_actions_and_manifest()
    {
        var artifacts = new[]
        {
            Artifact("z/item.generated.ts", "z"),
            Artifact("a/item.g.cs", "a"),
        };

        var plan = GenerationWritePlanner.Plan(
            artifacts,
            new Dictionary<string, string>(StringComparer.Ordinal));

        Assert.IsTrue(plan.CanApply);
        Assert.IsNotNull(plan.NextManifest);
        CollectionAssert.AreEqual(
            new[] { "a/item.g.cs", "z/item.generated.ts" },
            plan.Actions.Select(action => action.RelativePath).ToArray());
        Assert.IsTrue(plan.Actions.All(action =>
            action.Kind == GenerationWriteActionKind.Create));
        CollectionAssert.AreEqual(
            new[] { "a/item.g.cs", "z/item.generated.ts" },
            plan.NextManifest.Artifacts
                .Select(artifact => artifact.RelativePath)
                .ToArray());
    }

    [TestMethod]
    public void Plan_identical_unowned_file_is_unchanged_and_adopted()
    {
        var artifact = Artifact("backend/item.g.cs", "same");

        var plan = GenerationWritePlanner.Plan(
            [artifact],
            new Dictionary<string, string>(StringComparer.Ordinal)
            {
                [artifact.RelativePath] = artifact.Content,
            });

        Assert.IsTrue(plan.CanApply);
        Assert.AreEqual(
            GenerationWriteActionKind.Unchanged,
            plan.Actions.Single().Kind);
        Assert.AreEqual(Hash("same"), plan.Actions.Single().ExistingSha256);
        Assert.IsNotNull(plan.NextManifest);
        Assert.IsTrue(plan.NextManifest.TryGetSha256(
            artifact.RelativePath,
            out var sha256));
        Assert.AreEqual(Hash("same"), sha256);
    }

    [TestMethod]
    public void Plan_manifest_owned_unmodified_file_allows_update()
    {
        var path = "backend/item.g.cs";
        var manifest = GenerationManifest.Create(
            [new GenerationManifestEntry(path, Hash("old"))]);

        var plan = GenerationWritePlanner.Plan(
            [Artifact(path, "new")],
            new Dictionary<string, string>(StringComparer.Ordinal)
            {
                [path] = "old",
            },
            manifest);

        var action = plan.Actions.Single();
        Assert.IsTrue(plan.CanApply);
        Assert.AreEqual(GenerationWriteActionKind.Update, action.Kind);
        Assert.AreEqual("new", action.Content);
        Assert.AreEqual(Hash("old"), action.ExistingSha256);
        Assert.AreEqual(Hash("new"), action.DesiredSha256);
    }

    [TestMethod]
    public void Plan_modified_or_unowned_existing_file_reports_conflict()
    {
        var ownedPath = "backend/owned.g.cs";
        var unownedPath = "backend/unowned.g.cs";
        var manifest = GenerationManifest.Create(
            [new GenerationManifestEntry(ownedPath, Hash("original"))]);

        var plan = GenerationWritePlanner.Plan(
            [
                Artifact(ownedPath, "desired-owned"),
                Artifact(unownedPath, "desired-unowned"),
            ],
            new Dictionary<string, string>(StringComparer.Ordinal)
            {
                [ownedPath] = "user-edit",
                [unownedPath] = "handwritten",
            },
            manifest);

        Assert.IsFalse(plan.CanApply);
        Assert.IsNull(plan.NextManifest);
        Assert.IsTrue(plan.Actions.All(action =>
            action.Kind == GenerationWriteActionKind.Conflict));
    }

    [TestMethod]
    public void Plan_any_conflict_blocks_manifest_for_other_safe_actions()
    {
        var plan = GenerationWritePlanner.Plan(
            [
                Artifact("backend/create.g.cs", "create"),
                Artifact("backend/conflict.g.cs", "desired"),
            ],
            new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["backend/conflict.g.cs"] = "handwritten",
            });

        CollectionAssert.AreEqual(
            new[]
            {
                GenerationWriteActionKind.Conflict,
                GenerationWriteActionKind.Create,
            },
            plan.Actions.Select(action => action.Kind).ToArray());
        Assert.IsFalse(plan.CanApply);
        Assert.IsNull(plan.NextManifest);
    }

    [TestMethod]
    public void Plan_unmodified_stale_manifest_entry_creates_delete_action()
    {
        var manifest = GenerationManifest.Create(
        [
            new("backend/current.g.cs", Hash("current")),
            new("backend/stale.g.cs", Hash("stale")),
        ]);

        var plan = GenerationWritePlanner.Plan(
            [Artifact("backend/current.g.cs", "current")],
            new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["backend/current.g.cs"] = "current",
                ["backend/stale.g.cs"] = "stale",
            },
            manifest);

        Assert.IsTrue(plan.CanApply);
        Assert.AreEqual(2, plan.Actions.Count);
        var delete = plan.Actions.Single(action =>
            action.RelativePath == "backend/stale.g.cs");
        Assert.AreEqual(GenerationWriteActionKind.Delete, delete.Kind);
        Assert.IsNull(delete.Content);
        Assert.AreEqual(Hash("stale"), delete.ExistingSha256);
        Assert.IsNull(delete.DesiredSha256);
        Assert.IsNotNull(plan.NextManifest);
        Assert.IsFalse(plan.NextManifest.TryGetSha256(
            "backend/stale.g.cs",
            out _));
    }

    [TestMethod]
    public void Plan_modified_stale_entry_conflicts_but_missing_entry_is_forgotten()
    {
        var manifest = GenerationManifest.Create(
        [
            new("backend/missing.g.cs", Hash("missing")),
            new("backend/modified.g.cs", Hash("original")),
        ]);

        var plan = GenerationWritePlanner.Plan(
            [],
            new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["backend/modified.g.cs"] = "user-edit",
            },
            manifest);

        var conflict = plan.Actions.Single();
        Assert.AreEqual("backend/modified.g.cs", conflict.RelativePath);
        Assert.AreEqual(
            GenerationWriteActionKind.Conflict,
            conflict.Kind);
        Assert.AreEqual(Hash("user-edit"), conflict.ExistingSha256);
        Assert.IsNull(conflict.Content);
        Assert.IsNull(conflict.DesiredSha256);
        Assert.IsFalse(plan.CanApply);
        Assert.IsNull(plan.NextManifest);
    }

    [TestMethod]
    public void Plan_stale_manifest_path_alias_conflicts_and_actions_stay_sorted()
    {
        var manifest = GenerationManifest.Create(
        [
            new("backend/ITEM.g.cs", Hash("same")),
            new("z/stale.g.cs", Hash("stale")),
        ]);

        var plan = GenerationWritePlanner.Plan(
            [Artifact("backend/item.g.cs", "same")],
            new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["backend/item.g.cs"] = "same",
                ["z/stale.g.cs"] = "stale",
            },
            manifest);

        CollectionAssert.AreEqual(
            new[]
            {
                "backend/ITEM.g.cs",
                "backend/item.g.cs",
                "z/stale.g.cs",
            },
            plan.Actions.Select(action => action.RelativePath).ToArray());
        Assert.AreEqual(
            GenerationWriteActionKind.Conflict,
            plan.Actions[0].Kind);
        Assert.AreEqual(
            GenerationWriteActionKind.Unchanged,
            plan.Actions[1].Kind);
        Assert.AreEqual(
            GenerationWriteActionKind.Delete,
            plan.Actions[2].Kind);
        Assert.IsFalse(plan.CanApply);
    }

    private static GeneratedArtifact Artifact(string relativePath, string content)
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
}
