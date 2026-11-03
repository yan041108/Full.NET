using System.Text.Json;
using System.Globalization;
using System.Resources;
using Full.NET.Modules.Organization.Serialization;
using Full.NET.Abstractions.Results;
using Full.NET.Abstractions.Tenancy;
using Full.NET.Modules.Identity.Contracts;
using Full.NET.Modules.ImportExport.Contracts;
using Full.NET.Modules.Organization.Contracts;
using Full.NET.Modules.Organization.Features.ManageTenantPositions;

namespace Full.NET.Modules.Organization.Features.ImportExport;

/// <summary>Organization 租户职位静态导入 Schema 处理器。</summary>
internal sealed class TenantPositionsStaticImportSchemaHandler(
    TenantPositionImportPreviewService previewService,
    TenantPositionManagementService managementService,
    IBackgroundSessionAuthorization sessions,
    ICurrentTenant tenant) : IStaticImportSchemaHandler
{
    internal const string PositionsWorksheetKey = "positions";
    private static readonly ResourceManager RowMessages = new("Full.NET.Modules.Organization.Resources.OrganizationErrors",
        typeof(TenantPositionsStaticImportSchemaHandler).Assembly);

    private static readonly string[] ImportHeaders =
    [
        "code",
        "name",
        "displayOrder",
        "unitCode",
        "positionLevelCode",
    ];

    public string SchemaKey => StaticImportSchemaKeys.OrganizationTenantPositions;

    public StaticImportSchemaDefinition GetDefinition() =>
        new(
            SchemaKey,
            "租户职位",
            StaticImportSchemaScopeKeys.Tenant,
            OrganizationPositionManagementPermissions.Import,
            [
                new StaticImportWorksheetDefinition(
                    PositionsWorksheetKey,
                    "职位",
                    ImportHeaders),
            ]);

    public byte[] CreateTemplate(string worksheetKey)
    {
        if (!string.Equals(worksheetKey, PositionsWorksheetKey, StringComparison.Ordinal))
        {
            throw new InvalidOperationException("Unsupported worksheet key.");
        }

        return OrganizationPositionWorkbookCodec.CreateImportTemplate();
    }

    public async Task<Result<StaticImportPreviewResult>> PreviewAsync(
        Stream content,
        long contentLength,
        StaticImportPreviewContext context,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var rows = await OrganizationPositionWorkbookCodec
                .ParseImportRowsAsync(content, contentLength, cancellationToken, context.ColumnMapping)
                .ConfigureAwait(false);
            var preview = previewService.Preview(rows.Select(row => row.Row).ToArray(), context);
            return Result<StaticImportPreviewResult>.Success(preview with
            {
                Rows = preview.Rows.Select(row => row with { LineNumber = rows[row.LineNumber - 1].LineNumber }).ToArray(),
            });
        }
        catch (StaticImportHeaderMappingException)
        {
            return InvalidMapping<StaticImportPreviewResult>();
        }
        catch (InvalidDataException)
        {
            return Result<StaticImportPreviewResult>.Failure(new Error(
                OrganizationErrorCodes.PositionImportWorkbookInvalid,
                "The organization position import workbook is invalid.",
                ErrorType.Validation));
        }
    }

    public async Task<Result<StaticImportBatchExecutionResult>> ExecuteBatchAsync(
        Stream content,
        long contentLength,
        int startLineNumber,
        int batchSize,
        StaticImportPreviewContext context,
        CancellationToken cancellationToken = default)
    {
        if (batchSize <= 0)
        {
            return Result<StaticImportBatchExecutionResult>.Failure(new Error(
                ValidationErrorCodes.Failed,
                "Batch size must be positive.",
                ErrorType.Validation));
        }

        try
        {
            if (context.TaskId is not Guid taskId || taskId == Guid.Empty)
                return Result<StaticImportBatchExecutionResult>.Failure(new Error(ValidationErrorCodes.Failed,
                    "Durable task identity is required for resumable import.", ErrorType.Validation));
            var capabilities = ResolveImportCapabilities(context);
            var serializer = OrganizationJsonSerializerContext.Default.TenantPositionImportPlan;
            TenantPositionImportPlan plan;
            string planJson;
            if (context.PreparedPlanJson is { } prepared)
            {
                if (prepared.Length > 1024 * 1024) throw new InvalidDataException("Import plan exceeds its budget.");
                plan = JsonSerializer.Deserialize(prepared, serializer)
                    ?? throw new InvalidDataException("Import plan is missing.");
                if (plan.Format != 1 || plan.TaskId != taskId || plan.SourceFileId != context.SourceFileId
                    || plan.AssignUnit != capabilities.CanAssignUnit || plan.AssignPositionLevel != capabilities.CanAssignPositionLevel
                    || plan.Rows.Count > 1000)
                    throw new InvalidDataException("Import plan binding is invalid.");
                planJson = prepared;
            }
            else
            {
                var rows = await OrganizationPositionWorkbookCodec.ParseImportRowsAsync(content, contentLength, cancellationToken, context.ColumnMapping)
                    .ConfigureAwait(false);
                var preview = previewService.Preview(rows.Select(row => row.Row).ToArray(), context);
                plan = new TenantPositionImportPlan(1, taskId, context.SourceFileId,
                    capabilities.CanAssignUnit, capabilities.CanAssignPositionLevel,
                    preview.Rows.Where(row => row.IsValid).OrderBy(row => row.LineNumber)
                        .Select(row => new TenantPositionImportPlanRow(rows[row.LineNumber - 1].LineNumber, rows[row.LineNumber - 1].Row)).ToArray());
                planJson = JsonSerializer.Serialize(plan, serializer);
                if (planJson.Length > 1024 * 1024) throw new InvalidDataException("Import plan exceeds its budget.");
            }
            var batchValidRows = plan.Rows.Skip(startLineNumber).Take(batchSize).ToArray();
            var batchRows = batchValidRows.Select(row => row.Row).ToArray();
            if (batchRows.Length == 0)
            {
                return Result<StaticImportBatchExecutionResult>.Success(
                    new StaticImportBatchExecutionResult([]) { PreparedPlanJson = planJson });
            }

            var executionRows = new StaticImportRowExecutionResult[batchRows.Length];
            for (var index = 0; index < batchRows.Length; index++)
            {
                var line = batchValidRows[index].LineNumber;
                // 能力快照只能收窄；每次实际写入前仍检查原会话和本行使用的字段权限。
                if (!await AuthorizeRowAsync(context, batchRows[index], cancellationToken).ConfigureAwait(false))
                {
                    executionRows[index] = new(line, false, null, ImportTaskAuthorityErrorCodes.Denied,
                        RowMessages.GetString(ImportTaskAuthorityErrorCodes.Denied, CultureInfo.CurrentUICulture)
                            ?? "The import session or required source permission is no longer valid.");
                    continue;
                }
                var imported = await managementService.ImportTaskRowAsync(taskId, line, batchRows[index],
                    capabilities, cancellationToken).ConfigureAwait(false);
                executionRows[index] = new StaticImportRowExecutionResult(line, imported.IsSuccess,
                    imported.IsSuccess ? imported.Value!.PositionId : null, imported.Error?.Code, imported.Error?.Message);
            }
            return Result<StaticImportBatchExecutionResult>.Success(
                new StaticImportBatchExecutionResult(executionRows) { PreparedPlanJson = planJson });
        }
        catch (StaticImportHeaderMappingException)
        {
            return InvalidMapping<StaticImportBatchExecutionResult>();
        }
        catch (InvalidDataException)
        {
            return Result<StaticImportBatchExecutionResult>.Failure(new Error(
                OrganizationErrorCodes.PositionImportWorkbookInvalid,
                "The organization position import workbook is invalid.",
                ErrorType.Validation));
        }
    }

    private async Task<bool> AuthorizeRowAsync(StaticImportPreviewContext context, ImportOrganizationPositionRow row, CancellationToken token)
    {
        var binding = context.SessionBinding;
        if (binding is null || !tenant.IsAvailable || tenant.IsHost || tenant.Id is null
            || binding.UserId != context.RequestedByUserId || binding.TenantId != tenant.Id) return false;
        var required = new List<string> { ImportExportPermissions.ImportTasksExecute, OrganizationPositionManagementPermissions.Import };
        if (!string.IsNullOrWhiteSpace(row.UnitCode))
            required.AddRange([OrganizationPositionManagementPermissions.AssignUnit, OrganizationUnitManagementPermissions.Read]);
        if (!string.IsNullOrWhiteSpace(row.PositionLevelCode))
            required.AddRange([OrganizationPositionManagementPermissions.AssignPositionLevel, OrganizationPositionLevelManagementPermissions.Read]);
        var actor = await sessions.AuthorizeAllAsync(binding, required, token).ConfigureAwait(false);
        return actor is not null && actor.UserId == binding.UserId && actor.TenantId == tenant.Id;
    }

    private static Result<T> InvalidMapping<T>() => Result<T>.Failure(new Error(ImportColumnMappingErrorCodes.Invalid,
        "The import header mapping does not match this workbook.", ErrorType.Validation));

    private static OrganizationPositionImportCapabilities ResolveImportCapabilities(
        StaticImportPreviewContext context)
    {
        return new OrganizationPositionImportCapabilities(
            HasCapability(context, OrganizationPositionManagementPermissions.AssignUnit)
            && HasCapability(context, OrganizationUnitManagementPermissions.Read),
            HasCapability(context, OrganizationPositionManagementPermissions.AssignPositionLevel)
            && HasCapability(context, OrganizationPositionLevelManagementPermissions.Read));
    }

    private static bool HasCapability(StaticImportPreviewContext context, string permissionCode) =>
        context.CapabilityFlags.TryGetValue(permissionCode, out var allowed) && allowed;
}

/// <summary>只持久化解析和纯校验结果；每行写入仍重新检查当前领域约束。</summary>
internal sealed record TenantPositionImportPlan(int Format, Guid TaskId, Guid? SourceFileId,
    bool AssignUnit, bool AssignPositionLevel, IReadOnlyList<TenantPositionImportPlanRow> Rows);

/// <summary>保留原始工作簿行号，用于恢复幂等。</summary>
internal sealed record TenantPositionImportPlanRow(int LineNumber, ImportOrganizationPositionRow Row);
