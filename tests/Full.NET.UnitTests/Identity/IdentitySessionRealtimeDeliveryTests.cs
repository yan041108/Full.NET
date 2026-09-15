using Full.NET.Modules.Identity.Features.ManageHostOnlineSessions;
using Full.NET.Realtime;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using NSubstitute.ExceptionExtensions;

namespace Full.NET.UnitTests.Identity;

[TestClass]
public sealed class IdentitySessionRealtimeDeliveryTests
{
    private static readonly Guid UserId =
        Guid.Parse("01981a75-f500-7000-8000-000000000011");
    private static readonly Guid SessionId =
        Guid.Parse("01981a75-f500-7000-8000-000000000012");

    [TestMethod]
    public async Task Publish_sessions_revoked_retries_then_succeeds()
    {
        var publisher = Substitute.For<IRealtimePublisher>();
        var attempts = 0;
        publisher
            .PublishToUserAsync(
                UserId,
                Arg.Any<RealtimeMessage>(),
                Arg.Any<CancellationToken>())
            .Returns(_ =>
            {
                attempts++;
                if (attempts == 1)
                {
                    throw new InvalidOperationException("transient");
                }

                return Task.CompletedTask;
            });
        var delivery = new IdentitySessionRealtimeDelivery(
            publisher,
            NullLogger<IdentitySessionRealtimeDelivery>.Instance);

        await delivery.PublishSessionsRevokedAsync(UserId, [SessionId]);

        await publisher.Received(2).PublishToUserAsync(
            UserId,
            Arg.Is<RealtimeMessage>(message =>
                message != null
                && message.Code == RealtimeMessageCodes.SessionRevoked),
            Arg.Any<CancellationToken>());
    }

    [TestMethod]
    public async Task Publish_sessions_revoked_swallows_failure_after_bounded_attempts()
    {
        var publisher = Substitute.For<IRealtimePublisher>();
        publisher
            .PublishToUserAsync(
                UserId,
                Arg.Any<RealtimeMessage>(),
                Arg.Any<CancellationToken>())
            .ThrowsAsync(new InvalidOperationException("persistent"));
        var delivery = new IdentitySessionRealtimeDelivery(
            publisher,
            NullLogger<IdentitySessionRealtimeDelivery>.Instance);

        await delivery.PublishSessionsRevokedAsync(UserId, [SessionId]);

        await publisher.Received(3).PublishToUserAsync(
            UserId,
            Arg.Any<RealtimeMessage>(),
            Arg.Any<CancellationToken>());
    }
}