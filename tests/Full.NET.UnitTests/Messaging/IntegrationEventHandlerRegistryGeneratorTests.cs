using Full.NET.Messaging.Generators;
using Full.NET.Messaging.Abstractions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace Full.NET.UnitTests.Messaging;

[TestClass]
public sealed class IntegrationEventHandlerRegistryGeneratorTests
{
    [TestMethod]
    public void Generator_emits_stable_three_key_switch_without_reflection()
    {
        var result = RunGenerator(
            """
            using Full.NET.Messaging.Abstractions;

            [IntegrationEventSubscription("consumer.beta", "fullnet.test.event.beta", 2)]
            public sealed class BetaSubscription { }

            [IntegrationEventSubscription("consumer.alpha", "fullnet.test.event.alpha", 1)]
            public sealed class AlphaSubscription { }
            """);

        Assert.IsFalse(result.Diagnostics.Any(item => item.Severity == DiagnosticSeverity.Error));
        var generated = AssertExactlyOneSource(result);
        StringAssert.Contains(generated, "(\"fullnet.test.event.alpha\", 1, \"consumer.alpha\")");
        StringAssert.Contains(generated, "typeof(global::AlphaSubscription)");
        StringAssert.Contains(generated, "(\"fullnet.test.event.beta\", 2, \"consumer.beta\")");
        Assert.IsTrue(
            generated.IndexOf("fullnet.test.event.alpha", StringComparison.Ordinal)
            < generated.IndexOf("fullnet.test.event.beta", StringComparison.Ordinal));
        Assert.IsFalse(generated.Contains("Reflection", StringComparison.Ordinal));
        Assert.IsFalse(generated.Contains("GetTypes", StringComparison.Ordinal));
    }

    [TestMethod]
    public void Generator_reports_duplicate_route_and_invalid_metadata()
    {
        var duplicate = RunGenerator(
            """
            using Full.NET.Messaging.Abstractions;

            [IntegrationEventSubscription("consumer.same", "fullnet.test.event.same", 1)]
            public sealed class FirstSubscription { }

            [IntegrationEventSubscription("consumer.same", "fullnet.test.event.same", 1)]
            public sealed class SecondSubscription { }

            [IntegrationEventSubscription("Bad Consumer", "Bad Event", 0)]
            public sealed class InvalidSubscription { }
            """);

        CollectionAssert.IsSubsetOf(
            new[] { "FNMESSAGING001", "FNMESSAGING002" },
            duplicate.Diagnostics.Select(item => item.Id).ToArray());
    }

    [TestMethod]
    public void Generator_emits_empty_registry_when_no_subscription_exists()
    {
        var result = RunGenerator("public sealed class NoSubscription { }");

        Assert.IsFalse(result.Diagnostics.Any(item => item.Severity == DiagnosticSeverity.Error));
        var generated = AssertExactlyOneSource(result);
        StringAssert.Contains(generated, "descriptor = default;");
        StringAssert.Contains(generated, "return false;");
    }

    private static GeneratorRunResult RunGenerator(string source)
    {
        var compilation = CSharpCompilation.Create(
            "GeneratorTests",
            [CSharpSyntaxTree.ParseText(source)],
            GetReferences(),
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
        var generator = new IntegrationEventHandlerRegistryGenerator();
        GeneratorDriver driver = CSharpGeneratorDriver.Create(generator);
        driver = driver.RunGenerators(compilation);
        return driver.GetRunResult().Results.Single();
    }

    private static IReadOnlyList<MetadataReference> GetReferences()
    {
        var trustedAssemblies = ((string)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")!)
            .Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries)
            .Select(path => MetadataReference.CreateFromFile(path))
            .Cast<MetadataReference>()
            .ToList();
        trustedAssemblies.Add(MetadataReference.CreateFromFile(
            typeof(IntegrationEventSubscriptionAttribute).Assembly.Location));
        return trustedAssemblies;
    }

    private static string AssertExactlyOneSource(GeneratorRunResult result)
    {
        Assert.HasCount(1, result.GeneratedSources);
        return result.GeneratedSources[0].SourceText.ToString();
    }
}
