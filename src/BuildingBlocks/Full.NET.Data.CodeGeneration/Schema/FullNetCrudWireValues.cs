namespace Full.NET.Data.CodeGeneration.Schema;

/// <summary>
/// 集中维护 CRUD Schema 与生成报告的稳定机器值，避免 CLR 枚举重命名隐式改变契约。
/// </summary>
internal static class FullNetCrudWireValues
{
    internal static string ToWireValue(FullNetCrudScene value) =>
        value switch
        {
            FullNetCrudScene.Single => "single",
            FullNetCrudScene.Tree => "tree",
            FullNetCrudScene.MasterDetail => "master.detail",
            FullNetCrudScene.ManyToMany => "many.to.many",
            _ => throw Unsupported(value),
        };

    internal static string ToWireValue(FullNetCrudDeleteMode value) =>
        value switch
        {
            FullNetCrudDeleteMode.HardDelete => "hard.delete",
            FullNetCrudDeleteMode.SoftDelete => "soft.delete",
            FullNetCrudDeleteMode.Immutable => "immutable",
            _ => throw Unsupported(value),
        };

    internal static string ToWireValue(FullNetCrudOwnershipMode value) =>
        value switch
        {
            FullNetCrudOwnershipMode.None => "none",
            FullNetCrudOwnershipMode.OrganizationUnit => "organization.unit",
            _ => throw Unsupported(value),
        };

    internal static string ToWireValue(FullNetCrudDataScope value) =>
        value switch
        {
            FullNetCrudDataScope.Unspecified => "unspecified",
            FullNetCrudDataScope.Global => "global",
            FullNetCrudDataScope.HostOnly => "host.only",
            FullNetCrudDataScope.TenantRequired => "tenant.required",
            _ => throw Unsupported(value),
        };

    internal static string ToWireValue(FullNetScalarType value) =>
        value switch
        {
            FullNetScalarType.Uuid => "uuid",
            FullNetScalarType.String => "string",
            FullNetScalarType.Int32 => "int32",
            FullNetScalarType.Int64 => "int64",
            FullNetScalarType.Boolean => "boolean",
            FullNetScalarType.DateTimeUtc => "date.time.utc",
            FullNetScalarType.Decimal => "decimal",
            _ => throw Unsupported(value),
        };

    internal static string ToWireValue(FullNetColumnControlKind value) =>
        value switch
        {
            FullNetColumnControlKind.Text => "text",
            FullNetColumnControlKind.Textarea => "textarea",
            FullNetColumnControlKind.Number => "number",
            FullNetColumnControlKind.Switch => "switch",
            FullNetColumnControlKind.DateTime => "datetime",
            FullNetColumnControlKind.Uuid => "uuid",
            _ => throw Unsupported(value),
        };

    internal static string ToWireValue(FullNetColumnQueryKind value) =>
        value switch
        {
            FullNetColumnQueryKind.None => "none",
            FullNetColumnQueryKind.Equals => "equals",
            FullNetColumnQueryKind.Contains => "contains",
            FullNetColumnQueryKind.Range => "range",
            _ => throw Unsupported(value),
        };

    internal static bool TryParse(
        string value,
        out FullNetCrudScene result) =>
        TryParse(
            value,
            [
                ("single", "Single", FullNetCrudScene.Single),
                ("tree", "Tree", FullNetCrudScene.Tree),
                ("master.detail", "MasterDetail", FullNetCrudScene.MasterDetail),
                ("many.to.many", "ManyToMany", FullNetCrudScene.ManyToMany),
            ],
            out result);

    internal static bool TryParse(
        string value,
        out FullNetCrudDeleteMode result) =>
        TryParse(
            value,
            [
                ("hard.delete", "HardDelete", FullNetCrudDeleteMode.HardDelete),
                ("soft.delete", "SoftDelete", FullNetCrudDeleteMode.SoftDelete),
                ("immutable", "Immutable", FullNetCrudDeleteMode.Immutable),
            ],
            out result);

    internal static bool TryParse(
        string value,
        out FullNetCrudOwnershipMode result) =>
        TryParse(
            value,
            [
                ("none", "None", FullNetCrudOwnershipMode.None),
                (
                    "organization.unit",
                    "OrganizationUnit",
                    FullNetCrudOwnershipMode.OrganizationUnit),
            ],
            out result);

    internal static bool TryParse(
        string value,
        out FullNetCrudDataScope result) =>
        TryParse(
            value,
            [
                ("unspecified", "Unspecified", FullNetCrudDataScope.Unspecified),
                ("global", "Global", FullNetCrudDataScope.Global),
                ("host.only", "HostOnly", FullNetCrudDataScope.HostOnly),
                (
                    "tenant.required",
                    "TenantRequired",
                    FullNetCrudDataScope.TenantRequired),
            ],
            out result);

    internal static bool TryParse(
        string value,
        out FullNetScalarType result) =>
        TryParse(
            value,
            [
                ("uuid", "Uuid", FullNetScalarType.Uuid),
                ("string", "String", FullNetScalarType.String),
                ("int32", "Int32", FullNetScalarType.Int32),
                ("int64", "Int64", FullNetScalarType.Int64),
                ("boolean", "Boolean", FullNetScalarType.Boolean),
                (
                    "date.time.utc",
                    "DateTimeUtc",
                    FullNetScalarType.DateTimeUtc),
                ("decimal", "Decimal", FullNetScalarType.Decimal),
            ],
            out result);

    internal static bool TryParse(
        string value,
        out FullNetColumnControlKind result) =>
        TryParse(
            value,
            [
                ("text", "Text", FullNetColumnControlKind.Text),
                ("textarea", "Textarea", FullNetColumnControlKind.Textarea),
                ("number", "Number", FullNetColumnControlKind.Number),
                ("switch", "Switch", FullNetColumnControlKind.Switch),
                ("datetime", "DateTime", FullNetColumnControlKind.DateTime),
                ("uuid", "Uuid", FullNetColumnControlKind.Uuid),
            ],
            out result);

    internal static bool TryParse(
        string value,
        out FullNetColumnQueryKind result) =>
        TryParse(
            value,
            [
                ("none", "None", FullNetColumnQueryKind.None),
                ("equals", "Equals", FullNetColumnQueryKind.Equals),
                ("contains", "Contains", FullNetColumnQueryKind.Contains),
                ("range", "Range", FullNetColumnQueryKind.Range),
            ],
            out result);

    private static bool TryParse<T>(
        string value,
        IReadOnlyList<(string WireValue, string LegacyAlias, T Value)> values,
        out T result)
        where T : struct, Enum
    {
        foreach (var candidate in values)
        {
            if (string.Equals(
                    value,
                    candidate.WireValue,
                    StringComparison.Ordinal)
                || string.Equals(
                    value,
                    candidate.LegacyAlias,
                    StringComparison.Ordinal))
            {
                result = candidate.Value;
                return true;
            }
        }

        result = default;
        return false;
    }

    private static ArgumentOutOfRangeException Unsupported<T>(T value)
        where T : struct, Enum =>
        new(
            nameof(value),
            value,
            "CRUD 稳定机器值不支持该枚举成员。");
}
