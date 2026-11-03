using System.Globalization;
using System.Security.Cryptography;
using System.Text.Json;
using Full.NET.Abstractions.Results;
using Full.NET.Modules.EnterpriseRequest.Generated;
using Full.NET.Modules.Identity.Contracts;

namespace Full.NET.Modules.EnterpriseRequest.Features.Delivery;

/// <summary>只有获授权字段才进入交付模型；版本也进入摘要，金额不可通过缓存或空值占位泄漏。</summary>
internal sealed record EnterpriseDeliverySnapshot(Guid Id, long Version, string RequestNumber, string Title,
    string Status, string? TotalAmount, bool IncludesAmounts, IReadOnlyList<EnterpriseDeliveryLine> Lines)
{
    internal string Digest() => Convert.ToHexStringLower(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(this,
        EnterpriseRequestDeliveryJsonContext.Default.EnterpriseDeliverySnapshot)));
}

/// <summary>行身份和版本用于拒绝生成后变动，明细金额仅在精确字段授权后进入投影。</summary>
internal sealed record EnterpriseDeliveryLine(Guid Id, long Version, int LineNumber, string Description,
    string Quantity, string? UnitPrice, string? LineAmount);

/// <summary>同一单据及明细连续两次核对，避免翻页、行编辑或范围变更产生静默混合快照。</summary>
internal sealed class EnterpriseRequestDeliverySnapshotReader(EnterpriseRequestDeliveryAccess access,
    EnterpriseRequestDetailQueryService details)
{
    internal async Task<Result<EnterpriseDeliverySnapshot>> ReadAsync(Guid id, SessionBindingSnapshot binding,
        IReadOnlyCollection<string> permissions, long? expectedVersion, bool? amounts, CancellationToken token)
    {
        var required = permissions.Concat([EnterpriseRequestPermissions.Read, EnterpriseRequestDetailPermissions.Read]).Distinct().ToArray();
        var first = await ReadOnceAsync(id, binding, required, expectedVersion, amounts, token).ConfigureAwait(false);
        if (!first.IsSuccess) return first;
        var second = await ReadOnceAsync(id, binding, required, expectedVersion, first.Value!.IncludesAmounts, token).ConfigureAwait(false);
        if (!second.IsSuccess) return second;
        return first.Value.Digest() == second.Value!.Digest() ? second : Failure("enterprise_request.delivery_version_conflict");
    }

    private async Task<Result<EnterpriseDeliverySnapshot>> ReadOnceAsync(Guid id, SessionBindingSnapshot binding,
        IReadOnlyCollection<string> permissions, long? expectedVersion, bool? amounts, CancellationToken token)
    {
        var parent = await access.ReadAsync(id, binding, permissions, token).ConfigureAwait(false);
        if (!parent.IsSuccess) return Result<EnterpriseDeliverySnapshot>.Failure(parent.Error!);
        if (expectedVersion is { } version && parent.Value!.Version != version) return Failure("enterprise_request.delivery_version_conflict");
        var granted = await access.AmountsAsync(binding, token).ConfigureAwait(false);
        if (amounts == true && !granted) return Failure("enterprise_request.delivery_forbidden");
        var include = amounts ?? granted;
        var lines = new List<EnterpriseDeliveryLine>();
        for (var page = 1; page <= 5; page++)
        {
            var result = await details.ListAsync(binding.UserId, false, page, 100, token,
                new EnterpriseRequestDetailListQuery { FilterRequestId = id }).ConfigureAwait(false);
            if (!result.IsSuccess) return Result<EnterpriseDeliverySnapshot>.Failure(result.Error!);
            if (result.Value!.Total > 500) return Failure("enterprise_request.delivery_limit_exceeded");
            foreach (var row in result.Value.Items)
            {
                if (row.RequestId != id || row.TenantId != binding.TenantId) return Failure("enterprise_request.delivery_forbidden");
                lines.Add(new(row.Id, row.Version, row.LineNumber, row.ItemDescription, Number(row.Quantity),
                    include ? Number(row.UnitPrice) : null, include ? Number(row.LineAmount) : null));
            }
            if (lines.Count >= result.Value.Total) break;
        }
        if (lines.Select(line => line.Id).Distinct().Count() != lines.Count) return Failure("enterprise_request.delivery_version_conflict");
        var request = parent.Value!;
        return Result<EnterpriseDeliverySnapshot>.Success(new(request.Id, request.Version, request.RequestNumber,
            request.Title, request.Status, include ? Number(request.TotalAmount) : null, include,
            lines.OrderBy(line => line.LineNumber).ThenBy(line => line.Id).ToArray()));
    }

    private static string Number(decimal value) => value.ToString(CultureInfo.InvariantCulture);
    private static Result<EnterpriseDeliverySnapshot> Failure(string code) => Result<EnterpriseDeliverySnapshot>.Failure(
        new Error(code, "The authorized document changed or exceeded its delivery budget.",
            code == "enterprise_request.delivery_forbidden" ? ErrorType.Forbidden : ErrorType.Conflict));
}
