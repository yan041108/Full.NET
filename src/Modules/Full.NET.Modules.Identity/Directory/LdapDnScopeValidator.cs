using System.Text;

namespace Full.NET.Modules.Identity.Directory;

/// <summary>LDAP DN 范围校验；确保子搜索根位于父 DN 允许范围内。</summary>
internal static class LdapDnScopeValidator
{
    /// <summary>
    /// 判断 <paramref name="candidateDn"/> 是否等于 <paramref name="rootDn"/>，
    /// 或为其子级（从目录树根部向右后缀匹配 RDN 组件）。
    /// </summary>
    /// <param name="candidateDn">待验证 DN。</param>
    /// <param name="rootDn">允许的根 DN。</param>
    /// <returns>是否在允许范围内。</returns>
    public static bool IsSameOrSubordinate(string? candidateDn, string? rootDn)
    {
        var candidateComponents = ParseComponents(candidateDn);
        var rootComponents = ParseComponents(rootDn);
        if (candidateComponents.Count == 0 || rootComponents.Count == 0)
        {
            return false;
        }

        if (candidateComponents.Count < rootComponents.Count)
        {
            return false;
        }

        for (var index = 0; index < rootComponents.Count; index++)
        {
            var candidateIndex = candidateComponents.Count - rootComponents.Count + index;
            if (!string.Equals(
                    candidateComponents[candidateIndex],
                    rootComponents[index],
                    StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }
        }

        return true;
    }

    private static List<string> ParseComponents(string? distinguishedName)
    {
        if (string.IsNullOrWhiteSpace(distinguishedName))
        {
            return [];
        }

        var components = new List<string>();
        var current = new StringBuilder();
        var escaped = false;
        foreach (var character in distinguishedName.Trim())
        {
            if (escaped)
            {
                current.Append(character);
                escaped = false;
                continue;
            }

            if (character == '\\')
            {
                escaped = true;
                continue;
            }

            if (character == ',')
            {
                AppendComponent(components, current);
                continue;
            }

            current.Append(character);
        }

        AppendComponent(components, current);
        return components;
    }

    private static void AppendComponent(List<string> components, StringBuilder current)
    {
        var value = current.ToString().Trim();
        current.Clear();
        if (value.Length > 0)
        {
            components.Add(value);
        }
    }
}
