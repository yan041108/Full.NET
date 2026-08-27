using Full.NET.Benchmarks.Outbox;

namespace Full.NET.UnitTests.Performance;

/// <summary>
/// 验证 Outbox 写入 Profile 只在显式请求时加入 Typed Plan 候选路径。
/// </summary>
[TestClass]
public sealed class OutboxWriteProfileContractTests
{
    [TestMethod]
    public void Defaults_to_registry_path_only()
    {
        var options = OutboxWriteProfileOptions.Parse([]);

        CollectionAssert.AreEqual(
            new[] { OutboxWriteProfileCommandPath.Registry },
            options.CommandPaths.ToArray());
    }

    [TestMethod]
    public void Parses_ordered_registry_and_typed_paths()
    {
        var options = OutboxWriteProfileOptions.Parse(
            ["--command-paths", "registry,typed"]);

        CollectionAssert.AreEqual(
            new[]
            {
                OutboxWriteProfileCommandPath.Registry,
                OutboxWriteProfileCommandPath.Typed,
            },
            options.CommandPaths.ToArray());
    }

    [TestMethod]
    public void Rejects_unknown_or_duplicate_command_paths()
    {
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() =>
            OutboxWriteProfileOptions.Parse(
                ["--command-paths", "registry,dynamic"]));
        Assert.ThrowsExactly<ArgumentException>(() =>
            OutboxWriteProfileOptions.Parse(
                ["--command-paths", "typed,typed"]));
    }

    [TestMethod]
    public void Scenario_matrix_reverses_path_order_on_even_repetitions()
    {
        var options = OutboxWriteProfileOptions.Parse(
            [
                "--targets", "legacy",
                "--concurrency", "1",
                "--repetitions", "2",
                "--command-paths", "registry,typed",
            ]);

        var order = OutboxWriteProfileScenarioMatrix
            .Create(options)
            .Select(scenario =>
                $"{scenario.Repetition}:{scenario.CommandPath}")
            .ToArray();

        CollectionAssert.AreEqual(
            new[]
            {
                "1:Registry",
                "1:Typed",
                "2:Typed",
                "2:Registry",
            },
            order);
    }

    [TestMethod]
    public void Command_paths_expose_stable_lowercase_tokens()
    {
        Assert.AreEqual(
            "registry",
            OutboxWriteProfileCommandPath.Registry.ToToken());
        Assert.AreEqual(
            "typed",
            OutboxWriteProfileCommandPath.Typed.ToToken());
    }
}
