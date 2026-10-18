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
        var updateDefaults = string.Join(
            ",\n  ",
            updateColumns.Select(column =>
                $"{column.JsonPropertyName}: {DefaultLiteral(column)}"));
        var updateValues = string.Join(
            ",\n    ",
            updateColumns.Select(column =>
                $"{column.JsonPropertyName}: item.{column.JsonPropertyName}"));
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
            ? "确定删除该条记录吗？此操作无法撤销。"
            : "确定删除该条记录吗？";
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
                "scopeVersion",
                "cancelChange",
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
                if (!editOpen.value || !editing.value) {
                  return;
                }
                const ticket = editTicket; const scopeTicket = scopeVersion.value;
                const succeeded = await update(editing.value, { ...editForm });
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
            import { onMounted, reactive, ref, watch } from 'vue';
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
            const deleteOpen = ref(false);
            const editing = ref<{{entity}}Response>();
            const deleting = ref<{{entity}}Response>();
            let createTicket = 0; let editTicket = 0; let deleteTicket = 0;
            const initialCreateForm = () => ({
              {{createDefaults}}
            });
            const initialEditForm = () => ({
              {{updateDefaults}}
            });
            const createForm = reactive(initialCreateForm());
            const editForm = reactive(initialEditForm());

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
                  : { status: 500, code: fallbackCode, title: fallbackCode };
              }
            });

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
              createOpen.value = true;
            }

            function openEdit(row: unknown): void {
              if (!{{canUpdateExpr}}.value || changing.value) return;
              // 表格插槽将行标为通用对象；只接受已由生成客户端校验并进入页面模型的同一对象。
              const item = items.value.find(candidate => candidate === row);
              if (!item) return;
              editing.value = item;
              // 只拷贝声明可更新字段；身份、租户和审计字段不得进入可提交表单。
              Object.assign(editForm, {
                {{updateValues}}
              });
              editOpen.value = true;
            }

            function openDelete(row: unknown): void {
              if (!{{canRemoveExpr}}.value || changing.value) return;
              // 先展示确认弹窗；表格按钮不得直接执行不可撤销的删除请求。
              const item = items.value.find(candidate => candidate === row);
              if (!item) return;
              deleting.value = item;
              deleteOpen.value = true;
            }

            async function submitCreate(): Promise<void> {
              if (!createOpen.value) return;
              const ticket = createTicket; const scopeTicket = scopeVersion.value;
              const succeeded = await create({ ...createForm });
              if (succeeded && ticket === createTicket && scopeTicket === scopeVersion.value && createOpen.value) {
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
                  :data="[...items]"
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
                        @click="openDelete(row)"
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
                    <el-button type="primary" :loading="changing" @click="submitCreate">保存</el-button>
                  </template>
                </el-dialog>
                <el-dialog v-model="editOpen" title="编辑">
                  <el-form label-width="120px">
            {{updateFields}}
                  </el-form>
                  <template #footer>
                    <el-button @click="editOpen = false">取消</el-button>
                    <el-button type="primary" :loading="changing" @click="submitEdit">保存</el-button>
                  </template>
                </el-dialog>
                <el-dialog v-model="deleteOpen" title="确认删除" style="--el-color-danger: #b42318; --el-color-danger-light-3: #b42318; --el-color-danger-dark-2: #991b1b">
                  <p>{{deleteWarning}}</p>
                  <template #footer>
                    <el-button @click="deleteOpen = false">取消</el-button>
                    <el-button type="danger" :loading="changing" @click="confirmDelete">确认删除</el-button>
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
