using Full.NET.Data.CodeGeneration.Schema;

namespace Full.NET.Data.CodeGeneration.Generation;

/// <summary>按显式 Schema 生成应用自有业务策略契约；不把业务状态机硬编码进通用 CRUD。</summary>
internal static class CrudMutationPolicyGenerator
{
    internal static string Contract(FullNetCrudSchema schema) => !schema.EntityCapabilities.HasMutationPolicy ? string.Empty : "\n" + $$"""
        /// <summary>在本模块命令事务内检查业务不变量；仅访问本模块数据，禁止跨模块调用及外部副作用。</summary>
        internal interface I{{schema.ClrTypeName}}MutationPolicy
        {
            Task<Error?> ValidateCreateAsync(Create{{schema.ClrTypeName}}Request request, Guid actorUserId, Guid? organizationUnitId, CancellationToken cancellationToken);
            Task<Error?> ValidateUpdateAsync(Guid id, Update{{schema.ClrTypeName}}Request request, Guid actorUserId, CancellationToken cancellationToken);
            Task<Error?> ValidateDeleteAsync(Guid id, Guid actorUserId, CancellationToken cancellationToken);
        }

        """;

    internal static string Call(FullNetCrudSchema schema, string operation, string idParameter = "")
    {
        if (!schema.EntityCapabilities.HasMutationPolicy) return string.Empty;
        var arguments = operation switch
        {
            "Create" => "request, actorUserId, " + (schema.EntityCapabilities.OwnershipMode == FullNetCrudOwnershipMode.OrganizationUnit ? "organizationUnitId" : "null"),
            "Update" => idParameter + ", request, actorUserId",
            "Delete" => idParameter + ", actorUserId",
            _ => throw new ArgumentOutOfRangeException(nameof(operation)),
        };
        return $$"""
            var mutationError = await mutationPolicy.Validate{{operation}}Async({{arguments}}, cancellationToken).ConfigureAwait(false);
            if (mutationError is not null) return Result<{{schema.ClrTypeName}}Response>.Failure(mutationError);

            """ + "\n";
    }
}
