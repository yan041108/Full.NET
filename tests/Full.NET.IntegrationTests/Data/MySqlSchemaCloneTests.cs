using Dapper;
using MySqlConnector;

namespace Full.NET.IntegrationTests.Data;

[TestClass]
public sealed class MySqlSchemaCloneTests
{
    [TestMethod]
    public async Task MySql_schema_clone_preserves_foreign_key_trigger_and_independent_view()
    {
        var sourceCs = await SharedDatabaseFixture.CreateMySqlDatabaseAsync();
        var targetCs = await SharedDatabaseFixture.CreateMySqlDatabaseAsync();
        var sourceName = SharedDatabaseFixture.GetMySqlDatabaseName(sourceCs);
        var targetName = SharedDatabaseFixture.GetMySqlDatabaseName(targetCs);
        await using var source = new MySqlConnection(sourceCs);
        await source.ExecuteAsync("""
            CREATE TABLE CloneParent (Id int PRIMARY KEY);
            CREATE TABLE CloneChild (Id int PRIMARY KEY, ParentId int NOT NULL, Value int NOT NULL,
                CONSTRAINT FK_CloneChild_Parent FOREIGN KEY (ParentId) REFERENCES CloneParent (Id));
            INSERT INTO CloneParent (Id) VALUES (1);
            INSERT INTO CloneChild (Id, ParentId, Value) VALUES (1, 1, 7);
            CREATE VIEW CloneView AS SELECT Id, Value FROM CloneChild;
            """);
        await source.ExecuteAsync("""
            CREATE TRIGGER CloneGuard BEFORE INSERT ON CloneChild FOR EACH ROW
            BEGIN
                IF NEW.Value < 0 THEN SIGNAL SQLSTATE '45000' SET MESSAGE_TEXT = 'clone.negative'; END IF;
            END;
            """);
        await using var admin = new MySqlConnection(await SharedDatabaseFixture.GetMySqlRootConnectionStringAsync());
        await admin.OpenAsync();
        await ApiSchemaTemplate.CloneMySqlSchemaAsync(admin, sourceName, targetName, CancellationToken.None);
        await using var target = new MySqlConnection(targetCs);
        Assert.AreEqual(1, await target.ExecuteScalarAsync<int>("""
            SELECT COUNT(*) FROM information_schema.REFERENTIAL_CONSTRAINTS
            WHERE CONSTRAINT_SCHEMA = DATABASE() AND CONSTRAINT_NAME = 'FK_CloneChild_Parent'
                AND UNIQUE_CONSTRAINT_SCHEMA = DATABASE();
            """));
        var invalidParent = await Assert.ThrowsExactlyAsync<MySqlException>(() => target.ExecuteAsync(
            "INSERT INTO CloneChild (Id, ParentId, Value) VALUES (2, 999, 7);"));
        Assert.AreEqual("23000", invalidParent.SqlState);
        var invalidValue = await Assert.ThrowsExactlyAsync<MySqlException>(() => target.ExecuteAsync(
            "INSERT INTO CloneChild (Id, ParentId, Value) VALUES (2, 1, -1);"));
        Assert.AreEqual("45000", invalidValue.SqlState);
        await target.ExecuteAsync("INSERT INTO CloneChild (Id, ParentId, Value) VALUES (2, 1, 11);");
        Assert.AreEqual(2, await target.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM CloneView;"));
        Assert.AreEqual(1, await source.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM CloneView;"));
    }
}
