using Full.NET.Data.CodeGeneration.Schema;

namespace Full.NET.Data.CodeGeneration.Generation;

/// <summary>生成同一编辑会话的主从表单，新增、修改和移除明细在一次聚合请求中提交。</summary>
internal static class CrudAggregateVueGenerator
{
    internal static string Imports(FullNetCrudSchema schema) => !CrudAggregateGenerator.Enabled(schema) ? string.Empty
        : $", {schema.ClrTypeName}DetailInput, {schema.ClrTypeName}DetailResponse, {schema.ClrTypeName}DetailRemoval, {schema.ClrTypeName}DetailChanges";

    internal static string Model(FullNetCrudSchema schema)
    {
        if (!CrudAggregateGenerator.Enabled(schema)) return string.Empty;
        var root = schema.ClrTypeName;
        var fields = CrudAggregateGenerator.InputColumns(schema);
        var defaults = string.Join(", ", fields.Select(column => column.JsonPropertyName + ": " + CrudVueFormGenerator.Default(column)));
        var child = CrudAggregateGenerator.Detail(schema);
        var childId = child.Columns.Single(column => column.DatabaseName == "Id").JsonPropertyName;
        var version = child.Columns.Single(column => column.DatabaseName == "Version").JsonPropertyName;
        return $$"""
            type DetailEditor = { key: string; id?: string; version?: string; input: { -readonly [Key in keyof {{root}}DetailInput]: {{root}}DetailInput[Key] } };
            const createRows = ref<DetailEditor[]>([]); const editRows = ref<DetailEditor[]>([]);
            const removedDetails = ref<{{root}}DetailRemoval[]>([]);
            const detailForms = new Map<string, FormInstance>();
            const detailCreateRules = computed(() => buildRules({{CrudVueFormGenerator.RuleFields(fields.Where(column => column.ResolvedUi.IncludeInCreate))}}, locale.value));
            const detailEditRules = computed(() => buildRules({{CrudVueFormGenerator.RuleFields(fields.Where(column => column.ResolvedUi.IncludeInUpdate))}}, locale.value));
            function captureDetailForm(key: string, instance: unknown): void {
              if (typeof instance === 'object' && instance !== null && 'validate' in instance && typeof instance.validate === 'function') detailForms.set(key, instance as FormInstance);
              else detailForms.delete(key);
            }
            function addDetail(rows: DetailEditor[]): void {
              if (rows.length >= 128) return;
              rows.push({ key: crypto.randomUUID(), input: { {{defaults}} } });
            }
            function removeDetail(rows: DetailEditor[], key: string): void {
              const index = rows.findIndex(row => row.key === key); if (index < 0) return;
              const row = rows[index];
              if (row.id && row.version) removedDetails.value.push({ id: row.id, version: row.version });
              rows.splice(index, 1); detailForms.delete(key);
            }
            function prepareDetails(rows: readonly {{root}}DetailResponse[]): void {
              removedDetails.value = [];
              editRows.value = rows.map(row => ({ key: crypto.randomUUID(), id: row.{{childId}}, version: row.{{version}},
                input: { {{string.Join(", ", fields.Select(column => column.JsonPropertyName + ": row." + column.JsonPropertyName))}} } }));
            }
            async function validateDetails(rows: readonly DetailEditor[]): Promise<boolean> {
              if (rows.length > 128) return false;
              return (await Promise.all(rows.map(row => detailForms.get(row.key)?.validate().catch(() => false) ?? false))).every(Boolean);
            }
            function detailChanges(): {{root}}DetailChanges {
              return { added: editRows.value.filter(row => !row.id).map(row => ({ ...row.input })),
                updated: editRows.value.filter(row => row.id).map(row => ({ id: row.id!, version: row.version!, input: { ...row.input } })),
                removed: [...removedDetails.value] };
            }
            """;
    }

    internal static string Editor(FullNetCrudSchema schema, bool create)
    {
        if (!CrudAggregateGenerator.Enabled(schema)) return string.Empty;
        var rows = create ? "createRows" : "editRows";
        var fields = string.Join("\n", CrudAggregateGenerator.InputColumns(schema).Where(column => column.ResolvedUi.IncludeInCreate || column.ResolvedUi.IncludeInUpdate).Select(column =>
        {
            var field = CrudVueFormGenerator.Field("row.input", column, inlineDate: true);
            var conditional = !column.ResolvedUi.IncludeInCreate ? "row.id" : !column.ResolvedUi.IncludeInUpdate ? "!row.id" : null;
            return conditional is null ? field : $"<template v-if=\"{conditional}\">{field}</template>";
        }));
        return $$"""
                  <section class="generated-crud-view__details">
                    <h3>{{Mustache("t('generatedCrud.details')")}}</h3>
                    <p>{{Mustache("t('generatedCrud.detailLimit')")}}</p>
                    <el-button @click="addDetail({{rows}})" :disabled="changing || {{rows}}.length >= 128">{{Mustache("t('generatedCrud.addDetail')")}}</el-button>
                    <div v-for="(row, index) in {{rows}}" :key="row.key" class="generated-crud-view__detail-row">
                      <h4>{{Mustache("t('generatedCrud.detailRow', { count: index + 1 })")}}</h4>
                      <el-form :ref="instance => captureDetailForm(row.key, instance)" :model="row.input" :rules="row.id ? detailEditRules : detailCreateRules" label-width="120px">
            {{fields}}
                      </el-form>
                      <el-button @click="removeDetail({{rows}}, row.key)" :disabled="changing">{{Mustache("t('generatedCrud.removeDetail')")}}</el-button>
                    </div>
                  </section>
            """;
    }

    private static string Mustache(string expression) => "{{ " + expression + " }}";
}
