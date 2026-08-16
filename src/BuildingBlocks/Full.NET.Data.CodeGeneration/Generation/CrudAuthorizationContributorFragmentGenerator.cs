using System.Text;
using Full.NET.Data.CodeGeneration.Schema;

namespace Full.NET.Data.CodeGeneration.Generation;

/// <summary>
/// 生成可幂等插入目标模块 AuthorizationContributor 的权限/导航/操作片段。
/// </summary>
internal static class CrudAuthorizationContributorFragmentGenerator
{
    /// <summary>生成仅含集合元素的片段，禁止输出完整类型以免覆盖手写 Contributor。</summary>
    internal static string Generate(FullNetCrudSchema schema)
    {
        ArgumentNullException.ThrowIfNull(schema);
        var permissions = schema.UsesLegacyEntityCapabilities
            ? $$"""
                new PermissionDefinition(
                    {{schema.ClrTypeName}}Permissions.Read,
                    "读取 {{schema.ClrTypeName}}",
                    AuthorizationScope.Host),
                new PermissionDefinition(
                    {{schema.ClrTypeName}}Permissions.Write,
                    "写入 {{schema.ClrTypeName}}",
                    AuthorizationScope.Host),
                """
            : $$"""
                new PermissionDefinition(
                    {{schema.ClrTypeName}}Permissions.Read,
                    "读取 {{schema.ClrTypeName}}",
                    AuthorizationScope.Host),
                new PermissionDefinition(
                    {{schema.ClrTypeName}}Permissions.Create,
                    "创建 {{schema.ClrTypeName}}",
                    AuthorizationScope.Host),
                new PermissionDefinition(
                    {{schema.ClrTypeName}}Permissions.Update,
                    "更新 {{schema.ClrTypeName}}",
                    AuthorizationScope.Host),
                new PermissionDefinition(
                    {{schema.ClrTypeName}}Permissions.Disable,
                    "停用 {{schema.ClrTypeName}}",
                    AuthorizationScope.Host),
                """;
        var actions = schema.UsesLegacyEntityCapabilities
            ? $$"""
                new AuthorizationActionDefinition(
                    "{{schema.ModuleKey}}.{{schema.PermissionResourceName}}.write",
                    "{{schema.ApiResourceName}}",
                    {{schema.ClrTypeName}}Permissions.Write,
                    "写入",
                    "write",
                    10),
                """
            : $$"""
                new AuthorizationActionDefinition(
                    "{{schema.CreatePermission}}",
                    "{{schema.ApiResourceName}}",
                    {{schema.ClrTypeName}}Permissions.Create,
                    "创建",
                    "create",
                    10),
                new AuthorizationActionDefinition(
                    "{{schema.UpdatePermission}}",
                    "{{schema.ApiResourceName}}",
                    {{schema.ClrTypeName}}Permissions.Update,
                    "更新",
                    "update",
                    20),
                new AuthorizationActionDefinition(
                    "{{schema.DisablePermission}}",
                    "{{schema.ApiResourceName}}",
                    {{schema.ClrTypeName}}Permissions.Disable,
                    "停用",
                    "disable",
                    30),
                """;

        return Normalize(
            $$"""
            // <fullnet-generated {{schema.ModuleKey}}.{{schema.EntityKey}} permissions>
            {{permissions}}
            // </fullnet-generated {{schema.ModuleKey}}.{{schema.EntityKey}} permissions>

            // <fullnet-generated {{schema.ModuleKey}}.{{schema.EntityKey}} navigation>
            new NavigationDefinition(
                "{{schema.ApiResourceName}}",
                null,
                "{{schema.ApiResourceName}}",
                "/{{schema.ModuleKey.Replace('_', '-')}}/{{schema.ApiResourceName}}",
                "{{schema.ApiResourceName}}",
                "{{schema.ClrTypeName}}",
                "{{schema.ClrTypeName}}",
                "collection",
                80,
                {{schema.ClrTypeName}}Permissions.Read),
            // </fullnet-generated {{schema.ModuleKey}}.{{schema.EntityKey}} navigation>

            // <fullnet-generated {{schema.ModuleKey}}.{{schema.EntityKey}} actions>
            {{actions}}
            // </fullnet-generated {{schema.ModuleKey}}.{{schema.EntityKey}} actions>
            """);
    }

    private static string Normalize(string content)
    {
        var builder = new StringBuilder(content.Length + 1);
        builder.Append(content.Replace("\r\n", "\n", StringComparison.Ordinal)
            .TrimEnd('\r', '\n'));
        builder.Append('\n');
        return builder.ToString();
    }
}
