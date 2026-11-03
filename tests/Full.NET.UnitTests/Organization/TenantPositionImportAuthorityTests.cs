using System.Globalization;
using Full.NET.Abstractions.Ids;
using Full.NET.Abstractions.Messaging;
using Full.NET.Abstractions.Tenancy;
using Full.NET.Abstractions.Time;
using Full.NET.Data.Abstractions;
using Full.NET.Modules.Identity.Contracts;
using Full.NET.Modules.ImportExport.Contracts;
using Full.NET.Modules.Organization.Contracts;
using Full.NET.Modules.Organization.Features.ImportExport;
using Full.NET.Modules.Organization.Features.ManageTenantPositions;
using Full.NET.Modules.Reporting.Contracts;
using Full.NET.Modules.Reporting.Features.RenderWorkbooks;
using Microsoft.Extensions.Options;
using NSubstitute;

namespace Full.NET.UnitTests.Organization;

/// <summary>来源及已使用字段撤权在实际行事务之前生效，行回执按当前文化生成。</summary>
[TestClass]
public sealed class TenantPositionImportAuthorityTests
{
    [TestMethod]
    [DataRow(false, "zh-CN")]
    [DataRow(true, "en-US")]
    public async Task Live_row_authority_rejects_revocation_without_queries_or_writes(bool usesUnit, string culture)
    {
        var previous = CultureInfo.CurrentUICulture;
        try
        {
            CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo(culture);
            var tenant = new CurrentTenantAccessor(); tenant.SetTenant(new(Guid.CreateVersion7(), "test", "Test"));
            var queries = Substitute.For<IQueryExecutor>(); var commands = Substitute.For<ICommandExecutor>();
            var transaction = Substitute.For<ICommandTransaction>(); var sessions = Substitute.For<IBackgroundSessionAuthorization>();
            var management = new TenantPositionManagementService(queries, commands, transaction,
                new TenantPositionQueryService(queries, Options.Create(new DatabaseOptions())), tenant, Substitute.For<IClock>(), Substitute.For<IIdGenerator>());
            var handler = new TenantPositionsStaticImportSchemaHandler(new TenantPositionImportPreviewService(), management, sessions, tenant);
            var headers = handler.GetDefinition().Worksheets[0].HeaderColumns;
            var values = new string?[] { "engineer", "Engineer", "0", usesUnit ? "sales" : null, null };
            var workbook = new ReportingWorkbookRenderer().Render(headers.Select(header => new ReportingExecutionColumnDefinition(header, header)).ToArray(),
                [new ReportingExecutionRow(headers.Select((header, index) => (header, value: values[index])).ToDictionary(pair => pair.header, pair => pair.value))]).Value!;
            var user = Guid.CreateVersion7(); var context = new StaticImportPreviewContext(user, new Dictionary<string, bool>
            { [OrganizationPositionManagementPermissions.AssignUnit] = true, [OrganizationUnitManagementPermissions.Read] = true })
            { TaskId = Guid.CreateVersion7(), SourceFileId = Guid.CreateVersion7(), SessionBinding = new(user, tenant.Id, Guid.CreateVersion7(), "stamp", "host", $"tenant:{tenant.Id:N}") };
            using var input = new MemoryStream(workbook);
            var result = await handler.ExecuteBatchAsync(input, workbook.Length, 0, 10, context);
            Assert.IsTrue(result.IsSuccess); var row = result.Value!.Rows.Single();
            Assert.AreEqual(2, row.LineNumber); Assert.AreEqual(ImportTaskAuthorityErrorCodes.Denied, row.ErrorCode);
            StringAssert.Contains(row.Message!, culture == "zh-CN" ? "导入会话" : "import session");
            await sessions.Received(1).AuthorizeAllAsync(context.SessionBinding,
                Arg.Is<IReadOnlyCollection<string>>(permissions => permissions != null && permissions.Contains(OrganizationPositionManagementPermissions.Import)
                    && permissions.Contains(ImportExportPermissions.ImportTasksExecute)
                    && permissions.Contains(OrganizationPositionManagementPermissions.AssignUnit) == usesUnit
                    && permissions.Contains(OrganizationUnitManagementPermissions.Read) == usesUnit), default);
            Assert.IsFalse(queries.ReceivedCalls().Any()); Assert.IsFalse(commands.ReceivedCalls().Any()); Assert.IsFalse(transaction.ReceivedCalls().Any());
        }
        finally { CultureInfo.CurrentUICulture = previous; }
    }
}
