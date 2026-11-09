using Full.NET.Data.Abstractions;
using Full.NET.IntegrationTests.Api;

namespace Full.NET.IntegrationTests.Notifications;

/// <summary>独立验证纯文本草稿、字段错误与已发布模板的实际持久化。</summary>
[TestClass]
public sealed class NotificationTemplateDraftApiTests
{
    [TestMethod]
    [DataRow(DatabaseProvider.SqlServer)]
    [DataRow(DatabaseProvider.MySql)]
    public async Task Static_template_is_published_and_unknown_body_placeholder_names_field(DatabaseProvider provider)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(3));
        var cancellationToken = timeout.Token;
        var database = provider == DatabaseProvider.MySql
            ? await SharedDatabaseFixture.CreateMySqlDatabaseAsync()
            : await SharedDatabaseFixture.CreateSqlServerDatabaseAsync();
        using var factory = new FullNetApiFactory(provider, database);
        await factory.InitializeAsync(cancellationToken, useSchemaTemplate: false);
        using var client = factory.CreateClientForHost("localhost");
        var token = await NotificationProfileBindingAssertions.LoginAsHostAdminAsync(client, cancellationToken);
        await NotificationTemplateIntentAssertions.VerifyParameterlessDraftAsync(client, token, cancellationToken);
    }
}
