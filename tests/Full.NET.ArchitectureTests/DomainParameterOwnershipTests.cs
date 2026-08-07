namespace Full.NET.ArchitectureTests;

[TestClass]
public sealed class DomainParameterOwnershipTests
{
    [TestMethod]
    public void Domain_parameter_gate_matches_production_modules()
    {
        var root = ArchitectureRepositoryRoot.Find();
        var violations = DomainParameterOwnershipScanner.ScanProductionModuleViolations(root);
        Assert.HasCount(0, violations, string.Join(Environment.NewLine, violations));
    }

    [TestMethod]
    public void Domain_parameter_gate_rejects_config_entry_contract_fixture()
    {
        const string fixture = """
            using Full.NET.Modules.Settings.Contracts;

            internal sealed class AppointmentPolicyService
            {
                public void Apply(CreateConfigEntryRequest request)
                {
                    _ = request.Value;
                }
            }
            """;

        Assert.IsTrue(DomainParameterOwnershipScanner.ContainsForbiddenConfigEntryUsage(fixture));
    }

    [TestMethod]
    public void Domain_parameter_gate_rejects_config_entry_table_fixture()
    {
        const string fixture = """
            SELECT Value
            FROM fn_settings_config_entry
            WHERE ConfigKey = @ConfigKey
            """;

        Assert.IsTrue(DomainParameterOwnershipScanner.ContainsForbiddenConfigEntryUsage(fixture));
    }

    [TestMethod]
    public void Domain_parameter_gate_rejects_config_entry_business_rule_parsing_fixture()
    {
        const string fixture = """
            private static int ReadLeadHours(ConfigEntryResponse entry) =>
                int.Parse(entry.Value, System.Globalization.CultureInfo.InvariantCulture);
            """;

        Assert.IsTrue(DomainParameterOwnershipScanner.ParsesBusinessRuleFromConfigEntry(fixture));
        Assert.IsTrue(DomainParameterOwnershipScanner.ContainsForbiddenConfigEntryUsage(fixture));
    }

    [TestMethod]
    public void Domain_parameter_gate_allows_unrelated_settings_contract_fixture()
    {
        const string fixture = """
            using Full.NET.Modules.Settings.Contracts;

            internal sealed class HostDictTypeEndpoint
            {
                public void Map(CreateHostDictTypeRequest request)
                {
                    _ = request.Code;
                }
            }
            """;

        Assert.IsFalse(DomainParameterOwnershipScanner.ContainsForbiddenConfigEntryUsage(fixture));
    }

    [TestMethod]
    public void Domain_parameter_gate_allows_settings_diagnostic_policy_sources()
    {
        var root = ArchitectureRepositoryRoot.Find();
        var path = Path.Combine(
            root,
            "src",
            "Modules",
            "Full.NET.Modules.Settings",
            "Features",
            "ManageDiagnosticPolicy",
            "DiagnosticPolicyManagementService.cs");
        var content = File.ReadAllText(path);
        var violations = DomainParameterOwnershipScanner.AnalyzeSource(
            "src/Modules/Full.NET.Modules.Settings/Features/ManageDiagnosticPolicy/DiagnosticPolicyManagementService.cs",
            content);

        Assert.HasCount(0, violations, string.Join(Environment.NewLine, violations));
    }
}