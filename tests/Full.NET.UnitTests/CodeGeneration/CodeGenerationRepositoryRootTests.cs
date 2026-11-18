using Full.NET.Tests.CodeGeneration;

namespace Full.NET.UnitTests.CodeGeneration;

/// <summary>确定性编译中的虚拟源码路径不能阻断实际检出目录的发现。</summary>
[TestClass]
public sealed class CodeGenerationRepositoryRootTests
{
    [TestMethod]
    public void Mapped_source_and_relocated_output_can_use_the_runtime_checkout()
    {
        using var fixture = new RepositoryFixture();
        var checkout = fixture.Repository("checkout");
        var nested = Path.Combine(checkout, "tests", "output");
        Directory.CreateDirectory(nested);
        Assert.AreEqual(checkout, CodeGenerationRemediationSchemas.LocateRepositoryRoot(null,
            [Path.Combine(fixture.Root, "_", "tests"), Path.Combine(fixture.Root, "relocated", "bin"), nested]));
    }

    [TestMethod]
    public void Runtime_checkout_precedes_a_stale_compilation_checkout()
    {
        using var fixture = new RepositoryFixture();
        var current = fixture.Repository("current");
        var original = fixture.Repository("original");
        Assert.AreEqual(current, CodeGenerationRemediationSchemas.LocateRepositoryRoot(null, [current, original]));
    }

    [TestMethod]
    public void Explicit_checkout_has_priority_and_invalid_binding_cannot_fall_back()
    {
        using var fixture = new RepositoryFixture();
        var bound = fixture.Repository("bound");
        var other = fixture.Repository("other");
        Assert.AreEqual(bound, CodeGenerationRemediationSchemas.LocateRepositoryRoot(bound, [other]));
        Assert.ThrowsExactly<DirectoryNotFoundException>(() =>
            CodeGenerationRemediationSchemas.LocateRepositoryRoot(Path.Combine(fixture.Root, "missing"), [other]));
    }

    [TestMethod]
    public void Missing_repository_is_rejected_and_the_real_entry_uses_runtime_context()
    {
        using var fixture = new RepositoryFixture();
        Assert.ThrowsExactly<DirectoryNotFoundException>(() =>
            CodeGenerationRemediationSchemas.LocateRepositoryRoot(null, [null, "", fixture.Root]));
        // 故意传入不存在的编译路径，正式入口仍需发现当前实际仓库。
        Assert.IsTrue(File.Exists(Path.Combine(CodeGenerationRemediationSchemas.RepositoryRoot(
            Path.Combine(fixture.Root, "_", "tests", "mapped.cs")), "Full.NET.slnx")));
    }

    private sealed class RepositoryFixture : IDisposable
    {
        internal string Root { get; } = Directory.CreateTempSubdirectory("fullnet-codegen-root-").FullName;

        internal string Repository(string name)
        {
            var directory = Path.Combine(Root, name);
            Directory.CreateDirectory(directory);
            File.WriteAllText(Path.Combine(directory, "Full.NET.slnx"), "<Solution />");
            return directory;
        }

        public void Dispose() => Directory.Delete(Root, recursive: true);
    }
}
