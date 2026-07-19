using Full.NET.Modules.Identity.Configuration;
using Full.NET.Modules.Identity.Seeding;
using Full.NET.Seeding.Abstractions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using NSubstitute;

namespace Full.NET.UnitTests.Identity;

[TestClass]
public sealed class E2eHostViewerSeedContributorTests
{
    private static readonly SeedContext Context = new(
        Guid.Parse("019822d3-0700-7000-8000-000000000203"),
        SeedProfile.Development,
        "Development",
        "zh-CN",
        "trace-e2e-host-viewer");

    [TestMethod]
    public async Task Missing_viewer_password_skips_without_database_writes()
    {
        var queryExecutor = Substitute.For<Full.NET.Data.Abstractions.IQueryExecutor>();
        var commandExecutor = Substitute.For<Full.NET.Data.Abstractions.ICommandExecutor>();
        var transaction = Substitute.For<Full.NET.Abstractions.Messaging.ICommandTransaction>();
        var contributor = CreateContributor(queryExecutor, commandExecutor, transaction, password: string.Empty);

        var result = await contributor.SeedAsync(Context);

        Assert.AreEqual(0, result.CreatedCount);
        Assert.AreEqual(0, result.UpdatedCount);
        Assert.AreEqual(1, result.SkippedCount);
        await queryExecutor.DidNotReceive().QuerySingleOrDefaultAsync<object>(
            Arg.Any<Full.NET.Data.Abstractions.SqlStatement>(),
            Arg.Any<object>(),
            Arg.Any<CancellationToken>());
    }

    private static E2eHostViewerSeedContributor CreateContributor(
        Full.NET.Data.Abstractions.IQueryExecutor queryExecutor,
        Full.NET.Data.Abstractions.ICommandExecutor commandExecutor,
        Full.NET.Abstractions.Messaging.ICommandTransaction transaction,
        string password)
    {
        var services = new ServiceCollection();
        services.AddSingleton(queryExecutor);
        services.AddSingleton(commandExecutor);
        services.AddSingleton(transaction);
        services.AddSingleton<
            Microsoft.AspNetCore.Identity.IPasswordHasher<Full.NET.Modules.Identity.Domain.IdentityUser>>(
            new Microsoft.AspNetCore.Identity.PasswordHasher<Full.NET.Modules.Identity.Domain.IdentityUser>());
        services.AddSingleton<Full.NET.Abstractions.Time.IClock>(
            _ => new Full.NET.Abstractions.Time.SystemClock());
        services.AddSingleton<Full.NET.Abstractions.Ids.IIdGenerator>(
            _ => new Full.NET.Abstractions.Ids.GuidV7IdGenerator());
        services.Configure<IdentityOptions>(options =>
        {
            options.E2eViewer.Username = "e2e-viewer";
            options.E2eViewer.Password = password;
            options.E2eViewer.DisplayName = "E2E 受限查看者";
        });
        services.AddSingleton<E2eHostViewerSeedContributor>();
        return services.BuildServiceProvider().GetRequiredService<E2eHostViewerSeedContributor>();
    }
}
