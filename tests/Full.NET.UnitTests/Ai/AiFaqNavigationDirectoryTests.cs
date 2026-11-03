using Full.NET.Abstractions.Ids;
using Full.NET.Abstractions.Time;
using Full.NET.Data.Abstractions;
using Full.NET.Modules.Ai;
using Full.NET.Modules.Ai.Contracts;
using Full.NET.Modules.Identity;
using Full.NET.Modules.Identity.Authorization;
using Full.NET.Modules.Identity.Contracts;
using Full.NET.Modules.Identity.Features.GetNavigation;
using Full.NET.Modules.Identity.Features.ManageHostMenus;
using Full.NET.Modules.Identity.Persistence;
using NSubstitute;

namespace Full.NET.UnitTests.Ai;

/// <summary>真实导航同步与投影必须允许仅有 FAQ 读取权限的审核者进入，且不暴露模型配置。</summary>
[TestClass]
public sealed class AiFaqNavigationDirectoryTests
{
    [TestMethod]
    [DataRow(true)]
    [DataRow(false)]
    public async Task Faq_reviewer_navigation_preserves_leaf_permissions_without_model_read(bool host)
    {
        var catalog = AuthorizationCatalog.Create([new IdentityAuthorizationContributor(), new AiAuthorizationContributor()]);
        var query = Substitute.For<IQueryExecutor>(); var command = Substitute.For<ICommandExecutor>();
        var ids = Substitute.For<IIdGenerator>(); ids.NewId().Returns(_ => Guid.CreateVersion7());
        var inserted = new List<InsertIdentityNavigation>();
        command.ExecuteAsync(Arg.Any<SqlStatement>(), Arg.Any<object?>(), Arg.Any<CancellationToken>()).Returns(call =>
        { if (call.ArgAt<object?>(1) is InsertIdentityNavigation menu) inserted.Add(menu); return 1; });
        await new HostNavigationCatalogSyncService(query, command, catalog, Substitute.For<IClock>(), ids).SyncMissingCatalogEntriesAsync();
        var directory = inserted.Single(item => item.RouteName == "module-ai");
        Assert.AreEqual(IdentityAuthorizationContributor.NavigationRead, directory.RequiredPermission);
        var routes = inserted.ToDictionary(item => item.Id, item => item.RouteName);
        var definitions = inserted.Where(item => item.RouteName == "module-ai" || item.RouteName.StartsWith("ai-", StringComparison.Ordinal))
            .Where(item => item.MenuType != "button").Select(item => new NavigationDefinition(item.RouteName,
                item.ParentId is { } parent ? routes[parent] : null, item.RouteName, item.Path, item.ComponentKey,
                item.Title, item.Caption, item.Icon, item.DisplayOrder, item.RequiredPermission, MenuType: item.MenuType)).ToArray();
        var projected = new NavigationProjector(catalog).Project([IdentityAuthorizationContributor.NavigationRead, AiKnowledgeFaqPermissions.Read], host, definitions);
        var ai = projected.Single(item => item.Id == "module-ai");
        CollectionAssert.AreEqual(new[] { "ai-knowledge-faqs" }, ai.Children.Select(item => item.Id).ToArray());
        Assert.IsEmpty(new NavigationProjector(catalog).Project([IdentityAuthorizationContributor.NavigationRead], host, definitions));
        await command.Received(1).ExecuteAsync(HostNavigationCatalogSyncService.RepairAiDirectoryPermission, Arg.Any<object?>(), Arg.Any<CancellationToken>());
    }
}
