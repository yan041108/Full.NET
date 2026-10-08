import { computed, onActivated, onBeforeUnmount, onDeactivated, readonly, ref, toRaw, watch } from 'vue';
import {
  createProductsApi,
  productPermissions
} from './products.generated';
import type {
  CreateProductRequest,
  GeneratedRequest,
  ProductResponse,
  UpdateProductRequest
} from './products.generated';

export type ProductPageUpdate = Omit<UpdateProductRequest, 'version'>;

export type ProductPageProblemCode =
  | 'client.catalog_products_load_failed'
  | 'client.catalog_products_operation_failed';

export interface ProductPageDependencies {
  request: GeneratedRequest;
  contextKey: () => string;
  hasPermission: (permission: string) => boolean;
  onProblem: (
    problem: unknown,
    fallbackCode: ProductPageProblemCode
  ) => void;
}

export function useProductPage(
  dependencies: ProductPageDependencies
) {
  const api = createProductsApi(dependencies.request);
  const items = ref<ProductResponse[]>([]);
  const page = ref(1);
  const pageSize = ref(20);
  const total = ref(0);
  const loading = ref(false);
  const changing = ref(false);
  const canRead = computed(() =>
    dependencies.hasPermission(productPermissions.read)
  );
  const canWrite = computed(() =>
    dependencies.hasPermission(productPermissions.write)
  );

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

  function isCurrentItem(item: ProductResponse): boolean {
    return items.value.some(candidate => toRaw(candidate) === toRaw(item));
  }

  // 同步失效阻止旧 Promise continuation；同轮上下文替换只恢复最终代次。
  watch(() => JSON.stringify([dependencies.contextKey(), canRead.value, canWrite.value]), () => {
    reset(); const ticket = scopeVersion.value;
    queueMicrotask(() => { if (active && ticket === scopeVersion.value) void load(); });
  }, { flush: 'sync' });
  const suspend = () => { active = false; reset(); };
  onDeactivated(suspend);
  onBeforeUnmount(suspend);
  onActivated(() => { if (!active) { active = true; void load(); } });

  async function load(
    nextPage = page.value,
    nextPageSize = pageSize.value
  ): Promise<boolean> {
    if (!canRead.value || loading.value) return false;
    const request = beginRequest(productPermissions.read);
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
        'client.catalog_products_load_failed'
      );
      return false;
    } finally {
      if (request.current()) loading.value = false; request.finish();
    }
  }

  async function create(
    input: CreateProductRequest
  ): Promise<boolean> {
    if (!canWrite.value || changing.value) return false;
    const request = beginRequest(productPermissions.write);
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
        'client.catalog_products_operation_failed'
      );
      return false;
    } finally {
      if (request.current()) changing.value = false; request.finish();
    }
  }

  async function update(
    item: ProductResponse,
    input: ProductPageUpdate
  ): Promise<boolean> {
    if (!canWrite.value || changing.value) return false;
    if (!isCurrentItem(item)) return false;
    const request = beginRequest(productPermissions.write);
    if (!request) return false;
    changeRequest = request;
    changing.value = true;
    try {
      await api.update(item.id, {
        ...input,
        version: item.version
      }, request.signal);
      if (!request.current()) return false;
      await load();
      return request.current();
    } catch (problem: unknown) {
      if (!request.current()) return false;
      dependencies.onProblem(
        problem,
        'client.catalog_products_operation_failed'
      );
      return false;
    } finally {
      if (request.current()) changing.value = false; request.finish();
    }
  }

  async function disable(
    item: ProductResponse
  ): Promise<boolean> {
    if (!canWrite.value || changing.value) return false;
    if (!isCurrentItem(item)) return false;
    const request = beginRequest(productPermissions.write);
    if (!request) return false;
    changeRequest = request;
    changing.value = true;
    try {
      await api.disable(item.id, {
        version: item.version
      }, request.signal);
      if (!request.current()) return false;
      await load();
      return request.current();
    } catch (problem: unknown) {
      if (!request.current()) return false;
      dependencies.onProblem(
        problem,
        'client.catalog_products_operation_failed'
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
