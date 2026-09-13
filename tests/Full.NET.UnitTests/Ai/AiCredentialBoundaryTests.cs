using Full.NET.AI.Abstractions.Models;
using Full.NET.AI.Abstractions.Credentials;
using Full.NET.AI.Abstractions.Connectivity;
using Full.NET.AI.Providers.Http;
using Full.NET.Composition;
using Full.NET.Modules.Ai.Persistence;
using Full.NET.Modules.Ai.Security;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;

namespace Full.NET.UnitTests.Ai;

[TestClass]
public sealed class AiCredentialBoundaryTests
{
    [TestMethod]
    public void Disabled_ai_module_does_not_register_providers_with_missing_credentials()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["FullNet:Modules:Preset"] = "Minimal",
        }).Build();
        services.AddFullNetApplicationModules(config, FullNetHostProfile.Api);
        Assert.IsFalse(services.Any(item => item.ServiceType == typeof(IAiModelClientFactory)));
        Assert.IsFalse(services.Any(item => item.ServiceType == typeof(IAiModelConnectivityProbe)));
        Assert.IsFalse(services.Any(item => item.ServiceType == typeof(IAiModelCredentialProtector)));
    }

    [TestMethod]
    public void Model_binding_does_not_expose_protected_credential_payload()
    {
        Assert.IsNull(typeof(ModelBinding).GetProperty("ProtectedCredential"));
    }

    [TestMethod]
    [DataRow("config")]
    [DataRow("version")]
    [DataRow("provider")]
    [DataRow("model")]
    [DataRow("endpoint")]
    [DataRow("fragment")]
    [DataRow("userinfo")]
    [DataRow("options")]
    public async Task Reference_cannot_be_reused_with_changed_binding(string change)
    {
        using var scope = new AiModelBindingScope();
        var binding = scope.Create(Model());
        var changed = change switch
        {
            "config" => binding with { ConfigId = Guid.NewGuid() },
            "version" => binding with { Version = 2 },
            "provider" => binding with { ProviderKey = "ollama" },
            "model" => binding with { ModelId = "another" },
            "endpoint" => binding with { Endpoint = new Uri("https://attacker.test") },
            "fragment" => binding with { Endpoint = new Uri("https://provider.test/#changed") },
            "userinfo" => binding with { Endpoint = new Uri("https://user:secret@provider.test/") },
            _ => binding with { Options = new Dictionary<string, string> { ["organization_id"] = "another" } },
        };
        Assert.AreEqual("protected-value", await scope.ReadAsync(binding));
        await Assert.ThrowsExactlyAsync<InvalidOperationException>(() => scope.ReadAsync(changed).AsTask());
    }

    [TestMethod]
    public async Task Reference_is_scoped_and_unusable_after_disposal()
    {
        using var issuer = new AiModelBindingScope();
        using var other = new AiModelBindingScope();
        var binding = issuer.Create(Model());
        await Assert.ThrowsExactlyAsync<InvalidOperationException>(() => other.ReadAsync(binding).AsTask());
        issuer.Dispose();
        await Assert.ThrowsExactlyAsync<ObjectDisposedException>(() => issuer.ReadAsync(binding).AsTask());
        Assert.ThrowsExactly<ObjectDisposedException>(() => issuer.Create(Model()));
    }

    [TestMethod]
    public async Task Cancelled_read_does_not_return_credential()
    {
        using var issuer = new AiModelBindingScope();
        var binding = issuer.Create(Model());
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        await Assert.ThrowsAsync<OperationCanceledException>(() => issuer.ReadAsync(binding, cancellation.Token).AsTask());
    }

    [TestMethod]
    public void Provider_writer_keeps_legacy_protection_purpose()
    {
        var protection = new EphemeralDataProtectionProvider();
        IAiModelCredentialProtector writer = new AiModelCredentialProtector(protection);
        var encrypted = writer.Protect("secret-key");
        Assert.AreNotEqual("secret-key", encrypted);
        Assert.AreEqual("secret-key", protection.CreateProtector("Full.NET.Ai.ModelConfigApiKey.v1").Unprotect(encrypted));
    }

    [TestMethod]
    public async Task Changed_endpoint_is_rejected_by_real_factory_before_client_creation()
    {
        using var issuer = new AiModelBindingScope();
        var binding = issuer.Create(Model(TestAiProviders.Protect("secret-key")));
        var http = Substitute.For<IHttpClientFactory>();
        var factory = TestAiProviders.Create(http, issuer).Single(item => item.ProviderKey == "openai_compatible");
        await Assert.ThrowsExactlyAsync<InvalidOperationException>(() => factory.CreateChatClientAsync(
            binding with { Endpoint = new Uri("https://attacker.test") }, default).AsTask());
        http.DidNotReceive().CreateClient(Arg.Any<string>());
    }

    [TestMethod]
    public void Production_registration_shares_reader_only_within_request_scope()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddDataProtection();
        services.AddFullNetApplicationModules(new ConfigurationBuilder().Build(), FullNetHostProfile.Api);
        using var provider = services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true });
        using var first = provider.CreateScope();
        using var second = provider.CreateScope();
        Assert.AreSame(first.ServiceProvider.GetRequiredService<AiModelBindingScope>(),
            first.ServiceProvider.GetRequiredService<IProtectedModelCredentialStore>());
        Assert.AreNotSame(first.ServiceProvider.GetRequiredService<IProtectedModelCredentialStore>(),
            second.ServiceProvider.GetRequiredService<IProtectedModelCredentialStore>());
        Assert.AreEqual(3, first.ServiceProvider.GetServices<IAiModelClientFactory>().Count());
        Assert.ThrowsExactly<InvalidOperationException>(() => provider.GetServices<IAiModelClientFactory>().ToArray());
    }

    private static AiModelConfigRecord Model(string protectedCredential = "protected-value") => new()
    {
        Id = Guid.NewGuid(), Version = 1, ProviderKey = "openai_compatible", ModelId = "test",
        EndpointBaseUrl = "https://provider.test", ApiKeyProtected = protectedCredential, OrganizationId = "organization",
    };
}
