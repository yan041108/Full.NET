using Full.NET.Data.CodeGeneration.Integration;

namespace Full.NET.UnitTests.CodeGeneration;

[TestClass]
public sealed class AuthorizationContributorIntegrationEditorTests
{
    internal const string Source = """
        namespace Acme.Catalog;
        public sealed class CatalogAuthorizationContributor
        {
            public IReadOnlyCollection<PermissionDefinition> Permissions { get; } =
            [
                new PermissionDefinition("manual")
            ];
            public IReadOnlyCollection<NavigationDefinition> Navigation { get; } = [];
            public IReadOnlyCollection<AuthorizationActionDefinition> Actions { get; } = [];
        }
        """;

    internal const string Fragment = """
        // <fullnet-generated catalog.product permissions>
        new PermissionDefinition("generated"),
        // </fullnet-generated catalog.product permissions>

        // <fullnet-generated catalog.product navigation>
        new NavigationDefinition("generated"),
        // </fullnet-generated catalog.product navigation>

        // <fullnet-generated catalog.product actions>
        new AuthorizationActionDefinition("generated"),
        // </fullnet-generated catalog.product actions>
        """;

    [TestMethod]
    public void Generated_elements_are_inserted_inside_each_collection_and_manual_elements_are_preserved()
    {
        var result = Edit(Source);
        Assert.IsTrue(result.Succeeded);
        Assert.IsTrue(result.Changed);
        var desired = result.DesiredContent;
        Assert.IsTrue(desired.IndexOf("catalog.product permissions>", StringComparison.Ordinal) < desired.IndexOf("];", StringComparison.Ordinal));
        Assert.IsTrue(desired.IndexOf("catalog.product navigation>", StringComparison.Ordinal) < desired.IndexOf("IReadOnlyCollection<AuthorizationActionDefinition>", StringComparison.Ordinal));
        Assert.IsTrue(desired.IndexOf("catalog.product actions>", StringComparison.Ordinal) < desired.LastIndexOf('}'));
        StringAssert.Contains(desired, "new PermissionDefinition(\"manual\"),");
    }

    [TestMethod]
    public void Complete_unchanged_blocks_are_idempotent()
    {
        var first = Edit(Source);
        var second = Edit(first.DesiredContent);
        Assert.IsTrue(second.Succeeded);
        Assert.IsFalse(second.Changed);
        Assert.AreEqual(first.DesiredContent, second.DesiredContent);
    }

    [TestMethod]
    public void Second_entity_preserves_first_entity_and_both_remain_idempotent()
    {
        var first = Edit(Source);
        var secondFragment = Fragment.Replace("catalog.product", "catalog.category", StringComparison.Ordinal)
            .Replace("\"generated\"", "\"category\"", StringComparison.Ordinal);
        var second = AuthorizationContributorIntegrationEditor.Edit(
            first.DesiredContent, "CatalogAuthorizationContributor.cs", secondFragment);
        Assert.IsTrue(second.Succeeded);
        Assert.IsTrue(second.Changed);
        StringAssert.Contains(second.DesiredContent, "new PermissionDefinition(\"generated\")");
        StringAssert.Contains(second.DesiredContent, "new PermissionDefinition(\"category\")");
        Assert.IsFalse(Edit(second.DesiredContent).Changed);
        var repeat = AuthorizationContributorIntegrationEditor.Edit(
            second.DesiredContent, "CatalogAuthorizationContributor.cs", secondFragment);
        Assert.IsTrue(repeat.Succeeded);
        Assert.IsFalse(repeat.Changed);
    }

    [TestMethod]
    public void Partial_generated_blocks_are_rejected_without_changing_source()
    {
        var source = Source.Replace("new PermissionDefinition(\"manual\")", Fragment[..Fragment.IndexOf("\n\n", StringComparison.Ordinal)], StringComparison.Ordinal);
        AssertRejected(source);
    }

    [TestMethod]
    public void Manually_changed_generated_block_is_rejected_without_changing_source()
    {
        var source = Edit(Source).DesiredContent.Replace("new PermissionDefinition(\"generated\")", "new PermissionDefinition(\"customized\")", StringComparison.Ordinal);
        AssertRejected(source);
    }

    [TestMethod]
    [DataRow("public IReadOnlyCollection<NavigationDefinition> Navigation { get; } = [];", "// IReadOnlyCollection<NavigationDefinition> Navigation")]
    [DataRow("public IReadOnlyCollection<NavigationDefinition> Navigation { get; } = [];", "public IReadOnlyCollection<NavigationDefinition> Navigation => CreateNavigation();")]
    public void Missing_or_nonstandard_collection_is_rejected(string original, string replacement)
    {
        AssertRejected(Source.Replace(original, replacement, StringComparison.Ordinal));
    }

    [TestMethod]
    public void Duplicate_collection_is_rejected()
    {
        AssertRejected(Source.Replace("public IReadOnlyCollection<NavigationDefinition> Navigation { get; } = [];", "public IReadOnlyCollection<NavigationDefinition> Navigation { get; } = []; public IReadOnlyCollection<NavigationDefinition> Navigation { get; } = [];", StringComparison.Ordinal));
    }

    [TestMethod]
    public void Marker_comment_outside_collection_does_not_suppress_integration()
    {
        AssertRejected(Source + "\n// <fullnet-generated catalog.product permissions>\n");
    }

    [TestMethod]
    public void Duplicate_nonstandard_collection_is_rejected()
    {
        AssertRejected(Source.Replace("public IReadOnlyCollection<NavigationDefinition> Navigation { get; } = [];", "public IReadOnlyCollection<NavigationDefinition> Navigation { get; } = []; public IReadOnlyCollection<NavigationDefinition> Navigation => CreateNavigation();", StringComparison.Ordinal));
    }

    [TestMethod]
    public void Markers_inside_raw_string_do_not_suppress_insertion()
    {
        var source = Source.Replace("public IReadOnlyCollection<PermissionDefinition>", "private const string Hint = \"\"\"\n// <fullnet-generated catalog.product permissions>\n\"\"\";\npublic IReadOnlyCollection<PermissionDefinition>", StringComparison.Ordinal);
        var result = Edit(source);
        Assert.IsTrue(result.Succeeded);
        Assert.IsTrue(result.Changed);
        StringAssert.Contains(result.DesiredContent, "new PermissionDefinition(\"generated\")");
    }

    [TestMethod]
    public void CrLf_source_preserves_newlines_and_remains_idempotent()
    {
        var source = Source.Replace("\r\n", "\n", StringComparison.Ordinal).Replace("\n", "\r\n", StringComparison.Ordinal);
        var first = Edit(source);
        Assert.IsTrue(first.Succeeded);
        Assert.IsFalse(first.DesiredContent.Replace("\r\n", string.Empty, StringComparison.Ordinal).Contains('\n'));
        Assert.IsFalse(Edit(first.DesiredContent).Changed);
    }

    [TestMethod]
    public void Preprocessor_conditioned_collections_are_rejected()
    {
        AssertRejected(Source.Replace("public IReadOnlyCollection<PermissionDefinition>", "#if TEST\n#endif\npublic IReadOnlyCollection<PermissionDefinition>", StringComparison.Ordinal));
    }

    [TestMethod]
    public void Generated_block_moved_into_nested_expression_is_rejected()
    {
        var source = Edit(Source).DesiredContent
            .Replace("// <fullnet-generated catalog.product permissions>", "..Wrap([\n// <fullnet-generated catalog.product permissions>", StringComparison.Ordinal)
            .Replace("// </fullnet-generated catalog.product permissions>", "// </fullnet-generated catalog.product permissions>\n]),", StringComparison.Ordinal);
        AssertRejected(source);
    }

    [TestMethod]
    public void Generated_block_embedded_in_conditional_element_is_rejected()
    {
        var source = Edit(Source).DesiredContent.Replace(
            "// <fullnet-generated catalog.product permissions>",
            "condition ?\n// <fullnet-generated catalog.product permissions>", StringComparison.Ordinal)
            .Replace("new PermissionDefinition(\"generated\"),", "new PermissionDefinition(\"generated\") : new PermissionDefinition(\"other\"),", StringComparison.Ordinal);
        AssertRejected(source);
    }

    private static ClientRouteIntegrationEditResult Edit(string source) =>
        AuthorizationContributorIntegrationEditor.Edit(source, "CatalogAuthorizationContributor.cs", Fragment);

    private static void AssertRejected(string source)
    {
        var result = Edit(source);
        Assert.IsFalse(result.Succeeded);
        Assert.IsFalse(result.Changed);
        Assert.AreEqual(source, result.DesiredContent);
    }
}
