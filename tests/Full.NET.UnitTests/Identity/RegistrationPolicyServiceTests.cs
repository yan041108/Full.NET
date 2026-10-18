using Full.NET.Abstractions.Results;
using Full.NET.Abstractions.Time;
using Full.NET.Data.Abstractions;
using Full.NET.Modules.Identity.Contracts;
using Full.NET.Modules.Identity.Features.ManageRegistrationPolicy;
using Full.NET.Modules.Identity.Persistence;
using NSubstitute;

namespace Full.NET.UnitTests.Identity;

[TestClass]
public sealed class RegistrationPolicyServiceTests
{
    [TestMethod]
    [DataRow((byte)3)]
    [DataRow((byte)255)]
    public async Task Unknown_persisted_mode_fails_closed(byte mode)
    {
        var query = Substitute.For<IQueryExecutor>();
        query.QuerySingleOrDefaultAsync<RegistrationPolicyRecord>(
            Arg.Any<SqlStatement>(), Arg.Any<object?>(), Arg.Any<CancellationToken>())
            .Returns(new RegistrationPolicyRecord(
                IdentityRegistrationPolicyConstants.PolicyId, true, mode, DateTimeOffset.UtcNow, 1));
        var result = await new RegistrationPolicyService(
            query, Substitute.For<ICommandExecutor>(), Substitute.For<IClock>()).GetAsync();
        Assert.IsFalse(result.IsSuccess);
        Assert.AreEqual(IdentityErrorCodes.RegistrationDisabled, result.Error!.Code);
    }

    [TestMethod]
    [DataRow((byte)0, false)]
    [DataRow((byte)0, true)]
    [DataRow((byte)1, false)]
    [DataRow((byte)1, true)]
    [DataRow((byte)2, false)]
    [DataRow((byte)2, true)]
    public void Persisted_mode_is_authoritative(byte mode, bool legacyEnabled)
    {
        var response = RegistrationPolicyService.Map(new RegistrationPolicyRecord(
            IdentityRegistrationPolicyConstants.PolicyId, legacyEnabled, mode, DateTimeOffset.UtcNow, 1));
        Assert.AreEqual((IdentityRegistrationMode)mode, response.RegistrationMode);
        Assert.AreEqual(mode == 2, response.IsPublicRegistrationEnabled);
    }

    [TestMethod]
    [DataRow((byte)3, 1)]
    [DataRow((byte)255, 1)]
    [DataRow((byte)0, 0)]
    [DataRow((byte)2, -1)]
    public async Task Invalid_policy_is_rejected_before_dependencies(byte mode, int version)
    {
        var query = Substitute.For<IQueryExecutor>();
        var command = Substitute.For<ICommandExecutor>();
        var clock = Substitute.For<IClock>();
        var result = await new RegistrationPolicyService(query, command, clock).UpdateAsync(
            new UpdateRegistrationPolicyRequest(true, version, (IdentityRegistrationMode)mode));
        Assert.IsFalse(result.IsSuccess);
        Assert.AreEqual(ValidationErrorCodes.Failed, result.Error!.Code);
        Assert.AreEqual(ErrorType.Validation, result.Error.Type);
        Assert.AreEqual(0, command.ReceivedCalls().Count());
        Assert.AreEqual(0, query.ReceivedCalls().Count());
        Assert.AreEqual(0, clock.ReceivedCalls().Count());
    }
}
