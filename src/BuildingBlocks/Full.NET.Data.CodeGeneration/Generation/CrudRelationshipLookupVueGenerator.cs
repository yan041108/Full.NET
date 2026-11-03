using Full.NET.Data.CodeGeneration.Schema;

namespace Full.NET.Data.CodeGeneration.Generation;

/// <summary>关联字段使用带版本的异步候选状态，旧响应不能覆盖新搜索；控件不构造 SQL 或任意数据请求。</summary>
internal static class CrudRelationshipLookupVueGenerator
{
    internal static string Factory(FullNetCrudSchema schema) => "create" + string.Concat(schema.ApiResourceName.Split('-').Select(part => char.ToUpperInvariant(part[0]) + part[1..])) + "Api";
    internal static int Index(FullNetCrudSchema schema, FullNetColumn column) => Enumerable.Range(0, schema.Relationships.Count).FirstOrDefault(index => schema.Relationships[index].DependentColumnName == column.DatabaseName, -1);
    internal static string Imports(FullNetCrudSchema schema) => !CrudRelationshipLookupGenerator.Enabled(schema) ? string.Empty
        : $"import {{ {Factory(schema)}, type {schema.ClrTypeName}RelationOption }} from './{schema.ApiResourceName}.generated';";

    internal static string Model(FullNetCrudSchema schema)
    {
        if (!CrudRelationshipLookupGenerator.Enabled(schema)) return string.Empty;
        var root = schema.ClrTypeName;
        var owned = CrudOrganizationOwnershipGenerator.IsOrganizationUnitOwned(schema);
        var organizationProperty = owned ? schema.Columns.Single(column => column.DatabaseName == "OrganizationUnitId").JsonPropertyName : string.Empty;
        var methods = string.Join("\n", Enumerable.Range(0, 2).Select(index =>
        {
            var side = CrudRelationshipLookupGenerator.Side(index);
            var segment = CrudRelationshipLookupGenerator.Segment(index);
            var field = schema.Columns.Single(column => column.DatabaseName == schema.Relationships[index].DependentColumnName).JsonPropertyName;
            var label = CrudRelationshipLookupGenerator.Label(CrudRelationshipLookupGenerator.Principal(schema, index));
            return $$"""
            const {{segment}}Lookup = reactive({ options: [] as {{root}}RelationOption[], page: 0, total: 0, search: '', loading: false, revision: 0 });
            async function load{{side}}Options(search = '', append = false, selected?: string): Promise<void> {
              const state = {{segment}}Lookup;
              if (!session.can('{{schema.ReadPermission}}'){{(owned ? " || !lookupOrganizationUnit()" : string.Empty)}} || append && (state.loading || state.options.length >= 200 || state.page * 20 >= state.total)) return;
              {{(label is null ? "if (search && !/^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/i.test(search)) return;" : string.Empty)}}
              const revision = selected || append ? state.revision : ++state.revision;
              state.loading = true;
              try {
                const result = await relationApi.{{segment}}Options(search, append ? state.page + 1 : 1, selected{{(owned ? ", lookupOrganizationUnit()" : string.Empty)}});
                if (revision !== state.revision) return;
                const current = editOpen.value ? editForm.{{field}} : createForm.{{field}};
                const retained = state.options.filter(option => option.value === current);
                const merged = selected || append ? [...state.options, ...result.items] : [...retained, ...result.items];
                state.options = [...new Map(merged.map(option => [option.value, option])).values()].slice(0, 200);
                if (!selected) { state.page = result.page; state.total = result.total; state.search = search; }
              } catch (error: unknown) {
                if (revision === state.revision) problem.value = isFullNetProblemDetails(error) ? error
                  : { status: 500, code: 'client.{{schema.ModuleKey}}_relation_lookup_failed', title: t('generatedCrud.operationFailed') };
              } finally { if (revision === state.revision) state.loading = false; }
            }
            """;
        }));
        return $$"""
            const relationApi = {{Factory(schema)}}(http);
            {{(owned ? $$"""
            function lookupOrganizationUnit(): string | undefined {
              const value = editOpen.value ? editing.value?.{{organizationProperty}} : organizationUnitId.value;
              return value && /^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/i.test(value)
                && value !== '00000000-0000-0000-0000-000000000000' ? value : undefined;
            }
            """ : string.Empty)}}
            {{methods}}
            {{(owned ? $$"""
            watch(() => lookupOrganizationUnit(), () => {
              for (const state of [sourceLookup, targetLookup]) { state.revision++; state.loading = false; state.options = []; state.page = 0; state.total = 0; }
              {{string.Join("\n", schema.Relationships.Select(relation => "createForm." + schema.Columns.Single(column => column.DatabaseName == relation.DependentColumnName).JsonPropertyName + " = '';"))}}
            }, { flush: 'sync' });
            """ : string.Empty)}}
            """;
    }

    internal static string Hydrate(FullNetCrudSchema schema) => !CrudRelationshipLookupGenerator.Enabled(schema) ? string.Empty
        : string.Join("\n", Enumerable.Range(0, 2).Select(index => $"void load{CrudRelationshipLookupGenerator.Side(index)}Options('', false, item.{schema.Columns.Single(column => column.DatabaseName == schema.Relationships[index].DependentColumnName).JsonPropertyName} ?? undefined);"));

    internal static string Field(FullNetCrudSchema schema, FullNetColumn column, string form)
    {
        var index = Index(schema, column);
        var hasLabel = CrudRelationshipLookupGenerator.Label(CrudRelationshipLookupGenerator.Principal(schema, index)) is not null;
        var side = CrudRelationshipLookupGenerator.Side(index);
        var segment = CrudRelationshipLookupGenerator.Segment(index);
        var owned = CrudOrganizationOwnershipGenerator.IsOrganizationUnitOwned(schema);
        var label = $"t('generatedCrud.field', {{ name: '{column.DatabaseName}' }})";
        return $"<el-form-item :label=\"{label}\" prop=\"{column.JsonPropertyName}\"><el-select class=\"generated-crud-select\" v-model=\"{form}.{column.JsonPropertyName}\" filterable remote clearable"
            + $" @clear=\"{form}.{column.JsonPropertyName} = {(column.IsNullable ? "null" : "''")}\" :loading=\"{segment}Lookup.loading\" :remote-method=\"search => load{side}Options(search)\""
            + $" @visible-change=\"visible => visible && load{side}Options('')\" @popup-scroll=\"load{side}Options({segment}Lookup.search, true)\" :aria-label=\"{label}\""
            + (hasLabel ? string.Empty : " :placeholder=\"t('generatedCrud.relationUuid')\"")
            + $" :disabled=\"changing || !session.can('{schema.ReadPermission}'){(owned ? " || !lookupOrganizationUnit()" : string.Empty)}\">"
            + $"<el-option v-for=\"option in {segment}Lookup.options\" :key=\"option.value\" :value=\"option.value\" :label=\"option.label\" /></el-select>"
            + $"<small v-if=\"{segment}Lookup.options.length >= 200 && {segment}Lookup.total > 200\">{{{{ t('generatedCrud.relationLimit') }}}}</small></el-form-item>";
    }
}
