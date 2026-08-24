using System.Reflection;
using System.Text.Json.Serialization;
using Full.NET.Caching.Fusion;
using Full.NET.Composition;
using Full.NET.Data.Abstractions;
using Full.NET.Data.Dapper;
using Full.NET.Data.MySql;
using Full.NET.Hosting.Observability;
using Full.NET.Modularity.Modules;
using Full.NET.Modules.Identity.Contracts;
using Full.NET.Modules.Notifications.Contracts;
using Full.NET.Modules.Tenancy.Contracts;
using Full.NET.Realtime.SignalR;
using Full.NET.Serialization.MemoryPack;
using global::MemoryPack;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Json;
using Microsoft.AspNetCore.Http.Metadata;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Full.NET.ArchitectureTests;

[TestClass]
public sealed class SerializationRulesTests
{
    private static readonly string[] ForbiddenTokens =
    [
        "TypelessFormatter",
        "TypelessContractlessStandardResolver",
        "ContractlessStandardResolver",
        "MessagePackSerializer",
        "MessagePackSerializer.DefaultOptions",
        "AddMessagePackProtocol",
        "Newtonsoft.Json",
    ];

    private static readonly Type[] ProductionIntegrationEventTypes =
    [
        typeof(TenantProvisionedIntegrationEvent),
        typeof(TenantChangedIntegrationEvent),
        typeof(AnnouncementPublishedIntegrationEvent),
        typeof(InboxMessageReceivedIntegrationEvent),
        typeof(InboxReadStateChangedIntegrationEvent),
        typeof(IdentityOrganizationUnitChangedIntegrationEvent),
    ];

    [TestMethod]
    public void ProductionSerialization_UsesApprovedApisAndGeneratedHttpContracts()
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
        AssertHttpEndpointContractsUseSourceGeneration();
    }

    [TestMethod]
    public void MemoryPackSerializer_UsesStableContentType()
    {
        var serializer = new MemoryPackIntegrationEventSerializer();

        Assert.AreEqual("application/x-memorypack", serializer.ContentType);
    }

    [TestMethod]
    public void IntegrationEvents_UseMemoryPackablePartialRecords()
    {
        foreach (var eventType in ProductionIntegrationEventTypes)
        {
            Assert.IsNotNull(
                eventType.GetCustomAttribute<MemoryPackableAttribute>(),
                $"{eventType.FullName} ???? [MemoryPackable]?");
            Assert.IsTrue(
                eventType.IsDefined(typeof(MemoryPackableAttribute), inherit: false),
                $"{eventType.FullName} ??? partial ??????????");
        }
    }

    private static bool IsGeneratedOutput(string path) =>
        path.Contains(
            $"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}",
            StringComparison.OrdinalIgnoreCase)
        || path.Contains(
            $"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}",
            StringComparison.OrdinalIgnoreCase);

    private static void AssertHttpEndpointContractsUseSourceGeneration()
    {
        var builder = WebApplication.CreateBuilder();
        builder.Environment.EnvironmentName = "Testing";
        builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
        {
            [$"{DatabaseOptions.SectionName}:Provider"] = DatabaseProvider.SqlServer.ToString(),
            [$"{DatabaseOptions.SectionName}:ConnectionString"] =
                "Server=127.0.0.1,1;Database=fullnet_architecture;User Id=sa;Password=FullNet_Test!123;TrustServerCertificate=True;Connect Timeout=1",
            [$"{DatabaseOptions.SectionName}:CommandTimeoutSeconds"] = "30",
            [$"{DatabaseOptions.SectionName}:MySqlGuidStorageMode"] =
                MySqlGuidStorageMode.Binary16.ToString(),
            ["Identity:AllowDevelopmentEphemeralSigningKey"] = "true",
            ["Identity:EnableRemoteSuperAdministratorManagement"] = "true",
            ["Identity:AllowedOrigins:0"] = "http://localhost",
            ["Tenancy:HostDomains:0"] = "localhost",
        });
        builder.AddFullNetServiceDefaults();
        builder.Services.AddFullNetDapper(builder.Configuration, builder.Environment.EnvironmentName);
        builder.Services.AddFullNetDatabaseSchemaModeGuard();
        builder.Services.AddFullNetMemoryPack();
        builder.Services.AddFullNetCaching(builder.Configuration, builder.Environment.EnvironmentName);
        builder.Services.AddFullNetRealtimeSignalR(
            builder.Configuration,
            builder.Environment.EnvironmentName);
        builder.Services.AddFullNetApplicationModules(
            builder.Configuration,
            FullNetHostProfile.Api);

        var app = builder.Build();
        app.MapPost(
                "/api/v1/__source-generation-guard-probe",
                (SourceGenerationGuardProbeRequest request) =>
                    new SourceGenerationGuardProbeResponse(request.Value))
            .Produces<SourceGenerationGuardProbeResponse>();
        app.MapFullNetModules();

        var serializerContexts = app.Services
            .GetRequiredService<IOptions<JsonOptions>>()
            .Value
            .SerializerOptions
            .TypeInfoResolverChain
            .OfType<JsonSerializerContext>()
            .ToArray();
        var uncoveredContracts = ((IEndpointRouteBuilder)app).DataSources
            .SelectMany(dataSource => dataSource.Endpoints)
            .OfType<RouteEndpoint>()
            .Where(endpoint => endpoint.RoutePattern.RawText?.StartsWith(
                "/api/v1/",
                StringComparison.Ordinal) == true)
            .SelectMany(endpoint => endpoint.Metadata
                .OfType<IAcceptsMetadata>()
                .Select(metadata => metadata.RequestType)
                .Concat(endpoint.Metadata
                    .OfType<IProducesResponseTypeMetadata>()
                    .Select(metadata => metadata.Type))
                .Where(type => type?.Namespace?.StartsWith(
                    "Full.NET.",
                    StringComparison.Ordinal) == true)
                .Select(type => new
                {
                    Route = endpoint.RoutePattern.RawText!,
                    Type = type!,
                }))
            .Where(contract => serializerContexts.All(
                context => context.GetTypeInfo(contract.Type) is null))
            .Select(contract => $"{contract.Route}: {contract.Type.FullName}")
            .Distinct(StringComparer.Ordinal)
            .OrderBy(contract => contract, StringComparer.Ordinal)
            .ToArray();
        var probePrefix = "/api/v1/__source-generation-guard-probe: ";
        var probeContracts = uncoveredContracts
            .Where(contract => contract.StartsWith(probePrefix, StringComparison.Ordinal))
            .ToArray();
        var expectedProbeContracts = new[]
        {
            probePrefix + typeof(SourceGenerationGuardProbeRequest).FullName,
            probePrefix + typeof(SourceGenerationGuardProbeResponse).FullName,
        }
            .OrderBy(contract => contract, StringComparer.Ordinal)
            .ToArray();
        CollectionAssert.AreEqual(expectedProbeContracts, probeContracts);

        var productionContracts = uncoveredContracts
            .Where(contract => !contract.StartsWith(probePrefix, StringComparison.Ordinal))
            .ToArray();

        Assert.HasCount(
            0,
            productionContracts,
            "?? HTTP Endpoint ????????? System.Text.Json ???????"
            + Environment.NewLine
            + string.Join(Environment.NewLine, productionContracts));
    }

    private sealed record SourceGenerationGuardProbeRequest(string Value);

    private sealed record SourceGenerationGuardProbeResponse(string Value);

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
