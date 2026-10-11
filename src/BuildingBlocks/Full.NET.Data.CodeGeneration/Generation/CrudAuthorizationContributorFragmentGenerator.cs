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
        // 租户数据权限归属于租户目录；其他数据作用域保留现有 Host 授权边界。
        var authorizationScope = schema.DataScope == FullNetCrudDataScope.TenantRequired
            ? "Tenant"
            : "Host";
        // 长度前缀保留模块与资源的边界，避免 a_b/c 与 a/b-c 生成同一导航键；旧版输出保持不变。
        var moduleRouteSegment = schema.ModuleKey.Replace('_', '-');
        var navigationKey = schema.UsesLegacyEntityCapabilities
            ? schema.ApiResourceName
            : $"m{moduleRouteSegment.Length}-{moduleRouteSegment}-{schema.ApiResourceName}";
        var permissions = schema.UsesLegacyEntityCapabilities
            ? $$"""
                new PermissionDefinition(
                    {{schema.ClrTypeName}}Permissions.Read,
                    "读取 {{schema.ClrTypeName}}",
                    AuthorizationScope.{{authorizationScope}}),
                new PermissionDefinition(
                    {{schema.ClrTypeName}}Permissions.Write,
                    "写入 {{schema.ClrTypeName}}",
                    AuthorizationScope.{{authorizationScope}}),
                """
            : $$"""
                new PermissionDefinition(
                    {{schema.ClrTypeName}}Permissions.Read,
                    "读取 {{schema.ClrTypeName}}",
                    AuthorizationScope.{{authorizationScope}}),
                new PermissionDefinition(
                    {{schema.ClrTypeName}}Permissions.Create,
                    "创建 {{schema.ClrTypeName}}",
                    AuthorizationScope.{{authorizationScope}}),
                new PermissionDefinition(
                    {{schema.ClrTypeName}}Permissions.Update,
                    "更新 {{schema.ClrTypeName}}",
                    AuthorizationScope.{{authorizationScope}}),
                new PermissionDefinition(
                    {{schema.ClrTypeName}}Permissions.Disable,
                    "停用 {{schema.ClrTypeName}}",
                    AuthorizationScope.{{authorizationScope}}),
                """;
        var actions = schema.UsesLegacyEntityCapabilities
            ? $$"""
                new AuthorizationActionDefinition(
                    "{{schema.ModuleKey}}.{{schema.PermissionResourceName}}.write",
                    "{{navigationKey}}",
                    {{schema.ClrTypeName}}Permissions.Write,
                    "写入",
                    "write",
                    10),
                """
            : $$"""
                new AuthorizationActionDefinition(
                    "{{schema.CreatePermission}}",
                    "{{navigationKey}}",
                    {{schema.ClrTypeName}}Permissions.Create,
                    "创建",
                    "create",
                    10),
                new AuthorizationActionDefinition(
                    "{{schema.UpdatePermission}}",
                    "{{navigationKey}}",
                    {{schema.ClrTypeName}}Permissions.Update,
                    "更新",
                    "update",
                    20),
                new AuthorizationActionDefinition(
                    "{{schema.DisablePermission}}",
                    "{{navigationKey}}",
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
                "{{navigationKey}}",
                null,
                "{{navigationKey}}",
                "/{{schema.ModuleKey.Replace('_', '-')}}/{{schema.ApiResourceName}}",
                "{{navigationKey}}",
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
