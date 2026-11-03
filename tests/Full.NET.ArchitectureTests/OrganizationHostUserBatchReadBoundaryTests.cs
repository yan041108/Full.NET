namespace Full.NET.ArchitectureTests;

/// <summary>架构测试：OrganizationHostUserBatchReadBoundary 约束与回归门禁。</summary>
[TestClass]
public sealed class OrganizationHostUserBatchReadBoundaryTests
{
    [TestMethod]
    public void Detail_queries_are_exempt_but_contract_list_composition_still_requires_batching()
    {
        var root = Path.Combine(Path.GetTempPath(), "fullnet-organization-query-boundary-" + Guid.NewGuid().ToString("N"));
        var directory = Path.Combine(root, "src", "Modules", "Full.NET.Modules.Organization");
        Directory.CreateDirectory(directory);
        var file = Path.Combine(directory, "FixtureQueryService.cs");
        try
        {
            File.WriteAllText(file, "class Fixture(IHostUserDisplayDirectory users) { public async Task<object> GetByIdAsync() { return await users.FindHostUsersAsync(ids); } }");
            Assert.IsEmpty(OrganizationHostUserBatchReadScanner.ScanOrganizationListCompositionViolations(root));
            File.WriteAllText(file, """
                class Fixture(IHostUserDisplayDirectory users) {
                    public async Task<object> ListAsync() { return rows; }
                }
                """);
            Assert.HasCount(1, OrganizationHostUserBatchReadScanner.ScanOrganizationListCompositionViolations(root));
            File.WriteAllText(file, """
                class Fixture(IHostUserDisplayDirectory users) {
                    public async Task<object> ListAsync() { return await users.FindHostUsersAsync(ids); }
                }
                """);
            Assert.IsEmpty(OrganizationHostUserBatchReadScanner.ScanOrganizationListCompositionViolations(root));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [TestMethod]
    public void Organization_list_queries_batch_host_users()
    {
        var root = ArchitectureRepositoryRoot.Find();
        var violations = OrganizationHostUserBatchReadScanner.ScanOrganizationListCompositionViolations(root);
        Assert.HasCount(0, violations, string.Join(Environment.NewLine, violations));
    }

    [TestMethod]
    public void Organization_list_queries_reject_per_row_active_user_fixture()
    {
        const string violatingFixture = """
            {
                foreach (var row in rows)
                {
                    await hostUserDirectory.FindActiveHostUserAsync(row.UserId, cancellationToken);
                }
            }
            """;

        Assert.IsTrue(
            OrganizationHostUserBatchReadScanner.ContainsPerRowActiveUserLookup(violatingFixture));
    }
}
