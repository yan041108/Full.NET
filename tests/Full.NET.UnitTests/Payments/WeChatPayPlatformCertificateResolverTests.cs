using System.Net;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Text.Json;
using Full.NET.Abstractions.Time;
using Full.NET.Modules.Payments.Connectivity;
using Full.NET.Modules.Payments.Persistence;
using Full.NET.Modules.Payments.Security;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.DependencyInjection;

namespace Full.NET.UnitTests.Payments;

[TestClass]
public sealed class WeChatPayPlatformCertificateResolverTests
{
    [TestMethod]
    public async Task Cache_refreshes_public_key_after_absolute_expiry()
    {
        using var fixture = new Fixture();
        var merchant = fixture.Merchant();
        Assert.IsNotNull(await fixture.Resolver.ResolvePublicKeyPemAsync(merchant, "serial"));
        await fixture.Resolver.ResolvePublicKeyPemAsync(merchant, "serial");
        Assert.AreEqual(1, fixture.Handler.Requests);
        fixture.Clock.UtcNow = fixture.Clock.UtcNow.AddHours(7);
        Assert.IsNotNull(await fixture.Resolver.ResolvePublicKeyPemAsync(merchant, "serial"));
        Assert.AreEqual(2, fixture.Handler.Requests);
    }

    [TestMethod]
    public async Task Cache_evicts_oldest_public_key_at_capacity()
    {
        using var fixture = new Fixture();
        var first = fixture.Merchant();
        await fixture.Resolver.ResolvePublicKeyPemAsync(first, "serial");
        fixture.Clock.UtcNow = fixture.Clock.UtcNow.AddMinutes(1);
        for (var i = 0; i < 1024; i++)
            await fixture.Resolver.ResolvePublicKeyPemAsync(fixture.Merchant(), "serial");
        await fixture.Resolver.ResolvePublicKeyPemAsync(first, "serial");
        Assert.AreEqual(1026, fixture.Handler.Requests);
    }

    [TestMethod]
    public async Task Cache_does_not_store_unknown_serial_numbers()
    {
        using var fixture = new Fixture();
        var merchant = fixture.Merchant();
        Assert.IsNull(await fixture.Resolver.ResolvePublicKeyPemAsync(merchant, "unknown"));
        Assert.IsNull(await fixture.Resolver.ResolvePublicKeyPemAsync(merchant, "unknown"));
        Assert.AreEqual(2, fixture.Handler.Requests);
    }

    private sealed class Fixture : IDisposable
    {
        private readonly ServiceProvider services;
        private readonly string protectedKey;
        private readonly string protectedPrivateKey;
        public MutableClock Clock { get; } = new();
        public ResponseHandler Handler { get; }
        public WeChatPayPlatformCertificateResolver Resolver { get; }

        public Fixture()
        {
            using var rsa = RSA.Create(2048);
            var request = new CertificateRequest("CN=unit-test", rsa, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
            using var certificate = request.CreateSelfSigned(DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow.AddDays(1));
            const string apiKey = "12345678901234567890123456789012";
            const string nonce = "123456789012";
            var plain = Encoding.UTF8.GetBytes(certificate.ExportCertificatePem());
            var encrypted = new byte[plain.Length + 16];
            using (var aes = new AesGcm(Encoding.UTF8.GetBytes(apiKey), 16))
                aes.Encrypt(Encoding.UTF8.GetBytes(nonce), plain, encrypted.AsSpan(0, plain.Length), encrypted.AsSpan(plain.Length), []);
            Handler = new ResponseHandler(JsonSerializer.Serialize(new
            {
                data = new[] { new { serial_no = "serial", encrypt_certificate = new
                { associated_data = "", nonce, ciphertext = Convert.ToBase64String(encrypted) } } }
            }));
            var protector = new PaymentSecretProtector(new EphemeralDataProtectionProvider());
            protectedKey = protector.ProtectApiV3Key(apiKey);
            protectedPrivateKey = protector.ProtectPrivateKey(rsa.ExportPkcs8PrivateKeyPem());
            services = new ServiceCollection().AddSingleton<IClock>(Clock).BuildServiceProvider();
            Resolver = ActivatorUtilities.CreateInstance<WeChatPayPlatformCertificateResolver>(services,
                new WeChatNativePayClient(new ClientFactory(Handler), protector));
        }

        public PaymentMerchantConfigRecord Merchant() => new()
        {
            Id = Guid.CreateVersion7(), MerchantId = "merchant", CertificateSerialNo = "merchant-serial",
            ApiV3KeyProtected = protectedKey, PrivateKeyProtected = protectedPrivateKey
        };

        public void Dispose() { services.Dispose(); Handler.Dispose(); }
    }

    private sealed class MutableClock : IClock
    {
        public DateTimeOffset UtcNow { get; set; } = DateTimeOffset.Parse("2026-09-12T00:00:00Z");
    }

    private sealed class ClientFactory(ResponseHandler handler) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => new(handler, disposeHandler: false) { BaseAddress = new Uri("https://unit.test") };
    }

    private sealed class ResponseHandler(string body) : HttpMessageHandler
    {
        public int Requests { get; private set; }
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Requests++;
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(body, Encoding.UTF8, "application/json") });
        }
    }
}
