using System.Text;
using Full.NET.Data.CodeGeneration.Schema;

namespace Full.NET.Data.CodeGeneration.Generation;

/// <summary>
/// 生成只承载页面状态与动作的双管理端模型，视觉结构、路由和菜单仍由宿主维护。
/// </summary>
internal static class CrudClientPageModelGenerator
{
    /// <summary>
    /// 生成复用现有 TypeScript API 客户端的 Vue Composition API 页面模型。
    /// </summary>
    internal static string GenerateVue(FullNetCrudSchema schema)
    {
        ArgumentNullException.ThrowIfNull(schema);
        if (!schema.UsesLegacyEntityCapabilities)
        {
            return GenerateExplicitVue(schema);
        }

        var entityVariable = LowerFirst(schema.ClrTypeName);
        var apiFactoryName = HttpSegmentToPascalCase(schema.ApiResourceName);
        var problemPrefix = ProblemCodePrefix(schema);
        var idProperty = JsonProperty(schema, "Id");
        var versionProperty = schema.HasVersion
            ? JsonProperty(schema, "Version")
            : null;
        var updateType = schema.HasVersion
            ? $"Omit<Update{schema.ClrTypeName}Request, '{versionProperty}'>"
            : $"Update{schema.ClrTypeName}Request";
        var updateCall = schema.HasVersion
            ? $$"""
            await api.update(item.{{idProperty}}, {
              ...input,
              {{versionProperty}}: item.{{versionProperty}}
            }, request.signal);
            """
            : $"await api.update(item.{idProperty}, input, request.signal);";
        var disableCall = schema.HasVersion
            ? $$"""
            await api.disable(item.{{idProperty}}, {
              {{versionProperty}}: item.{{versionProperty}}
            }, request.signal);
            """
            : $"await api.disable(item.{idProperty}, request.signal);";

        return Normalize(
            $$"""
            import { computed, onActivated, onBeforeUnmount, onDeactivated, readonly, ref, toRaw, watch } from 'vue';
            import {
              create{{apiFactoryName}}Api,
              {{entityVariable}}Permissions
            } from './{{schema.ApiResourceName}}.generated';
            import type {
              Create{{schema.ClrTypeName}}Request,
              GeneratedRequest,
              {{schema.ClrTypeName}}Response,
              Update{{schema.ClrTypeName}}Request
            } from './{{schema.ApiResourceName}}.generated';

            export type {{schema.ClrTypeName}}PageUpdate = {{updateType}};

            export type {{schema.ClrTypeName}}PageProblemCode =
              | 'client.{{problemPrefix}}_load_failed'
              | 'client.{{problemPrefix}}_operation_failed';

            export interface {{schema.ClrTypeName}}PageDependencies {
              request: GeneratedRequest;
              contextKey: () => string;
              hasPermission: (permission: string) => boolean;
              onProblem: (
                problem: unknown,
                fallbackCode: {{schema.ClrTypeName}}PageProblemCode
              ) => void;
            }

            export function use{{schema.ClrTypeName}}Page(
              dependencies: {{schema.ClrTypeName}}PageDependencies
            ) {
              const api = create{{apiFactoryName}}Api(dependencies.request);
              const items = ref<{{schema.ClrTypeName}}Response[]>([]);
              const page = ref(1);
              const pageSize = ref(20);
              const total = ref(0);
              const loading = ref(false);
              const changing = ref(false);
              const canRead = computed(() =>
                dependencies.hasPermission({{entityVariable}}Permissions.read)
              );
              const canWrite = computed(() =>
                dependencies.hasPermission({{entityVariable}}Permissions.write)
              );

            {{IndentLines(GenerateVueLifecycle(schema).ReplaceLineEndings("\n"), 2).Replace("\n  \n", "\n\n", StringComparison.Ordinal)}}

              async function load(
                nextPage = page.value,
                nextPageSize = pageSize.value
              ): Promise<boolean> {
                if (!canRead.value || loading.value) return false;
                const request = beginRequest({{entityVariable}}Permissions.read);
                if (!request) return false;
                loading.value = true;
                try {
                  const result = await api.list(nextPage, nextPageSize, request.signal);
                  if (!request.current()) return false;
                  items.value = result.items;
                  page.value = result.page;
                  pageSize.value = result.pageSize;
                  total.value = result.total;
                  return true;
                } catch (problem: unknown) {
                  if (!request.current()) return false;
                  dependencies.onProblem(
                    problem,
                    'client.{{problemPrefix}}_load_failed'
                  );
                  return false;
                } finally {
                  if (request.current()) loading.value = false; request.finish();
                }
              }

              async function create(
                input: Create{{schema.ClrTypeName}}Request
              ): Promise<boolean> {
                if (!canWrite.value || changing.value) return false;
                const request = beginRequest({{entityVariable}}Permissions.write);
                if (!request) return false;
                changeRequest = request;
                changing.value = true;
                try {
                  await api.create(input, request.signal);
                  if (!request.current()) return false;
                  await load();
                  return request.current();
                } catch (problem: unknown) {
                  if (!request.current()) return false;
                  dependencies.onProblem(
                    problem,
                    'client.{{problemPrefix}}_operation_failed'
                  );
                  return false;
                } finally {
                  if (request.current()) changing.value = false; request.finish();
                }
              }

              async function update(
                item: {{schema.ClrTypeName}}Response,
                input: {{schema.ClrTypeName}}PageUpdate
              ): Promise<boolean> {
                if (!canWrite.value || changing.value) return false;
                if (!isCurrentItem(item)) return false;
                const request = beginRequest({{entityVariable}}Permissions.write);
                if (!request) return false;
                changeRequest = request;
                changing.value = true;
                try {
            {{IndentLines(updateCall, 6)}}
                  if (!request.current()) return false;
                  await load();
                  return request.current();
                } catch (problem: unknown) {
                  if (!request.current()) return false;
                  dependencies.onProblem(
                    problem,
                    'client.{{problemPrefix}}_operation_failed'
                  );
                  return false;
                } finally {
                  if (request.current()) changing.value = false; request.finish();
                }
              }

              async function disable(
                item: {{schema.ClrTypeName}}Response
              ): Promise<boolean> {
                if (!canWrite.value || changing.value) return false;
                if (!isCurrentItem(item)) return false;
                const request = beginRequest({{entityVariable}}Permissions.write);
                if (!request) return false;
                changeRequest = request;
                changing.value = true;
                try {
            {{IndentLines(disableCall, 6)}}
                  if (!request.current()) return false;
                  await load();
                  return request.current();
                } catch (problem: unknown) {
                  if (!request.current()) return false;
                  dependencies.onProblem(
                    problem,
                    'client.{{problemPrefix}}_operation_failed'
                  );
                  return false;
                } finally {
                  if (request.current()) changing.value = false; request.finish();
                }
              }

              return {
                items: readonly(items),
                page: readonly(page),
                pageSize: readonly(pageSize),
                total: readonly(total),
                loading: readonly(loading),
                changing: readonly(changing),
                scopeVersion: readonly(scopeVersion),
                cancelChange,
                canRead,
                canWrite,
                load,
                create,
                update,
                disable
              };
            }
            """);
    }

    /// <summary>
    /// 生成由 Layui 宿主订阅状态快照并负责渲染的无 DOM 页面模型。
    /// </summary>
    internal static string GenerateLayui(FullNetCrudSchema schema)
    {
        ArgumentNullException.ThrowIfNull(schema);
        if (!schema.UsesLegacyEntityCapabilities)
        {
            return GenerateExplicitLayui(schema);
        }

        var entityVariable = LowerFirst(schema.ClrTypeName);
        var apiFactoryName = HttpSegmentToPascalCase(schema.ApiResourceName);
        var problemPrefix = ProblemCodePrefix(schema);
        var idProperty = JsonProperty(schema, "Id");
        var versionProperty = schema.HasVersion
            ? JsonProperty(schema, "Version")
            : null;
        var updateCall = schema.HasVersion
            ? $$"""
            await api.update(item.{{idProperty}}, {
              ...input,
              {{versionProperty}}: item.{{versionProperty}}
            });
            """
            : $"await api.update(item.{idProperty}, input);";
        var disableCall = schema.HasVersion
            ? $$"""
            await api.disable(item.{{idProperty}}, {
              {{versionProperty}}: item.{{versionProperty}}
            });
            """
            : $"await api.disable(item.{idProperty});";

        return Normalize(
            $$"""
            import {
              create{{apiFactoryName}}Api,
              {{entityVariable}}Permissions
            } from './{{schema.ApiResourceName}}.generated.js';

            export function create{{schema.ClrTypeName}}PageModel(options) {
              const api = create{{apiFactoryName}}Api(options.request);
              let items = [];
              let page = 1;
              let pageSize = 20;
              let total = 0;
              let loading = false;
              let changing = false;

              const canRead = () =>
                Boolean(options.hasPermission({{entityVariable}}Permissions.read));
              const canWrite = () =>
                Boolean(options.hasPermission({{entityVariable}}Permissions.write));

              function getState() {
                return Object.freeze({
                  items: Object.freeze([...items]),
                  page,
                  pageSize,
                  total,
                  loading,
                  changing,
                  canRead: canRead(),
                  canWrite: canWrite()
                });
              }

              function publish() {
                options.onStateChange?.(getState());
              }

              async function load(nextPage = page, nextPageSize = pageSize) {
                if (!canRead() || loading) return false;
                loading = true;
                publish();
                try {
                  const result = await api.list(nextPage, nextPageSize);
                  items = Array.isArray(result?.items) ? result.items : [];
                  page = result?.page ?? nextPage;
                  pageSize = result?.pageSize ?? nextPageSize;
                  total = result?.total ?? 0;
                  return true;
                } catch (problem) {
                  options.onProblem(
                    problem,
                    'client.{{problemPrefix}}_load_failed'
                  );
                  return false;
                } finally {
                  loading = false;
                  publish();
                }
              }

              async function create(input) {
                if (!canWrite() || changing) return false;
                changing = true;
                publish();
                try {
                  await api.create(input);
                  await load();
                  return true;
                } catch (problem) {
                  options.onProblem(
                    problem,
                    'client.{{problemPrefix}}_operation_failed'
                  );
                  return false;
                } finally {
                  changing = false;
                  publish();
                }
              }

              async function update(item, input) {
                if (!canWrite() || changing) return false;
                changing = true;
                publish();
                try {
            {{IndentLines(updateCall, 6)}}
                  await load();
                  return true;
                } catch (problem) {
                  options.onProblem(
                    problem,
                    'client.{{problemPrefix}}_operation_failed'
                  );
                  return false;
                } finally {
                  changing = false;
                  publish();
                }
              }

              async function disable(item) {
                if (!canWrite() || changing) return false;
                changing = true;
                publish();
                try {
            {{IndentLines(disableCall, 6)}}
                  await load();
                  return true;
                } catch (problem) {
                  options.onProblem(
                    problem,
                    'client.{{problemPrefix}}_operation_failed'
                  );
                  return false;
                } finally {
                  changing = false;
                  publish();
                }
              }

              return Object.freeze({
                getState,
                load,
                create,
                update,
                disable
              });
            }
            """);
    }

    private static string ProblemCodePrefix(FullNetCrudSchema schema) =>
        $"{schema.ModuleKey}_{schema.ApiResourceName.Replace('-', '_')}";

    private static string GenerateExplicitVue(FullNetCrudSchema schema)
    {
        var entityVariable = LowerFirst(schema.ClrTypeName);
        var apiFactoryName = HttpSegmentToPascalCase(schema.ApiResourceName);
        var problemPrefix = ProblemCodePrefix(schema);
        var idProperty = JsonProperty(schema, "Id");
        var versionProperty = schema.HasVersion
            ? JsonProperty(schema, "Version")
            : null;
        var updateImport = schema.EntityCapabilities.CanUpdate
            ? $",\n  Update{schema.ClrTypeName}Request"
            : string.Empty;
        var updateAlias = schema.EntityCapabilities.CanUpdate
            ? "\n\n" + (schema.HasVersion
                ? $"export type {schema.ClrTypeName}PageUpdate = "
                    + $"Omit<Update{schema.ClrTypeName}Request, "
                    + $"'{versionProperty}'>;"
                : $"export type {schema.ClrTypeName}PageUpdate = "
                    + $"Update{schema.ClrTypeName}Request;")
            : string.Empty;
        var updateAction = schema.EntityCapabilities.CanUpdate
            ? GenerateExplicitVueUpdateAction(
                schema,
                idProperty,
                versionProperty,
                problemPrefix)
            : string.Empty;
        var deleteAction = schema.EntityCapabilities.CanDelete
            ? GenerateExplicitVueDeleteAction(
                schema,
                idProperty,
                versionProperty,
                problemPrefix)
            : string.Empty;
        var returnedActions = string.Concat(
            schema.EntityCapabilities.CanUpdate ? ",\n    update" : string.Empty,
            schema.EntityCapabilities.CanDelete ? ",\n    remove" : string.Empty);

        return Normalize(
            $$"""
            import { computed, onActivated, onBeforeUnmount, onDeactivated, readonly, ref, toRaw, watch } from 'vue';
            import {
              create{{apiFactoryName}}Api,
              {{entityVariable}}Permissions
            } from './{{schema.ApiResourceName}}.generated';
            import type {
              Create{{schema.ClrTypeName}}Request,
              GeneratedRequest,
              {{schema.ClrTypeName}}Response{{updateImport}}
            } from './{{schema.ApiResourceName}}.generated';{{updateAlias}}

            export type {{schema.ClrTypeName}}PageProblemCode =
              | 'client.{{problemPrefix}}_load_failed'
              | 'client.{{problemPrefix}}_operation_failed';

            export interface {{schema.ClrTypeName}}PageDependencies {
              request: GeneratedRequest;
              contextKey: () => string;
              hasPermission: (permission: string) => boolean;
              onProblem: (
                problem: unknown,
                fallbackCode: {{schema.ClrTypeName}}PageProblemCode
              ) => void;
            }

            export function use{{schema.ClrTypeName}}Page(
              dependencies: {{schema.ClrTypeName}}PageDependencies
            ) {
              const api = create{{apiFactoryName}}Api(dependencies.request);
              const items = ref<{{schema.ClrTypeName}}Response[]>([]);
              const page = ref(1);
              const pageSize = ref(20);
              const total = ref(0);
              const loading = ref(false);
              const changing = ref(false);
              const canRead = computed(() =>
                dependencies.hasPermission({{entityVariable}}Permissions.read)
              );
              const canCreate = computed(() =>
                dependencies.hasPermission({{entityVariable}}Permissions.create)
              );
              const canUpdate = computed(() =>
                dependencies.hasPermission({{entityVariable}}Permissions.update)
              );
              const canDisable = computed(() =>
                dependencies.hasPermission({{entityVariable}}Permissions.disable)
              );
              const canWrite = canUpdate;

            {{IndentLines(GenerateVueLifecycle(schema).ReplaceLineEndings("\n"), 2).Replace("\n  \n", "\n\n", StringComparison.Ordinal)}}

              async function load(
                nextPage = page.value,
                nextPageSize = pageSize.value
              ): Promise<boolean> {
                if (!canRead.value || loading.value) return false;
                const request = beginRequest({{entityVariable}}Permissions.read);
                if (!request) return false;
                loading.value = true;
                try {
                  const result = await api.list(nextPage, nextPageSize, request.signal);
                  if (!request.current()) return false;
                  items.value = result.items;
                  page.value = result.page;
                  pageSize.value = result.pageSize;
                  total.value = result.total;
                  return true;
                } catch (problem: unknown) {
                  if (!request.current()) return false;
                  dependencies.onProblem(
                    problem,
                    'client.{{problemPrefix}}_load_failed'
                  );
                  return false;
                } finally {
                  if (request.current()) loading.value = false; request.finish();
                }
              }

              async function create(
                input: Create{{schema.ClrTypeName}}Request
              ): Promise<boolean> {
                if (!canCreate.value || changing.value) return false;
                const request = beginRequest({{entityVariable}}Permissions.create);
                if (!request) return false;
                changeRequest = request;
                changing.value = true;
                try {
                  await api.create(input, request.signal);
                  if (!request.current()) return false;
                  await load();
                  return request.current();
                } catch (problem: unknown) {
                  if (!request.current()) return false;
                  dependencies.onProblem(
                    problem,
                    'client.{{problemPrefix}}_operation_failed'
                  );
                  return false;
                } finally {
                  if (request.current()) changing.value = false; request.finish();
                }
              }
            {{updateAction}}{{deleteAction}}

              return {
                items: readonly(items),
                page: readonly(page),
                pageSize: readonly(pageSize),
                total: readonly(total),
                loading: readonly(loading),
                changing: readonly(changing),
                scopeVersion: readonly(scopeVersion),
                cancelChange,
                canRead,
                canCreate,
                canUpdate,
                canDisable,
                canWrite,
                load,
                create{{returnedActions}}
              };
            }
            """);
    }

    private static string GenerateExplicitVueUpdateAction(
        FullNetCrudSchema schema,
        string idProperty,
        string? versionProperty,
        string problemPrefix)
    {
        var call = schema.HasVersion
            ? $$"""
            await api.update(item.{{idProperty}}, {
              ...input,
              {{versionProperty}}: item.{{versionProperty}}
            }, request.signal);
            """
            : $"await api.update(item.{idProperty}, input, request.signal);";
        return "\n\n" + IndentLines(
            $$"""
            async function update(
              item: {{schema.ClrTypeName}}Response,
              input: {{schema.ClrTypeName}}PageUpdate
            ): Promise<boolean> {
              if (!canUpdate.value || changing.value) return false;
              if (!isCurrentItem(item)) return false;
              const request = beginRequest({{LowerFirst(schema.ClrTypeName)}}Permissions.update);
              if (!request) return false;
              changeRequest = request;
              changing.value = true;
              try {
            {{IndentLines(call, 4)}}
                if (!request.current()) return false;
                await load();
                return request.current();
              } catch (problem: unknown) {
                if (!request.current()) return false;
                dependencies.onProblem(
                  problem,
                  'client.{{problemPrefix}}_operation_failed'
                );
                return false;
              } finally {
                if (request.current()) changing.value = false; request.finish();
              }
            }
            """,
            2);
    }

    private static string GenerateExplicitVueDeleteAction(
        FullNetCrudSchema schema,
        string idProperty,
        string? versionProperty,
        string problemPrefix)
    {
        var call = schema.HasVersion
            ? $$"""
            await api.delete(item.{{idProperty}}, {
              {{versionProperty}}: item.{{versionProperty}}
            }, request.signal);
            """
            : $"await api.delete(item.{idProperty}, request.signal);";
        return "\n\n" + IndentLines(
            $$"""
            async function remove(
              item: {{schema.ClrTypeName}}Response
            ): Promise<boolean> {
              if (!canDisable.value || changing.value) return false;
              if (!isCurrentItem(item)) return false;
              const request = beginRequest({{LowerFirst(schema.ClrTypeName)}}Permissions.disable);
              if (!request) return false;
              changeRequest = request;
              changing.value = true;
              try {
            {{IndentLines(call, 4)}}
                if (!request.current()) return false;
                await load();
                return request.current();
              } catch (problem: unknown) {
                if (!request.current()) return false;
                dependencies.onProblem(
                  problem,
                  'client.{{problemPrefix}}_operation_failed'
                );
                return false;
              } finally {
                if (request.current()) changing.value = false; request.finish();
              }
            }
            """,
            2);
    }

    private static string GenerateExplicitLayui(FullNetCrudSchema schema)
    {
        var entityVariable = LowerFirst(schema.ClrTypeName);
        var apiFactoryName = HttpSegmentToPascalCase(schema.ApiResourceName);
        var problemPrefix = ProblemCodePrefix(schema);
        var idProperty = JsonProperty(schema, "Id");
        var versionProperty = schema.HasVersion
            ? JsonProperty(schema, "Version")
            : null;
        var updateAction = schema.EntityCapabilities.CanUpdate
            ? GenerateExplicitLayuiUpdateAction(
                schema,
                idProperty,
                versionProperty,
                problemPrefix)
            : string.Empty;
        var deleteAction = schema.EntityCapabilities.CanDelete
            ? GenerateExplicitLayuiDeleteAction(
                schema,
                idProperty,
                versionProperty,
                problemPrefix)
            : string.Empty;
        var returnedActions = string.Concat(
            schema.EntityCapabilities.CanUpdate ? ",\n    update" : string.Empty,
            schema.EntityCapabilities.CanDelete ? ",\n    remove" : string.Empty);

        return Normalize(
            $$"""
            import {
              create{{apiFactoryName}}Api,
              {{entityVariable}}Permissions
            } from './{{schema.ApiResourceName}}.generated.js';

            export function create{{schema.ClrTypeName}}PageModel(options) {
              const api = create{{apiFactoryName}}Api(options.request);
              let items = [];
              let page = 1;
              let pageSize = 20;
              let total = 0;
              let loading = false;
              let changing = false;

              const canRead = () =>
                Boolean(options.hasPermission({{entityVariable}}Permissions.read));
              const canWrite = () =>
                Boolean(options.hasPermission({{entityVariable}}Permissions.write));

              function getState() {
                return Object.freeze({
                  items: Object.freeze([...items]),
                  page,
                  pageSize,
                  total,
                  loading,
                  changing,
                  canRead: canRead(),
                  canWrite: canWrite()
                });
              }

              function publish() {
                options.onStateChange?.(getState());
              }

              async function load(nextPage = page, nextPageSize = pageSize) {
                if (!canRead() || loading) return false;
                loading = true;
                publish();
                try {
                  const result = await api.list(nextPage, nextPageSize);
                  items = Array.isArray(result?.items) ? result.items : [];
                  page = result?.page ?? nextPage;
                  pageSize = result?.pageSize ?? nextPageSize;
                  total = result?.total ?? 0;
                  return true;
                } catch (problem) {
                  options.onProblem(
                    problem,
                    'client.{{problemPrefix}}_load_failed'
                  );
                  return false;
                } finally {
                  loading = false;
                  publish();
                }
              }

              async function create(input) {
                if (!canWrite() || changing) return false;
                changing = true;
                publish();
                try {
                  await api.create(input);
                  await load();
                  return true;
                } catch (problem) {
                  options.onProblem(
                    problem,
                    'client.{{problemPrefix}}_operation_failed'
                  );
                  return false;
                } finally {
                  changing = false;
                  publish();
                }
              }
            {{updateAction}}{{deleteAction}}

              return Object.freeze({
                getState,
                load,
                create{{returnedActions}}
              });
            }
            """);
    }

    private static string GenerateExplicitLayuiUpdateAction(
        FullNetCrudSchema schema,
        string idProperty,
        string? versionProperty,
        string problemPrefix)
    {
        var call = schema.HasVersion
            ? $$"""
            await api.update(item.{{idProperty}}, {
              ...input,
              {{versionProperty}}: item.{{versionProperty}}
            });
            """
            : $"await api.update(item.{idProperty}, input);";
        return "\n\n" + IndentLines(
            $$"""
            async function update(item, input) {
              if (!canWrite() || changing) return false;
              changing = true;
              publish();
              try {
            {{IndentLines(call, 4)}}
                await load();
                return true;
              } catch (problem) {
                options.onProblem(
                  problem,
                  'client.{{problemPrefix}}_operation_failed'
                );
                return false;
              } finally {
                changing = false;
                publish();
              }
            }
            """,
            2);
    }

    private static string GenerateExplicitLayuiDeleteAction(
        FullNetCrudSchema schema,
        string idProperty,
        string? versionProperty,
        string problemPrefix)
    {
        var call = schema.HasVersion
            ? $$"""
            await api.delete(item.{{idProperty}}, {
              {{versionProperty}}: item.{{versionProperty}}
            });
            """
            : $"await api.delete(item.{idProperty});";
        return "\n\n" + IndentLines(
            $$"""
            async function remove(item) {
              if (!canWrite() || changing) return false;
              changing = true;
              publish();
              try {
            {{IndentLines(call, 4)}}
                await load();
                return true;
              } catch (problem) {
                options.onProblem(
                  problem,
                  'client.{{problemPrefix}}_operation_failed'
                );
                return false;
              } finally {
                changing = false;
                publish();
              }
            }
            """,
            2);
    }

    /// <summary>把生成页面的请求、缓存行与动作状态限制在当前上下文和激活代次。</summary>
    private static string GenerateVueLifecycle(FullNetCrudSchema schema)
    {
        var permissionValues = schema.UsesLegacyEntityCapabilities
            ? "canRead.value, canWrite.value"
            : "canRead.value, canCreate.value, canUpdate.value, canDisable.value";
        return $$"""
            const scopeVersion = ref(0);
            let active = true;
            const controllers = new Set<AbortController>();
            let changeRequest: ReturnType<typeof beginRequest>;

            // 取消只终止客户端接入；服务端可能已经提交，恢复后从权威列表读取。
            function beginRequest(permission: string) {
              if (!active || !dependencies.hasPermission(permission)) return undefined;
              const ticket = scopeVersion.value;
              const controller = new AbortController();
              controllers.add(controller);
              return {
                signal: controller.signal,
                current: () => active && ticket === scopeVersion.value && !controller.signal.aborted
                  && dependencies.hasPermission(permission),
                cancel: () => { controller.abort(); controllers.delete(controller); },
                finish: () => controllers.delete(controller)
              };
            }

            function cancelChange(): void {
              changeRequest?.cancel(); changeRequest = undefined; changing.value = false;
            }

            function reset(): void {
              for (const controller of controllers) controller.abort();
              controllers.clear(); changeRequest = undefined;
              items.value = []; page.value = 1; pageSize.value = 20; total.value = 0;
              loading.value = false; changing.value = false; scopeVersion.value++;
            }

            function isCurrentItem(item: {{schema.ClrTypeName}}Response): boolean {
              return items.value.some(candidate => toRaw(candidate) === toRaw(item));
            }

            // 同步失效阻止旧 Promise continuation；同轮上下文替换只恢复最终代次。
            watch(() => JSON.stringify([dependencies.contextKey(), {{permissionValues}}]), () => {
              reset(); const ticket = scopeVersion.value;
              queueMicrotask(() => { if (active && ticket === scopeVersion.value) void load(); });
            }, { flush: 'sync' });
            const suspend = () => { active = false; reset(); };
            onDeactivated(suspend);
            onBeforeUnmount(suspend);
            onActivated(() => { if (!active) { active = true; void load(); } });
            """;
    }

    private static string JsonProperty(
        FullNetCrudSchema schema,
        string databaseName) =>
        schema.Columns.Single(column =>
            column.DatabaseName == databaseName).JsonPropertyName;

    private static string HttpSegmentToPascalCase(string value) =>
        string.Concat(value.Split('-', StringSplitOptions.None).Select(UpperFirst));

    private static string UpperFirst(string value) =>
        string.Concat(char.ToUpperInvariant(value[0]), value[1..]);

    private static string LowerFirst(string value) =>
        string.Concat(char.ToLowerInvariant(value[0]), value[1..]);

    private static string IndentLines(string content, int spaces)
    {
        var indentation = new string(' ', spaces);
        return indentation + content.Replace(
            "\n",
            $"\n{indentation}",
            StringComparison.Ordinal);
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
