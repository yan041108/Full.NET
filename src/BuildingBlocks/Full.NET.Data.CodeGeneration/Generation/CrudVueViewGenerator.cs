using System.Text;
using Full.NET.Data.CodeGeneration.Schema;

namespace Full.NET.Data.CodeGeneration.Generation;

/// <summary>
/// 生成消费既有页面模型的 Vue 3 + Element Plus 列表/编辑页，按精确权限隐藏操作。
/// </summary>
internal static class CrudVueViewGenerator
{
    /// <summary>
    /// 生成可落地 SFC。导入路径按写入 <c>ui/admin/src/views</c> 后的位置计算。
    /// </summary>
    internal static string Generate(FullNetCrudSchema schema)
    {
        ArgumentNullException.ThrowIfNull(schema);
        var entity = schema.ClrTypeName;
        var entityVar = LowerFirst(entity);
        var pageHook = $"use{entity}Page";
        var listColumns = schema.Columns
            .Where(column => column.ResolvedUi.ShowInList)
            .ToArray();
        var createColumns = schema.Columns
            .Where(column => column.ResolvedUi.IncludeInCreate)
            .ToArray();
        var updateColumns = schema.Columns
            .Where(column => column.ResolvedUi.IncludeInUpdate)
            .ToArray();
        var tableColumns = string.Join(
            "\n",
            listColumns.Select(column =>
                $"      <el-table-column prop=\"{column.JsonPropertyName}\" label=\"{column.DatabaseName}\" />"));
        var createFields = string.Join(
            "\n",
            createColumns.Select(column => RenderFormField("createForm", column)));
        var updateFields = string.Join(
            "\n",
            updateColumns.Select(column => RenderFormField("editForm", column)));
        var createDefaults = string.Join(
            ",\n  ",
            createColumns.Select(column =>
                $"{column.JsonPropertyName}: {DefaultLiteral(column)}"));
        var canCreateExpr = schema.UsesLegacyEntityCapabilities
            ? "canWrite"
            : "canCreate";
        var canUpdateExpr = schema.UsesLegacyEntityCapabilities
            ? "canWrite"
            : "canUpdate";
        var canRemoveExpr = schema.UsesLegacyEntityCapabilities
            ? "canWrite"
            : "canDisable";
        var updateAction = schema.EntityCapabilities.CanUpdate
            || schema.UsesLegacyEntityCapabilities
            ? "update"
            : string.Empty;
        var removeAction = schema.EntityCapabilities.CanDelete
            || schema.UsesLegacyEntityCapabilities
            ? "remove"
            : string.Empty;
        var returned = string.Join(
            ",\n  ",
            new[]
            {
                "items",
                "page",
                "pageSize",
                "total",
                "loading",
                canCreateExpr,
                canUpdateExpr,
                canRemoveExpr,
                "load",
                "create",
                updateAction,
                removeAction,
            }.Where(static name => name.Length > 0).Distinct(StringComparer.Ordinal));
        var submitEdit = updateAction.Length == 0
            ? "async function submitEdit(): Promise<void> {}"
            : """
              async function submitEdit(): Promise<void> {
                if (!editing.value) {
                  return;
                }
                const succeeded = await update(editing.value, { ...editForm });
                if (succeeded) {
                  editOpen.value = false;
                }
              }
              """;
        var submitRemove = removeAction.Length == 0
            ? $"async function removeRow(_row: {entity}Response): Promise<void> {{}}"
            : """
              async function removeRow(row: EntityResponse): Promise<void> {
                await remove(row);
              }
              """.Replace("EntityResponse", entity + "Response", StringComparison.Ordinal);

        return Normalize(
            $$"""
            <script setup lang="ts">
            import { onMounted, reactive, ref } from 'vue';
            import {
              ElButton,
              ElDialog,
              ElForm,
              ElFormItem,
              ElInput,
              ElPagination,
              ElSwitch,
              ElTable,
              ElTableColumn
            } from 'element-plus';
            import {
              isFullNetProblemDetails,
              type FullNetProblemDetails
            } from '@fullnet/client-contracts';
            import { http } from '../api/http';
            import { useSessionStore } from '../auth/session';
            import { {{pageHook}} } from './{{schema.ApiResourceName}}-page.generated';
            import type { {{entity}}Response } from './{{schema.ApiResourceName}}.generated';

            const session = useSessionStore();
            const problem = ref<FullNetProblemDetails>();
            const createOpen = ref(false);
            const editOpen = ref(false);
            const editing = ref<{{entity}}Response>();
            const createForm = reactive({
              {{createDefaults}}
            });
            const editForm = reactive({
              {{createDefaults}}
            });

            const {
              {{returned}}
            } = {{pageHook}}({
              request: http,
              hasPermission: permission => session.can(permission),
              onProblem: (error, fallbackCode) => {
                problem.value = isFullNetProblemDetails(error)
                  ? error
                  : { status: 500, code: fallbackCode, title: fallbackCode };
              }
            });

            onMounted(() => {
              void load();
            });

            function openCreate(): void {
              createOpen.value = true;
            }

            function openEdit(row: {{entity}}Response): void {
              editing.value = row;
              Object.assign(editForm, row);
              editOpen.value = true;
            }

            async function submitCreate(): Promise<void> {
              const succeeded = await create({ ...createForm });
              if (succeeded) {
                createOpen.value = false;
              }
            }

            {{submitEdit}}

            {{submitRemove}}
            </script>

            <template>
              <section class="generated-crud-view">
                <div v-if="problem" class="art-inline-alert" role="alert">
                  <strong translate="no">{{Mustache("problem.code")}}</strong>
                  <span>{{Mustache("problem.title")}}</span>
                </div>
                <div class="generated-crud-view__toolbar">
                  <el-button
                    v-if="{{canCreateExpr}}"
                    type="primary"
                    @click="openCreate"
                  >
                    创建
                  </el-button>
                </div>
                <el-table
                  :data="items"
                  empty-text="暂无数据"
                  v-loading="loading"
                >
            {{tableColumns}}
                  <el-table-column label="操作" width="160">
                    <template #default="{ row }">
                      <el-button
                        v-if="{{canUpdateExpr}}"
                        link
                        type="primary"
                        @click="openEdit(row)"
                      >
                        编辑
                      </el-button>
                      <el-button
                        v-if="{{canRemoveExpr}}"
                        link
                        type="danger"
                        @click="removeRow(row)"
                      >
                        删除
                      </el-button>
                    </template>
                  </el-table-column>
                </el-table>
                <el-pagination
                  :current-page="page"
                  :page-size="pageSize"
                  :total="total"
                  layout="total, prev, pager, next"
                  @current-change="(next: number) => load(next)"
                />
                <el-dialog v-model="createOpen" title="创建">
                  <el-form label-width="120px">
            {{createFields}}
                  </el-form>
                  <template #footer>
                    <el-button @click="createOpen = false">取消</el-button>
                    <el-button type="primary" @click="submitCreate">保存</el-button>
                  </template>
                </el-dialog>
                <el-dialog v-model="editOpen" title="编辑">
                  <el-form label-width="120px">
            {{updateFields}}
                  </el-form>
                  <template #footer>
                    <el-button @click="editOpen = false">取消</el-button>
                    <el-button type="primary" @click="submitEdit">保存</el-button>
                  </template>
                </el-dialog>
              </section>
            </template>
            """);
    }

    private static string RenderFormField(string formName, FullNetColumn column)
    {
        var control = column.ResolvedUi.ControlKind switch
        {
            FullNetColumnControlKind.Switch =>
                $"        <el-switch v-model=\"{formName}.{column.JsonPropertyName}\" />",
            FullNetColumnControlKind.Textarea =>
                $"        <el-input v-model=\"{formName}.{column.JsonPropertyName}\" type=\"textarea\" />",
            _ =>
                $"        <el-input v-model=\"{formName}.{column.JsonPropertyName}\" />",
        };
        return $"""
                  <el-form-item label="{column.DatabaseName}">
            {control}
                  </el-form-item>
            """;
    }

    private static string DefaultLiteral(FullNetColumn column) =>
        column.ScalarType switch
        {
            FullNetScalarType.Boolean => "false",
            FullNetScalarType.Int32 or FullNetScalarType.Int64
                or FullNetScalarType.Decimal => "0",
            _ => column.IsNullable ? "null" : "''",
        };

    private static string Mustache(string expression) =>
        "{{" + expression + "}}";

    private static string LowerFirst(string value) =>
        string.Concat(char.ToLowerInvariant(value[0]), value[1..]);

    private static string Normalize(string content)
    {
        var builder = new StringBuilder(content.Length + 1);
        builder.Append(content.Replace("\r\n", "\n", StringComparison.Ordinal)
            .TrimEnd('\r', '\n'));
        builder.Append('\n');
        return builder.ToString();
    }
}
