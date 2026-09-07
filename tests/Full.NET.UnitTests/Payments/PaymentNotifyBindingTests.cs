using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Full.NET.Abstractions.Ids;
using Full.NET.Abstractions.Messaging;
using Full.NET.Abstractions.Time;
using Full.NET.Data.Abstractions;
using Full.NET.Data.Dapper;
using Full.NET.Modules.Payments.Connectivity;
using Full.NET.Modules.Payments.Contracts;
using Full.NET.Modules.Payments.Features.ReceiveWeChatNotify;
using Full.NET.Modules.Payments.Persistence;
using Full.NET.Modules.Payments.Security;
using Microsoft.AspNetCore.DataProtection;
using NSubstitute;

namespace Full.NET.UnitTests.Payments;

/// <summary>使用真实签名和密文验证回调身份关联及交易终态，外部资源全部隔离。</summary>
[TestClass]
public sealed class PaymentNotifyBindingTests
{
    /// <summary>合法验签不等于可以更新任意订单，已退款状态也不得被迟到通知覆盖。</summary>
    /// <param name="scenario">需要改变的订单边界或状态。</param>
    /// <param name="ackCode">期望应答码。</param>
    /// <param name="updates">允许的订单状态更新次数。</param>
    [TestMethod]
    [DataRow("valid", "SUCCESS", 1)]
    [DataRow("receipt-race", "FAIL", 1)]
    [DataRow("merchant", "FAIL", 0)]
    [DataRow("tenant", "FAIL", 0)]
    [DataRow("channel", "FAIL", 0)]
    [DataRow("currency", "FAIL", 0)]
    [DataRow("refunded", "SUCCESS", 0)]
    [DataRow("refunding", "SUCCESS", 0)]
    [DataRow("closed", "FAIL", 0)]
    [DataRow("rejected-receipt", "FAIL", 0)]
    public async Task Signed_notification_preserves_order_binding_and_terminal_state(
        string scenario, string ackCode, int updates)
    {
        const string key = "01234567890123456789012345678901";
        var merchantId = Guid.NewGuid();
        var tenantId = Guid.NewGuid();
        var protector = new PaymentSecretProtector(new EphemeralDataProtectionProvider());
        var merchant = new PaymentMerchantConfigRecord
        {
            Id = merchantId, TenantId = tenantId, AppId = "review-app", MerchantId = "review-merchant",
            ChannelKey = PaymentChannelKeys.WeChatNative, IsEnabled = true,
            ApiV3KeyProtected = protector.ProtectApiV3Key(key),
        };
        var order = new PaymentOrderRecord
        {
            Id = Guid.NewGuid(), OutTradeNo = "review-order", AmountMinor = 100, Version = 1,
            MerchantConfigId = scenario == "merchant" ? Guid.NewGuid() : merchantId,
            TenantId = scenario == "tenant" ? Guid.NewGuid() : tenantId,
            ChannelKey = scenario == "channel" ? PaymentChannelKeys.AlipayPage : PaymentChannelKeys.WeChatNative,
            Currency = scenario == "currency" ? "USD" : "CNY",
            TradeStateKey = scenario switch
            {
                "refunded" => PaymentTradeStateKeys.Refunded,
                "refunding" => PaymentTradeStateKeys.Refunding,
                "closed" => PaymentTradeStateKeys.Closed,
                _ => PaymentTradeStateKeys.AwaitingPayment,
            },
        };
        var queries = Substitute.For<IQueryExecutor>();
        queries.QuerySingleOrDefaultAsync<PaymentMerchantConfigRecord>(
            Arg.Any<SqlStatement>(), Arg.Any<object?>(), Arg.Any<CancellationToken>()).Returns(merchant);
        queries.QuerySingleOrDefaultAsync<PaymentOrderRecord>(
            Arg.Any<SqlStatement>(), Arg.Any<object?>(), Arg.Any<CancellationToken>()).Returns(order);
        if (scenario == "rejected-receipt")
        {
            queries.QuerySingleOrDefaultAsync<PaymentNotifyReceiptRecord>(
                Arg.Any<SqlStatement>(), Arg.Any<object?>(), Arg.Any<CancellationToken>())
                .Returns(new PaymentNotifyReceiptRecord { ProcessStatusKey = PaymentNotifyProcessStatusKeys.Rejected });
        }

        var commands = Substitute.For<ICommandExecutor>();
        commands.ExecuteAsync(Arg.Any<SqlStatement>(), Arg.Any<object?>(), Arg.Any<CancellationToken>()).Returns(1);
        if (scenario == "receipt-race")
        {
            commands.ExecuteAsync(PaymentNotifyReceiptSql.Insert, Arg.Any<object?>(), Arg.Any<CancellationToken>())
                .Returns<int>(_ => throw new DataCommandException(DataCommandFailureKind.UniqueConstraint, new InvalidOperationException()));
        }
        var coordinator = new RecordingDbTransactionCoordinator();
        var transaction = new DapperCommandTransaction(coordinator);
        using var rsa = RSA.Create(2048);
        var certificates = new FixedCertificateResolver(rsa.ExportSubjectPublicKeyInfoPem());
        var clock = Substitute.For<IClock>();
        clock.UtcNow.Returns(DateTimeOffset.UtcNow);
        var ids = Substitute.For<IIdGenerator>();
        ids.NewId().Returns(_ => Guid.NewGuid());
        var service = new PaymentWeChatNotifyService(queries, commands, transaction, protector, certificates, clock, ids);

        var plaintext = JsonSerializer.Serialize(new
        {
            mchid = merchant.MerchantId, appid = merchant.AppId, out_trade_no = order.OutTradeNo,
            transaction_id = "review-transaction", trade_state = "SUCCESS", amount = new { total = 100, currency = "CNY" },
        });
        var body = CreateEnvelope(key, plaintext);
        var timestamp = clock.UtcNow.ToUnixTimeSeconds().ToString(System.Globalization.CultureInfo.InvariantCulture);
        const string nonce = "review-notify-nonce";
        var signature = Convert.ToBase64String(rsa.SignData(Encoding.UTF8.GetBytes($"{timestamp}\n{nonce}\n{body}\n"),
            HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1));

        var response = await service.HandleAsync(merchantId, "/review/callback", timestamp, nonce,
            signature, "review-serial", body);

        Assert.AreEqual(ackCode, response.Code);
        if (scenario == "receipt-race")
        {
            Assert.AreEqual(1, coordinator.RollbackCount);
            Assert.AreEqual(0, coordinator.CommitCount);
        }
        await commands.Received(updates).ExecuteAsync(PaymentOrderSql.UpdateTradeState,
            Arg.Any<object?>(), Arg.Any<CancellationToken>());
    }

    /// <summary>创建带完整性校验标签的测试通知密文，不访问真实商户密钥。</summary>
    /// <param name="key">隔离测试 API 密钥。</param>
    /// <param name="plaintext">需要加密的交易报文。</param>
    /// <returns>实际服务可解密的通知正文。</returns>
    private static string CreateEnvelope(string key, string plaintext)
    {
        const string nonce = "593BEC0C930B";
        var bytes = Encoding.UTF8.GetBytes(plaintext);
        var ciphertext = new byte[bytes.Length];
        var tag = new byte[16];
        using var cipher = new AesGcm(Encoding.UTF8.GetBytes(key), tag.Length);
        cipher.Encrypt(Encoding.UTF8.GetBytes(nonce), bytes, ciphertext, tag, Encoding.UTF8.GetBytes("transaction"));
        return JsonSerializer.Serialize(new
        {
            id = "review-event", event_type = "TRANSACTION.SUCCESS",
            resource = new
            {
                algorithm = "AEAD_AES_256_GCM", nonce, associated_data = "transaction",
                ciphertext = Convert.ToBase64String([.. ciphertext, .. tag]),
            },
        });
    }

    /// <summary>仅返回本测试 RSA 公钥，不访问网络证书目录。</summary>
    /// <param name="publicKey">测试公钥。</param>
    private sealed class FixedCertificateResolver(string publicKey) : IWeChatPayPlatformCertificateResolver
    {
        /// <inheritdoc />
        public Task<string?> ResolvePublicKeyPemAsync(PaymentMerchantConfigRecord merchantConfig,
            string platformSerialNo, CancellationToken cancellationToken = default) => Task.FromResult<string?>(publicKey);
    }
}
