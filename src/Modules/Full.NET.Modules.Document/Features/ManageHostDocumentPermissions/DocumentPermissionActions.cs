namespace Full.NET.Modules.Document.Features.ManageHostDocumentPermissions;

/// <summary>动作集合按封闭机器码归一化；写、删、分享包含读取但互不提升。</summary>
internal static class DocumentPermissionActions
{
    internal static int Parse(IReadOnlyList<string>? actions)
    {
        if (actions is null || actions.Count is < 1 or > 4 || actions.Distinct(StringComparer.Ordinal).Count() != actions.Count) return 0;
        var mask = 0;
        foreach (var action in actions)
        {
            var flag = action switch { "read" => 1, "write" => 3, "delete" => 5, "share" => 9, _ => 0 };
            if (flag == 0) return 0;
            mask |= flag;
        }
        return mask;
    }
    internal static string[] ToNames(int mask) => new[] { "read", "write", "delete", "share" }
        .Where((_, index) => (mask & (1 << index)) != 0).ToArray();
}
