using System.Text.RegularExpressions;

namespace Full.NET.ArchitectureTests;

[TestClass]
public sealed partial class NotificationsPlatformBoundaryTests
{
    private static readonly string[] ExpectedTables =
    [
        "fn_notifications_binding",
        "fn_notifications_binding_version",
        "fn_notifications_delivery",
        "fn_notifications_delivery_attempt",
        "fn_notifications_domain_audit",
        "fn_notifications_intent",
        "fn_notifications_preference",
        "fn_notifications_provider_profile",
        "fn_notifications_provider_profile_version",
        "fn_notifications_receipt",
        "fn_notifications_recipient",
        "fn_notifications_recipient_endpoint",
        "fn_notifications_template",
        "fn_notifications_template_version",
    ];

    [TestMethod]
    public void Platform_104_migrations_publish_equivalent_owned_table_contracts()
    {
        var root = ArchitectureRepositoryRoot.Find();
        var sqlServer = ReadMigration(root, "SqlServer");
        var mySql = ReadMigration(root, "MySql");

        CollectionAssert.AreEqual(ExpectedTables, ReadTables(sqlServer));
        CollectionAssert.AreEqual(ExpectedTables, ReadTables(mySql));
        StringAssert.Contains(sqlServer, "uniqueidentifier");
        StringAssert.Contains(mySql, "BINARY(16)");
        StringAssert.Contains(sqlServer, "UX_fn_notifications_intent_Idempotency");
        StringAssert.Contains(mySql, "UX_fn_notifications_intent_Idempotency");
        StringAssert.Contains(sqlServer, "UX_fn_notifications_receipt_Idempotency");
        StringAssert.Contains(sqlServer, "TR_fn_notifications_template_version_Immutable");
        StringAssert.Contains(mySql, "TR_fn_notifications_template_version_Immutable");
        StringAssert.Contains(sqlServer, "PRIMARY KEY NONCLUSTERED (Id)");
        Assert.IsFalse(sqlServer.Contains("ON CONFLICT", StringComparison.OrdinalIgnoreCase));
        Assert.IsFalse(mySql.Contains("ON CONFLICT", StringComparison.OrdinalIgnoreCase));
    }

    [TestMethod]
    public void Platform_104_migrations_do_not_reference_foreign_module_tables()
    {
        var root = ArchitectureRepositoryRoot.Find();
        var sql = ReadMigration(root, "SqlServer") + ReadMigration(root, "MySql");
        var foreignTables = TableRegex().Matches(sql)
            .Select(match => match.Value)
            .Where(table => !table.StartsWith("fn_notifications_", StringComparison.Ordinal))
            .Distinct(StringComparer.Ordinal)
            .ToArray();

        Assert.IsEmpty(
            foreignTables,
            "Notifications 平台迁移禁止创建或引用其他模块的数据表。" + string.Join(',', foreignTables));
    }

    private static string ReadMigration(string root, string provider) =>
        File.ReadAllText(Path.Combine(
            root,
            "src",
            "BuildingBlocks",
            "Full.NET.Migrations.DbUp",
            "Migrations",
            provider,
            "104_NotificationsPlatformExtension.sql"));

    private static string[] ReadTables(string sql) => TableRegex().Matches(sql)
        .Select(match => match.Value)
        .Where(table => table.StartsWith("fn_notifications_", StringComparison.Ordinal)
            && table is not "fn_notifications_announcement" and not "fn_notifications_inbox_message")
        .Distinct(StringComparer.Ordinal)
        .Order(StringComparer.Ordinal)
        .ToArray();

    [TestMethod]
    public void Platform_105_inbox_scope_migrations_publish_equivalent_intent_isolation_contracts()
    {
        var root = ArchitectureRepositoryRoot.Find();
        var sqlServer = File.ReadAllText(Path.Combine(
            root, "src", "BuildingBlocks", "Full.NET.Migrations.DbUp", "Migrations", "SqlServer",
            "105_NotificationsInboxScopeExtension.sql"));
        var mySql = File.ReadAllText(Path.Combine(
            root, "src", "BuildingBlocks", "Full.NET.Migrations.DbUp", "Migrations", "MySql",
            "105_NotificationsInboxScopeExtension.sql"));

        StringAssert.Contains(sqlServer, "ScopeKey");
        StringAssert.Contains(sqlServer, "TenantScopeKey");
        StringAssert.Contains(sqlServer, "IntentId");
        StringAssert.Contains(sqlServer, "UX_fn_notifications_inbox_Intent_Recipient");
        StringAssert.Contains(sqlServer, "WHERE IntentId IS NOT NULL");
        StringAssert.Contains(mySql, "UX_fn_notifications_inbox_Intent_Recipient");
        StringAssert.Contains(sqlServer, "CK_fn_notifications_endpoint_Verification");
        StringAssert.Contains(mySql, "CK_fn_notifications_endpoint_Verification");
        Assert.IsFalse(sqlServer.Contains("ON CONFLICT", StringComparison.OrdinalIgnoreCase));
        Assert.IsFalse(mySql.Contains("ON CONFLICT", StringComparison.OrdinalIgnoreCase));
    }

    [GeneratedRegex(@"\bfn_[a-z0-9]+_[a-z0-9_]+\b")]
    private static partial Regex TableRegex();

    [TestMethod]
    public void Production_module_does_not_ship_test_notification_provider()
    {
        var root = ArchitectureRepositoryRoot.Find();
        var moduleDirectory = Path.Combine(
            root,
            "src",
            "Modules",
            "Full.NET.Modules.Notifications");
        var offenders = Directory
            .EnumerateFiles(moduleDirectory, "*.cs", SearchOption.AllDirectories)
            .Where(path => !path.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.Ordinal))
            .Where(path => File.ReadAllText(path).Contains("class TestNotificationProvider", StringComparison.Ordinal))
            .Select(path => Path.GetRelativePath(root, path))
            .ToArray();

        Assert.IsEmpty(offenders, "测试 Provider 禁止进入生产 Notifications 程序集。");
    }

    [TestMethod]
    public void Delivery_hosted_processor_is_registered_only_on_worker_background_entry()
    {
        var root = ArchitectureRepositoryRoot.Find();
        var moduleSource = File.ReadAllText(Path.Combine(
            root,
            "src",
            "Modules",
            "Full.NET.Modules.Notifications",
            "NotificationsModule.cs"));
        var addServicesIndex = moduleSource.IndexOf("public void AddServices", StringComparison.Ordinal);
        var addBackgroundIndex = moduleSource.IndexOf(
            "public void AddBackgroundServices",
            StringComparison.Ordinal);
        Assert.IsTrue(addServicesIndex >= 0 && addBackgroundIndex > addServicesIndex);
        var addServicesBody = moduleSource[addServicesIndex..addBackgroundIndex];

        Assert.IsFalse(
            addServicesBody.Contains("AddHostedService", StringComparison.Ordinal),
            "API AddServices 禁止启动 Delivery HostedService。");
        Assert.IsFalse(
            addServicesBody.Contains("AddBackgroundServices(", StringComparison.Ordinal),
            "API AddServices 不得再调用 AddBackgroundServices，以免把领取循环带进 API。");
        StringAssert.Contains(moduleSource, "AddHostedService<NotificationDeliveryHostedProcessor>");
    }
}
