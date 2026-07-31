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
            import { computed, readonly, ref } from 'vue';
            import {
              create{{apiFactoryName}}Api,
              {{entityVariable}}Permissions,
              type Create{{schema.ClrTypeName}}Request,
              type GeneratedRequest,
              type {{schema.ClrTypeName}}Response,
              type Update{{schema.ClrTypeName}}Request
            } from './{{schema.ApiResourceName}}.generated';

            export type {{schema.ClrTypeName}}PageUpdate = {{updateType}};

            export type {{schema.ClrTypeName}}PageProblemCode =
              | 'client.{{problemPrefix}}_load_failed'
              | 'client.{{problemPrefix}}_operation_failed';

            export interface {{schema.ClrTypeName}}PageDependencies {
              request: GeneratedRequest;
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

              async function load(
                nextPage = page.value,
                nextPageSize = pageSize.value
              ): Promise<boolean> {
                if (!canRead.value || loading.value) return false;
                loading.value = true;
                try {
                  const result = await api.list(nextPage, nextPageSize);
                  items.value = result.items;
                  page.value = result.page;
                  pageSize.value = result.pageSize;
                  total.value = result.total;
                  return true;
                } catch (problem: unknown) {
                  dependencies.onProblem(
                    problem,
                    'client.{{problemPrefix}}_load_failed'
                  );
                  return false;
                } finally {
                  loading.value = false;
                }
              }

              async function create(
                input: Create{{schema.ClrTypeName}}Request
              ): Promise<boolean> {
                if (!canWrite.value || changing.value) return false;
                changing.value = true;
                try {
                  await api.create(input);
                  await load();
                  return true;
                } catch (problem: unknown) {
                  dependencies.onProblem(
                    problem,
                    'client.{{problemPrefix}}_operation_failed'
                  );
                  return false;
                } finally {
                  changing.value = false;
                }
              }

              async function update(
                item: {{schema.ClrTypeName}}Response,
                input: {{schema.ClrTypeName}}PageUpdate
              ): Promise<boolean> {
                if (!canWrite.value || changing.value) return false;
                changing.value = true;
                try {
            {{IndentLines(updateCall, 6)}}
                  await load();
                  return true;
                } catch (problem: unknown) {
                  dependencies.onProblem(
                    problem,
                    'client.{{problemPrefix}}_operation_failed'
                  );
                  return false;
                } finally {
                  changing.value = false;
                }
              }

              async function disable(
                item: {{schema.ClrTypeName}}Response
              ): Promise<boolean> {
                if (!canWrite.value || changing.value) return false;
                changing.value = true;
                try {
            {{IndentLines(disableCall, 6)}}
                  await load();
                  return true;
                } catch (problem: unknown) {
                  dependencies.onProblem(
                    problem,
                    'client.{{problemPrefix}}_operation_failed'
                  );
                  return false;
                } finally {
                  changing.value = false;
                }
              }

              return {
                items: readonly(items),
                page: readonly(page),
                pageSize: readonly(pageSize),
                total: readonly(total),
                loading: readonly(loading),
                changing: readonly(changing),
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
            ? $",\n  type Update{schema.ClrTypeName}Request"
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
            import { computed, readonly, ref } from 'vue';
            import {
              create{{apiFactoryName}}Api,
              {{entityVariable}}Permissions,
              type Create{{schema.ClrTypeName}}Request,
              type GeneratedRequest,
              type {{schema.ClrTypeName}}Response{{updateImport}}
            } from './{{schema.ApiResourceName}}.generated';{{updateAlias}}

            export type {{schema.ClrTypeName}}PageProblemCode =
              | 'client.{{problemPrefix}}_load_failed'
              | 'client.{{problemPrefix}}_operation_failed';

            export interface {{schema.ClrTypeName}}PageDependencies {
              request: GeneratedRequest;
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

              async function load(
                nextPage = page.value,
                nextPageSize = pageSize.value
              ): Promise<boolean> {
                if (!canRead.value || loading.value) return false;
                loading.value = true;
                try {
                  const result = await api.list(nextPage, nextPageSize);
                  items.value = result.items;
                  page.value = result.page;
                  pageSize.value = result.pageSize;
                  total.value = result.total;
                  return true;
                } catch (problem: unknown) {
                  dependencies.onProblem(
                    problem,
                    'client.{{problemPrefix}}_load_failed'
                  );
                  return false;
                } finally {
                  loading.value = false;
                }
              }

              async function create(
                input: Create{{schema.ClrTypeName}}Request
              ): Promise<boolean> {
                if (!canWrite.value || changing.value) return false;
                changing.value = true;
                try {
                  await api.create(input);
                  await load();
                  return true;
                } catch (problem: unknown) {
                  dependencies.onProblem(
                    problem,
                    'client.{{problemPrefix}}_operation_failed'
                  );
                  return false;
                } finally {
                  changing.value = false;
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
                canRead,
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
            });
            """
            : $"await api.update(item.{idProperty}, input);";
        return "\n\n" + IndentLines(
            $$"""
            async function update(
              item: {{schema.ClrTypeName}}Response,
              input: {{schema.ClrTypeName}}PageUpdate
            ): Promise<boolean> {
              if (!canWrite.value || changing.value) return false;
              changing.value = true;
              try {
            {{IndentLines(call, 4)}}
                await load();
                return true;
              } catch (problem: unknown) {
                dependencies.onProblem(
                  problem,
                  'client.{{problemPrefix}}_operation_failed'
                );
                return false;
              } finally {
                changing.value = false;
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
            });
            """
            : $"await api.delete(item.{idProperty});";
        return "\n\n" + IndentLines(
            $$"""
            async function remove(
              item: {{schema.ClrTypeName}}Response
            ): Promise<boolean> {
              if (!canWrite.value || changing.value) return false;
              changing.value = true;
              try {
            {{IndentLines(call, 4)}}
                await load();
                return true;
              } catch (problem: unknown) {
                dependencies.onProblem(
                  problem,
                  'client.{{problemPrefix}}_operation_failed'
                );
                return false;
              } finally {
                changing.value = false;
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
