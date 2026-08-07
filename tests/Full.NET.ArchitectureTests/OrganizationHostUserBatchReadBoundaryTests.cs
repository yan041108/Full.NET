namespace Full.NET.ArchitectureTests;

[TestClass]
public sealed class OrganizationHostUserBatchReadBoundaryTests
{
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