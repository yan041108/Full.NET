using Full.NET.Hosting.Observability;
using Full.NET.Modules.Settings.Contracts;
using Full.NET.Modules.Settings.Features.ManageDiagnosticPolicy;

namespace Full.NET.UnitTests.Settings;

/// <summary>单元测试（设置）：Diagnostic Policy Validation。</summary>
[TestClass]
public sealed class DiagnosticPolicyValidationTests
{
    [TestMethod]
    public void Payload_limits_allow_explicit_disable_and_reject_values_beyond_projection_ceiling()
    {
        var now = DateTimeOffset.UtcNow;
        var rule = new DiagnosticPolicyRuleRequest("Endpoint", "/api/items", null, null, 0, 2048, now.AddMinutes(5));
        Assert.IsNull(DiagnosticPolicyManagementService.Validate(new("Normal", [rule], 0), now));
        Assert.IsNotNull(DiagnosticPolicyManagementService.Validate(new("Normal", [rule with { MaxRequestPayloadBytesOverride = -1 }], 0), now));
        Assert.IsNotNull(DiagnosticPolicyManagementService.Validate(new("Normal", [rule with { MaxResponsePayloadBytesOverride = 2049 }], 0), now));
    }

    [TestMethod]
    public void Instance_capacity_override_requires_global_http_scope()
    {
        var now = DateTimeOffset.UtcNow;
        var tenantScoped = new UpdateDiagnosticPolicyRequest(
            "Normal",
            [new DiagnosticPolicyRuleRequest(
                nameof(DiagnosticPolicyScopeKind.Tenant),
                Guid.NewGuid().ToString(),
                SuccessSampleRateOverride: null,
                BestEffortCapacityOverride: 1,
                MaxRequestPayloadBytesOverride: null,
                MaxResponsePayloadBytesOverride: null,
                ExpiresAtUtc: now.AddMinutes(5))],
            ConfigEntryVersion: 0);

        var rejected = DiagnosticPolicyManagementService.Validate(tenantScoped, now);

        Assert.IsNotNull(rejected);
        Assert.AreEqual("settings.diagnostic_policy.scoped_capacity", rejected.Code);

        var global = tenantScoped with
        {
            Rules = [tenantScoped.Rules[0] with
            {
                ScopeKind = nameof(DiagnosticPolicyScopeKind.Category),
                ScopeValue = LogClassification.HttpOperation,
            }],
        };
        Assert.IsNull(DiagnosticPolicyManagementService.Validate(global, now));
    }
}
