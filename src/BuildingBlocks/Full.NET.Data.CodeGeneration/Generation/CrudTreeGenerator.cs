using Full.NET.Data.CodeGeneration.Naming;
using Full.NET.Data.CodeGeneration.Schema;

namespace Full.NET.Data.CodeGeneration.Generation;

/// <summary>树结构的同租户写入串行化随本地事务释放，避免不同节点分别通过检查后形成环。</summary>
internal static class CrudTreeGenerator
{
    internal static string VueParentField(FullNetCrudSchema schema, string form)
    {
        var parent = schema.Columns.Single(column => column.DatabaseName == "ParentId").JsonPropertyName;
        return $"<el-form-item :label=\"t('generatedCrud.parent')\" prop=\"{parent}\"><el-select class=\"generated-crud-select\" v-model=\"{form}.{parent}\" filterable clearable @clear=\"{form}.{parent} = null\" :aria-label=\"t('generatedCrud.parent')\"><el-option v-for=\"choice in treeParentChoices({(form == "editForm" ? "editing?." + schema.Columns.Single(column => column.DatabaseName == "Id").JsonPropertyName : string.Empty)})\" :key=\"choice.id\" :value=\"choice.id\" :label=\"choice.label\" /></el-select></el-form-item>";
    }

    internal static string VueModel(FullNetCrudSchema schema)
    {
        var id = schema.Columns.Single(column => column.DatabaseName == "Id").JsonPropertyName;
        var parent = schema.Columns.Single(column => column.DatabaseName == "ParentId").JsonPropertyName;
        var label = schema.Columns.FirstOrDefault(column => column.ScalarType == FullNetScalarType.String)?.JsonPropertyName ?? id;
        return $$"""
            type TreeRow = {{schema.ClrTypeName}}Response & { children: TreeRow[] };
            function validTreeAncestor(item: {{schema.ClrTypeName}}Response, excludedId?: string): boolean {
              let node: {{schema.ClrTypeName}}Response | undefined = item;
              const seen = new Set<string>();
              for (let depth = 0; node && depth < 33; depth++) {
                if (node.{{id}} === excludedId || seen.has(node.{{id}})) return false;
                seen.add(node.{{id}});
                if (!node.{{parent}}) return true;
                const parentId: string = node.{{parent}};
                node = items.value.find(candidate => candidate.{{id}} === parentId);
              }
              return node === undefined;
            }
            const treeModel = computed(() => {
              const nodes = new Map<string, TreeRow>(items.value.map(item => [item.{{id}}, { ...item, children: [] }]));
              const roots: TreeRow[] = []; let invalid = false;
              for (const node of nodes.values()) {
                const valid = validTreeAncestor(node); invalid ||= !valid;
                const parent = node.{{parent}} ? nodes.get(node.{{parent}}) : undefined;
                if (valid && parent) parent.children.push(node); else roots.push(node);
              }
              return { rows: roots, invalid };
            });
            function treeParentChoices(excludedId?: string) {
              return items.value.filter(item => validTreeAncestor(item, excludedId))
                .map(item => ({ id: item.{{id}}, label: String(item.{{label}} ?? item.{{id}}) }));
            }
            """;
    }

    internal static string VueLoad(FullNetCrudSchema schema) => $$"""
                  // 树显示完整有界结果，不能把普通分页的单页冒充整棵树。
                  if (result.total > 1000) throw { status: 409, code: '{{schema.ModuleKey}}.{{schema.PermissionResourceName}}.tree_limit', title: 'Narrow the tree filter to at most 1000 nodes.' };
                  const rows = [...result.items];
                  for (let next = 2; rows.length < result.total; next++) {
                    const more = await api.list(next, 100, frozenQuery);
                    if (more.total !== result.total || more.items.length === 0 || rows.length + more.items.length > 1000)
                      throw { status: 409, code: '{{schema.ModuleKey}}.{{schema.PermissionResourceName}}.tree_changed', title: 'The tree changed while loading. Reload it.' };
                    rows.push(...more.items);
                  }
                  if (new Set(rows.map(item => item.{{schema.Columns.Single(column => column.DatabaseName == "Id").JsonPropertyName}})).size !== rows.length)
                    throw { status: 409, code: '{{schema.ModuleKey}}.{{schema.PermissionResourceName}}.tree_changed', title: 'The tree changed while loading. Reload it.' };
        """;

    internal static string Table(FullNetCrudSchema schema)
    {
        var name = schema.DatabaseTableName + "_tree_guard";
        if (name.Length > 64) throw new ArgumentException("Tree 实体须为 _tree_guard 辅助表保留共同标识符长度，禁止静默截断表名。");
        return name;
    }

    internal static string Migration(FullNetCrudSchema schema, bool mysql)
    {
        if (schema.Scene != FullNetCrudScene.Tree) return string.Empty;
        var table = Table(schema);
        var primary = DatabaseObjectNameBuilder.Build("PK_" + table);
        var tenant = schema.IsTenantScoped;
        var unique = DatabaseObjectNameBuilder.Build("UX_" + table + "_TenantId");
        return mysql
            ? $"\n-- 同一实体租户的一行写锁；不能通过清理任务删除活动租户的锁行。\nCREATE TABLE IF NOT EXISTS {table}\n(\n    Id binary(16) NOT NULL COMMENT '应用 UUID v7',\n"
                + (tenant ? $"    TenantId binary(16) NOT NULL COMMENT '可信租户',\n    CONSTRAINT {unique} UNIQUE (TenantId),\n" : string.Empty)
                + $"    CONSTRAINT {primary} PRIMARY KEY (Id)\n) ENGINE=InnoDB COMMENT='树写入事务互斥';\n"
            : $"\n-- 同一实体租户的一行写锁；创建和使用均在本地事务内，不依赖会话级命名锁。\nIF OBJECT_ID(N'dbo.{table}', N'U') IS NULL\nBEGIN\n    CREATE TABLE dbo.{table}\n    (\n        Id uniqueidentifier NOT NULL,\n"
                + (tenant ? $"        TenantId uniqueidentifier NOT NULL,\n        CONSTRAINT {unique} UNIQUE (TenantId),\n" : string.Empty)
                + $"        CONSTRAINT {primary} PRIMARY KEY (Id)\n    );\nEND;\n";
    }

    internal static string Methods(FullNetCrudSchema schema)
    {
        var table = Table(schema);
        var tenant = schema.IsTenantScoped;
        var predicate = tenant ? "TenantId = @TenantId" : "Id = @Id";
        var columns = tenant ? "Id, TenantId" : "Id";
        var values = tenant ? "@Id, @TenantId" : "@Id";
        var serverSql = $"IF NOT EXISTS (SELECT Id FROM {table} WITH (UPDLOCK, HOLDLOCK) WHERE {predicate}) INSERT INTO {table} ({columns}) VALUES ({values});";
        var mysqlSql = $"INSERT INTO {table} ({columns}) VALUES ({values}) ON DUPLICATE KEY UPDATE Id = Id;";
        var scope = schema.DataScope switch
        {
            FullNetCrudDataScope.TenantRequired => "SqlDataScope.TenantRequired",
            FullNetCrudDataScope.HostOnly => "SqlDataScope.HostOnly",
            _ => "SqlDataScope.Global",
        };
        var binding = tenant ? "SqlTenantBinding.CurrentTenantId" : "SqlTenantBinding.None";
        return $$"""
            private async Task AcquireTreeWriteLockAsync(CancellationToken cancellationToken)
            {
                // 锁行唯一键使不同节点的写操作共用同一事务互斥；在环检查和结构修改前取得。
                var sql = databaseOptions.Value.Provider == DatabaseProvider.MySql
                    ? "{{mysqlSql}}" : "{{serverSql}}";
                await commandExecutor.ExecuteAsync(new SqlStatement("{{schema.ModuleKey}}.{{schema.EntityKey}}.tree_write_lock", sql, {{scope}}, {{binding}}),
                    new { Id = {{(tenant ? "idGenerator.NewId()" : "Guid.Parse(\"01900000-0000-7000-8000-000000000001\")")}}{{(tenant ? ", TenantId = currentTenant.Id!.Value" : string.Empty)}} }, cancellationToken).ConfigureAwait(false);
            }

            private async Task<Result<{{schema.ClrTypeName}}Response>?> EnsureTreeHasNoChildrenAsync(Guid id, CancellationToken cancellationToken)
            {
                var count = await queryExecutor.QuerySingleOrDefaultAsync<long>(new SqlStatement(
                    "{{schema.ModuleKey}}.{{schema.EntityKey}}.tree_children",
                    "SELECT COUNT(*) FROM {{schema.DatabaseTableName}} WHERE ParentId = @Id{{(tenant ? " AND TenantId = @TenantId" : string.Empty)}}{{(schema.EntityCapabilities.DeleteMode == FullNetCrudDeleteMode.SoftDelete ? " AND IsDeleted = 0" : string.Empty)}}",
                    {{scope}}, {{binding}}), new { Id = id{{(tenant ? ", TenantId = currentTenant.Id!.Value" : string.Empty)}} }, cancellationToken).ConfigureAwait(false);
                return count == 0 ? null : Result<{{schema.ClrTypeName}}Response>.Failure(new Error(
                    "{{schema.ModuleKey}}.{{schema.PermissionResourceName}}.parent_has_children", "Remove or move the child nodes first.", ErrorType.Conflict));
            }
            """;
    }
}
