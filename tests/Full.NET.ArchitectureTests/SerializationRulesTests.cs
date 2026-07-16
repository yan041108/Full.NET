using System.Reflection;
using Full.NET.Modules.Tenancy.Contracts;
using MessagePack;

namespace Full.NET.ArchitectureTests;

[TestClass]
public sealed class SerializationRulesTests
{
    private static readonly string[] ForbiddenTokens =
    [
        "TypelessFormatter",
        "TypelessContractlessStandardResolver",
        "ContractlessStandardResolver",
        "MessagePackSerializer.DefaultOptions",
        "Newtonsoft.Json",
    ];

    [TestMethod]
    public void ProductionSources_DoNotUseForbiddenSerializationApis()
    {
        var repositoryRoot = FindRepositoryRoot();
        var offenders = Directory
            .EnumerateFiles(Path.Combine(repositoryRoot, "src"), "*.cs", SearchOption.AllDirectories)
            .Where(path => !IsGeneratedOutput(path))
            .Select(path => new
            {
                Path = Path.GetRelativePath(repositoryRoot, path),
                Content = File.ReadAllText(path),
            })
            .Where(file => ForbiddenTokens.Any(token =>
                file.Content.Contains(token, StringComparison.Ordinal)))
            .Select(file => file.Path)
            .OrderBy(path => path, StringComparer.Ordinal)
            .ToArray();

        Assert.HasCount(0, offenders);
    }

    [TestMethod]
    public void MessagePackSerializer_EnforcesUntrustedDataSecurity()
    {
        var repositoryRoot = FindRepositoryRoot();
        var path = Path.Combine(
            repositoryRoot,
            "src",
            "BuildingBlocks",
            "Full.NET.Serialization.MessagePack",
            "MessagePackIntegrationEventSerializer.cs");
        var source = File.ReadAllText(path);

        StringAssert.Contains(
            source,
            "WithSecurity(MessagePackSecurity.UntrustedData)");
        foreach (var token in ForbiddenTokens)
        {
            Assert.DoesNotContain(token, source, StringComparison.Ordinal);
        }
    }

    [TestMethod]
    public void TenantProvisionedEvent_UsesUniqueIntegerKeysZeroThroughTwo()
    {
        var eventType = typeof(TenantProvisionedIntegrationEvent);
        Assert.IsNotNull(eventType.GetCustomAttribute<MessagePackObjectAttribute>());
        var keys = eventType
            .GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Select(property => property.GetCustomAttribute<KeyAttribute>())
            .Where(attribute => attribute is not null)
            .Select(attribute => attribute!.IntKey)
            .OrderBy(key => key)
            .ToArray();

        CollectionAssert.AreEqual(new[] { 0, 1, 2 }, keys);
        Assert.AreEqual(keys.Length, keys.Distinct().Count());
    }

    private static bool IsGeneratedOutput(string path) =>
        path.Contains(
            $"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}",
            StringComparison.OrdinalIgnoreCase)
        || path.Contains(
            $"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}",
            StringComparison.OrdinalIgnoreCase);

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "Full.NET.slnx")))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        throw new DirectoryNotFoundException(
            "Could not locate the Full.NET repository root.");
    }
}
