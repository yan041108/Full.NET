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
        var requestColumns = CrudArtifactGenerator.FormRequestColumns(schema);
        var createColumns = requestColumns
            .Where(column => column.ResolvedUi.IncludeInCreate)
            .ToArray();
        var updateColumns = requestColumns
            .Where(column => column.ResolvedUi.IncludeInUpdate)
            .ToArray();
        var tableColumns = string.Join(
            "\n",
            listColumns.Select(column =>
                $"      <el-table-column prop=\"{column.JsonPropertyName}\" :label=\"t('generatedCrud.field', {{ name: '{column.DatabaseName}' }})\"{(column.ResolvedUi.Sortable ? ' '+"sortable=\"custom\"" : string.Empty)} />"));
        var createFields = string.Join(
            "\n",
            createColumns.Select(column => schema.Scene == FullNetCrudScene.Tree && column.DatabaseName == "ParentId"
                ? CrudTreeGenerator.VueParentField(schema, "createForm") : CrudRelationshipLookupGenerator.Enabled(schema) && CrudRelationshipLookupVueGenerator.Index(schema, column) >= 0
                    ? CrudRelationshipLookupVueGenerator.Field(schema, column, "createForm") : CrudVueFormGenerator.Field("createForm", column)));
        var updateFields = string.Join(
            "\n",
            updateColumns.Select(column => schema.Scene == FullNetCrudScene.Tree && column.DatabaseName == "ParentId"
                ? CrudTreeGenerator.VueParentField(schema, "editForm") : CrudRelationshipLookupGenerator.Enabled(schema) && CrudRelationshipLookupVueGenerator.Index(schema, column) >= 0
                    ? CrudRelationshipLookupVueGenerator.Field(schema, column, "editForm") : CrudVueFormGenerator.Field("editForm", column)));
        var createDefaults = string.Join(
            ",\n  ",
            requestColumns.Select(column =>
                $"{column.JsonPropertyName}: {CrudVueFormGenerator.Default(column)}"));
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
            ? schema.UsesLegacyEntityCapabilities ? "disable" : "remove"
            : string.Empty;
        var deleteWarning = !schema.UsesLegacyEntityCapabilities
            && schema.EntityCapabilities.DeleteMode == FullNetCrudDeleteMode.HardDelete
            ? Mustache("t('generatedCrud.irreversibleRemove')")
            : Mustache("t('generatedCrud.removePrompt')");
        var returned = string.Join(
            ",\n  ",
            new[]
            {
                "items",
                "page",
                "pageSize",
                "total",
                "loading",
                "changing",
                "query",
                "scopeVersion",
                "cancelChange",
                canCreateExpr,
                canUpdateExpr,
                canRemoveExpr,
                "load",
                "create",
                CrudAggregateGenerator.Enabled(schema) ? "loadAggregate" : string.Empty,
                updateAction,
                schema.UsesLegacyEntityCapabilities ? "disable: remove" : removeAction,
            }.Where(static name => name.Length > 0).Distinct(StringComparer.Ordinal));
        var submitEdit = updateAction.Length == 0
            ? "async function submitEdit(): Promise<void> {}"
            : $$"""
              async function submitEdit(): Promise<void> {
                if (!editOpen.value || !editing.value) {
                  return;
                }
                if (!await editFormRef.value?.validate().catch(() => false)) return;
                {{(CrudAggregateGenerator.Enabled(schema) ? "if (!await validateDetails(editRows.value)) return;" : string.Empty)}}
                const ticket = editTicket; const scopeTicket = scopeVersion.value;
                const succeeded = await update(editing.value, { ...editForm }{{(CrudAggregateGenerator.Enabled(schema) ? ", detailChanges()" : string.Empty)}});
                if (succeeded && ticket === editTicket && scopeTicket === scopeVersion.value && editOpen.value) {
                  editOpen.value = false;
                }
              }
              """;
        var submitRemove = removeAction.Length == 0
            ? "async function confirmDelete(): Promise<void> {}"
            : $$"""
              async function confirmDelete(): Promise<void> {
                if (!deleteOpen.value || !deleting.value) return;
                const ticket = deleteTicket; const scopeTicket = scopeVersion.value;
                const succeeded = await {{removeAction}}(deleting.value);
                if (succeeded && ticket === deleteTicket && scopeTicket === scopeVersion.value && deleteOpen.value) {
                  deleteOpen.value = false;
                }
              }
              """;

        return Normalize(
            $$"""
            <script setup lang="ts">
            import { computed, nextTick, onMounted, reactive, ref, watch } from 'vue';
            import {
              ElButton,
              ElDialog,
              ElForm,
              ElFormItem,
              ElInput,
              ElInputNumber,
              ElDatePicker,
              ElSelect,
              ElRadio,
              ElRadioGroup,
              ElOption,
              type FormInstance,
              type FormItemRule,
              ElPagination,
              ElSwitch,
              ElTable,
              vLoading,
              ElTableColumn
            } from 'element-plus';
            import {
              isFullNetProblemDetails,
              type FullNetProblemDetails
            } from '@fullnet/client-contracts';
            import { http } from '../api/http';
            import { useSessionStore } from '../auth/session';
            import { useAdminI18n } from '../i18n/adminI18n';
            import { {{pageHook}} } from './{{schema.ApiResourceName}}-page.generated';
            import type { {{entity}}Response, Create{{entity}}Request, {{entity}}ListOptions{{CrudAggregateVueGenerator.Imports(schema)}} } from './{{schema.ApiResourceName}}.generated';
            {{CrudRelationshipLookupVueGenerator.Imports(schema)}}

            const session = useSessionStore();
            const { t, locale } = useAdminI18n();
            const createFormRef = ref<FormInstance>();
            const editFormRef = ref<FormInstance>();
            {{(CrudOrganizationOwnershipGenerator.IsOrganizationUnitOwned(schema) ? "const organizationUnitId = ref('');" : string.Empty)}}
            const problem = ref<FullNetProblemDetails>();
            const createOpen = ref(false);
            const editOpen = ref(false);
            const deleteOpen = ref(false);
            const editing = ref<{{entity}}Response>();
            const deleting = ref<{{entity}}Response>();
            let createTicket = 0; let editTicket = 0; let deleteTicket = 0;
            type FormModel = { -readonly [Key in keyof Create{{entity}}Request]: Create{{entity}}Request[Key] };
            function defaults(): FormModel { return { {{createDefaults}} }; }
            const createForm = reactive<FormModel>(defaults());
            const editForm = reactive<FormModel>(defaults());
            const initialCreateForm = defaults; const initialEditForm = defaults;

            const {
              {{returned}}
            } = {{pageHook}}({
              request: http,
              contextKey: () => JSON.stringify([session.state, session.currentUser?.id,
                session.currentUser?.sessionId, session.currentUser?.tenantId, session.currentUser?.scope,
                session.currentUser?.actorScope, session.currentUser?.permissions]),
              hasPermission: permission => session.can(permission),
              onProblem: (error, fallbackCode) => {
                problem.value = isFullNetProblemDetails(error)
                  ? error
                  : { status: 500, code: fallbackCode, title: t('generatedCrud.operationFailed') };
              }
            });

            {{CrudVueFormGenerator.DateModels(schema, requestColumns)}}
            {{CrudVueFormGenerator.Rules(requestColumns)}}
            {{CrudAggregateVueGenerator.Model(schema)}}
            {{CrudRelationshipLookupVueGenerator.Model(schema)}}
            {{(schema.Scene == FullNetCrudScene.Tree ? CrudTreeGenerator.VueModel(schema) : string.Empty)}}
            const sortKeys = new Set<string>([{{string.Join(", ", schema.Columns.Where(column => column.ResolvedUi.Sortable).Select(column => $"'{column.JsonPropertyName}'"))}}]);
            function sortChanged(event: { prop?: string | null; order?: string | null }): void {
              if (event.prop && event.order) {
                if (!sortKeys.has(event.prop)) return;
                query.sortBy = event.prop as {{entity}}ListOptions['sortBy'];
                query.descending = event.order === 'descending';
              } else { query.sortBy = undefined; query.descending = undefined; }
              void load(1);
            }
            function clearFilters(): void {
              for (const key of Object.keys(query)) Reflect.deleteProperty(query, key);
              void load(1);
            }

            // 旧上下文的对话框、行与输入必须同步失效，迟到响应由页面模型拒绝接入。
            watch(scopeVersion, () => {
              createOpen.value = false; editOpen.value = false; deleteOpen.value = false;
              editing.value = undefined; deleting.value = undefined; problem.value = undefined;
              Object.assign(createForm, initialCreateForm()); Object.assign(editForm, initialEditForm());
            }, { flush: 'sync' });
            watch(createOpen, open => {
              createTicket++;
              if (!open) { cancelChange(); Object.assign(createForm, initialCreateForm()); }
            }, { flush: 'sync' });
            watch(editOpen, open => {
              editTicket++;
              if (!open) { cancelChange(); editing.value = undefined; Object.assign(editForm, initialEditForm()); }
            }, { flush: 'sync' });
            watch(deleteOpen, open => {
              deleteTicket++;
              if (!open) { cancelChange(); deleting.value = undefined; }
            }, { flush: 'sync' });

            onMounted(() => {
              void load();
            });

            function openCreate(): void {
              if (!{{canCreateExpr}}.value || changing.value) return;
              problem.value = undefined;
              Object.assign(createForm, defaults());
              {{(CrudAggregateGenerator.Enabled(schema) ? "createRows.value = [];" : string.Empty)}}
              createOpen.value = true;
              void nextTick(() => createFormRef.value?.clearValidate());
            }

            async function openEdit(row: unknown): Promise<void> {
              if (!{{canUpdateExpr}}.value || changing.value) return;
              // 树行附带 children；按白名单中的标识回到已由生成客户端校验的当前记录。
              let item = resolveRow(row);
              if (!item) return;
              {{(CrudAggregateGenerator.Enabled(schema) ? "const aggregate = await loadAggregate(item); if (!aggregate) return; item = aggregate.master; prepareDetails(aggregate.details);" : string.Empty)}}
              editing.value = item;
              Object.assign(editForm, { {{string.Join(", ", requestColumns.Select(column => $"{column.JsonPropertyName}: item.{column.JsonPropertyName}"))}} });
              editOpen.value = true;
              {{CrudRelationshipLookupVueGenerator.Hydrate(schema)}}
              void nextTick(() => editFormRef.value?.clearValidate());
            }

            function openDelete(row: unknown): void {
              if (!{{canRemoveExpr}}.value || changing.value) return;
              // 先展示确认弹窗；表格按钮不得直接执行不可撤销的删除请求。
              const item = resolveRow(row);
              if (!item) return;
              deleting.value = item;
              deleteOpen.value = true;
            }

            async function submitCreate(): Promise<void> {
              if (!await createFormRef.value?.validate().catch(() => false)) return;
              {{(CrudAggregateGenerator.Enabled(schema) ? "if (!await validateDetails(createRows.value)) return;" : string.Empty)}}
              {{(CrudOrganizationOwnershipGenerator.IsOrganizationUnitOwned(schema) ? "if (!/^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/i.test(organizationUnitId.value) || organizationUnitId.value === '00000000-0000-0000-0000-000000000000') { problem.value = { status: 400, code: 'client.organization_unit_required', title: t('generatedCrud.uuidInvalid') }; return; }" : string.Empty)}}
              if (!createOpen.value) return;
              const ticket = createTicket; const scopeTicket = scopeVersion.value;
              const succeeded = await create({ ...createForm }{{(CrudAggregateGenerator.Enabled(schema) ? ", createRows.value.map(row => ({ ...row.input }))" : string.Empty)}}{{(CrudOrganizationOwnershipGenerator.IsOrganizationUnitOwned(schema) ? ", organizationUnitId.value" : string.Empty)}});
              if (succeeded && ticket === createTicket && scopeTicket === scopeVersion.value && createOpen.value) {
                createOpen.value = false;
              }
            }

            {{submitEdit}}

            {{submitRemove}}
            function resolveRow(row: unknown): {{entity}}Response | undefined {
              if (typeof row !== 'object' || row === null || !('{{schema.Columns.Single(column => column.DatabaseName == "Id").JsonPropertyName}}' in row)) return undefined;
              return items.value.find(candidate => candidate.{{schema.Columns.Single(column => column.DatabaseName == "Id").JsonPropertyName}} === row.{{schema.Columns.Single(column => column.DatabaseName == "Id").JsonPropertyName}});
            }
            </script>

            <template>
              <section class="generated-crud-view">
                <div v-if="problem" class="art-inline-alert" role="alert">
                  <strong translate="no">{{Mustache("problem.code")}}</strong>
                  <span>{{Mustache("problem.title")}}</span>
                </div>
                <el-form class="generated-crud-view__filters" inline @submit.prevent="load(1)">
            {{string.Join("\n", CrudListQueryGenerator.Fields(schema).Select(field => CrudVueFormGenerator.Field("query", field.Column, field.QueryName, query: true,
                label: field.Suffix == "From" ? $"t('generatedCrud.rangeFrom', {{ name: '{field.Column.DatabaseName}' }})" : field.Suffix == "To" ? $"t('generatedCrud.rangeTo', {{ name: '{field.Column.DatabaseName}' }})" : null)))}}
                  <el-button native-type="submit" :loading="loading">{{Mustache("t('generatedCrud.search')")}}</el-button>
                  <el-button :disabled="loading" @click="clearFilters">{{Mustache("t('generatedCrud.reset')")}}</el-button>
                </el-form>
                <div class="generated-crud-view__toolbar">
                  <el-button
                    v-if="{{canCreateExpr}}"
                    type="primary"
                    @click="openCreate"
                  >{{Mustache("t('generatedCrud.create')")}}</el-button>
                </div>
                <el-table
                  :data="{{(schema.Scene == FullNetCrudScene.Tree ? "treeModel.rows" : "[...items]")}}"
                  {{(schema.Scene == FullNetCrudScene.Tree ? "row-key=\"" + schema.Columns.Single(column => column.DatabaseName == "Id").JsonPropertyName + "\" default-expand-all :tree-props=\"{ children: 'children' }\"" : string.Empty)}}
                  :empty-text="t('generatedCrud.empty')"
                  @sort-change="sortChanged"
                  v-loading="loading"
                >
            {{tableColumns}}
                  <el-table-column :label="t('generatedCrud.actions')" width="160">
                    <template #default="{ row }">
                      <el-button
                        v-if="{{canUpdateExpr}}"
                        link
                        type="primary"
                        @click="openEdit(row)"
                      >{{Mustache("t('generatedCrud.update')")}}</el-button>
                      <el-button
                        v-if="{{canRemoveExpr}}"
                        link
                        type="danger"
                        @click="openDelete(row)"
                      >{{Mustache("t('generatedCrud.remove')")}}</el-button>
                    </template>
                  </el-table-column>
                </el-table>
                {{(schema.Scene == FullNetCrudScene.Tree ? "<p v-if=\"treeModel.invalid\" role=\"alert\">{{ t('generatedCrud.invalidTree') }}</p>" : string.Empty)}}
                <el-pagination v-if="{{(schema.Scene == FullNetCrudScene.Tree ? "false" : "true")}}"
                  :current-page="page"
                  :page-size="pageSize"
                  :total="total"
                  layout="total, prev, pager, next"
                  @current-change="(next: number) => load(next)"
                />
                <el-dialog append-to-body width="min(680px, calc(100vw - 32px))" v-model="createOpen" :title="t('generatedCrud.create')" style="--el-color-danger: #b42318; --el-text-color-placeholder: #59636f">
                  <el-form ref="createFormRef" :model="createForm" :rules="createRules" label-width="120px">
            {{createFields}}
            {{(CrudOrganizationOwnershipGenerator.IsOrganizationUnitOwned(schema) ? "<el-form-item :label=\"t('generatedCrud.organizationUnit')\"><el-input v-model=\"organizationUnitId\" :aria-label=\"t('generatedCrud.organizationUnit')\" /></el-form-item>" : string.Empty)}}
                  </el-form>
            {{CrudAggregateVueGenerator.Editor(schema, true)}}
                  <template #footer>
                    <el-button @click="createOpen = false">{{Mustache("t('generatedCrud.cancel')")}}</el-button>
                    <el-button type="primary" @click="submitCreate" :loading="changing">{{Mustache("t('generatedCrud.save')")}}</el-button>
                  </template>
                </el-dialog>
                <el-dialog append-to-body width="min(680px, calc(100vw - 32px))" v-model="editOpen" :title="t('generatedCrud.update')" style="--el-color-danger: #b42318; --el-text-color-placeholder: #59636f">
                  <el-form ref="editFormRef" :model="editForm" :rules="editRules" label-width="120px">
            {{updateFields}}
                  </el-form>
            {{CrudAggregateVueGenerator.Editor(schema, false)}}
                  <template #footer>
                    <el-button @click="editOpen = false">{{Mustache("t('generatedCrud.cancel')")}}</el-button>
                    <el-button type="primary" @click="submitEdit" :loading="changing">{{Mustache("t('generatedCrud.save')")}}</el-button>
                  </template>
                </el-dialog>
                <el-dialog append-to-body width="min(680px, calc(100vw - 32px))" v-model="deleteOpen" :title="t('generatedCrud.confirmRemove')" style="--el-color-danger: #b42318; --el-color-danger-light-3: #b42318; --el-color-danger-dark-2: #991b1b" @close="deleting = undefined">
                  <p>{{deleteWarning}}</p>
                  <template #footer>
                    <el-button @click="deleteOpen = false">{{Mustache("t('generatedCrud.cancel')")}}</el-button>
                    <el-button type="danger" @click="confirmDelete" :loading="changing">{{Mustache("t('generatedCrud.confirmRemove')")}}</el-button>
                  </template>
                </el-dialog>
              </section>
            </template>
            <style scoped>
            .generated-crud-view { display: grid; gap: 1rem; min-width: 0; }
            .generated-crud-view__filters { display: flex; flex-wrap: wrap; align-items: center; gap: .5rem; }
            .generated-crud-view__details { display: grid; gap: .75rem; margin-block: 1rem; }
            .generated-crud-view__detail-row { border: 1px solid var(--el-border-color); border-radius: 4px; padding: .75rem; min-width: 0; }
            .generated-crud-view :deep(.el-input-number), .generated-crud-view :deep(.el-date-editor), .generated-crud-view :deep(.el-select) { max-width: 100%; }
            /* 空选择框的占位元素使用绝对定位，显式保留内容高度，避免移动端背景采样越过弹窗。 */
            :global(.generated-crud-select .el-select__selection) { min-height: 24px; background-color: var(--el-fill-color-blank, #fff); }
            </style>
            """);
    }

    private static string Mustache(string expression) =>
        "{{" + expression + "}}";

    private static string LowerFirst(string value) =>
        string.Concat(char.ToLowerInvariant(value[0]), value[1..]);

    private static string Normalize(string content)
    {
        var builder = new StringBuilder(content.Length + 1);
        builder.Append(string.Join("\n", content.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n').Select(line => line.TrimEnd(' ', '\t')))
            .TrimEnd('\r', '\n'));
        builder.Append('\n');
        return builder.ToString();
    }
}
