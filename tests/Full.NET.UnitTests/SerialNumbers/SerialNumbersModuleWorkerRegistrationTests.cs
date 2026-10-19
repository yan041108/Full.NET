using Full.NET.Modules.SerialNumbers;
using Full.NET.Modules.SerialNumbers.Contracts;
using Full.NET.Modules.SerialNumbers.Features.DataApprovalBridge;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Full.NET.UnitTests.SerialNumbers;

/// <summary>单元测试（流水号）：Serial Numbers Module Worker Registration。</summary>
[TestClass]
public sealed class SerialNumbersModuleWorkerRegistrationTests
{
    [TestMethod]
    public void AddBackgroundServices_registers_data_approval_appliers()
    {
        var services = new ServiceCollection();
        new SerialNumbersModule().AddBackgroundServices(
            services,
            new ConfigurationBuilder().Build());

        Assert.IsTrue(services.Any(descriptor =>
            descriptor.ServiceType == typeof(ISerialRuleChangeApprovalApplier)
            && descriptor.ImplementationType == typeof(SerialRuleChangeApprovalApplier)
            && descriptor.Lifetime == ServiceLifetime.Scoped));
        Assert.IsTrue(services.Any(descriptor =>
            descriptor.ServiceType == typeof(ISerialRuleDisableApprovalApplier)
            && descriptor.ImplementationType == typeof(SerialRuleDisableApprovalApplier)
            && descriptor.Lifetime == ServiceLifetime.Scoped));
    }
}
