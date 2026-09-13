using Full.NET.Abstractions.Ids;
using Full.NET.Abstractions.Messaging;
using Full.NET.Abstractions.Results;
using Full.NET.Abstractions.Time;
using Full.NET.Data.Abstractions;
using Full.NET.Modules.DataApproval.Contracts;
using Full.NET.Modules.SerialNumbers.Contracts;
using Full.NET.Modules.SerialNumbers.Features.DataApprovalBridge;
using Full.NET.Modules.SerialNumbers.Features.ManageHostSerialRules;
using Microsoft.Extensions.Options;
using NSubstitute;

namespace Full.NET.UnitTests.SerialNumbers;

[TestClass]
public sealed class SerialRuleDisableApprovalServiceTests
{
    /// <summary>禁用审批场景未启用时预览应返回校验失败。</summary>
    [TestMethod]
    public async Task Preview_returns_validation_error_when_disable_approval_not_required()
    {
        var queryExecutor = Substitute.For<IQueryExecutor>();
        var ruleReader = new HostSerialRuleReader(queryExecutor);
        var ruleService = new HostSerialRuleService(
            queryExecutor,
            Substitute.For<IMultiResultQueryExecutor>(),
            Substitute.For<ICommandExecutor>(),
            Substitute.For<ICommandTransaction>(),
            Substitute.For<IClock>(),
            Substitute.For<IIdGenerator>(),
            Options.Create(new DatabaseOptions { Provider = DatabaseProvider.SqlServer, ConnectionString = "unused" }),
            ruleReader);
        var approvalSource = Substitute.For<ISerialRuleChangeApprovalSource>();
        var submissionPort = Substitute.For<IDataApprovalSubmissionPort>();
        var scenarioPolicy = Substitute.For<IDataApprovalScenarioPolicyPort>();
        scenarioPolicy.BlocksDirectWriteAsync(
                DataApprovalScenarioKeys.SerialRuleHostDisable,
                Arg.Any<CancellationToken>())
            .Returns(false);

        var service = new SerialRuleDisableApprovalService(
            ruleService,
            approvalSource,
            submissionPort,
            scenarioPolicy);

        var result = await service.PreviewAsync(
            Guid.NewGuid(),
            new ChangeSerialNumberRuleStatusRequest(1));

        Assert.IsFalse(result.IsSuccess);
        Assert.AreEqual(SerialNumberErrorCodes.DisableApprovalNotRequired, result.Error!.Code);
    }
}
