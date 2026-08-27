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
}
