import { computed, onActivated, onBeforeUnmount, onDeactivated, readonly, ref, toRaw, watch } from 'vue';
import {
  createEnterpriseRequestsApi,
  enterpriseRequestPermissions,
  type CreateEnterpriseRequestInput,
  type GeneratedRequest,
  type EnterpriseRequestResponse,
  type UpdateEnterpriseRequestRequest
} from './enterprise-requests.generated';

export type EnterpriseRequestPageUpdate = Omit<UpdateEnterpriseRequestRequest, 'version'>;

export type EnterpriseRequestPageProblemCode =
  | 'client.enterprise_request_enterprise_requests_load_failed'
  | 'client.enterprise_request_enterprise_requests_operation_failed';

export interface EnterpriseRequestPageDependencies {
  request: GeneratedRequest;
  contextKey: () => string;
  hasPermission: (permission: string) => boolean;
  onProblem: (
    problem: unknown,
    fallbackCode: EnterpriseRequestPageProblemCode
  ) => void;
}

export function useEnterpriseRequestPage(
  dependencies: EnterpriseRequestPageDependencies
) {
  const api = createEnterpriseRequestsApi(dependencies.request);
  const items = ref<EnterpriseRequestResponse[]>([]);
  const page = ref(1);
  const pageSize = ref(20);
  const total = ref(0);
  const loading = ref(false);
  const changing = ref(false);
  const canRead = computed(() =>
    dependencies.hasPermission(enterpriseRequestPermissions.read)
  );
  const canCreate = computed(() =>
    dependencies.hasPermission(enterpriseRequestPermissions.create)
  );
  const canUpdate = computed(() =>
    dependencies.hasPermission(enterpriseRequestPermissions.update)
  );
  const canDisable = computed(() =>
    dependencies.hasPermission(enterpriseRequestPermissions.disable)
  );
  const canWrite = canUpdate;
  const canSubmit = computed(() => dependencies.hasPermission(enterpriseRequestPermissions.submit));


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

  function isCurrentItem(item: EnterpriseRequestResponse): boolean {
    return items.value.some(candidate => toRaw(candidate) === toRaw(item));
  }

  // 同步失效阻止旧 Promise continuation；同轮上下文替换只恢复最终代次。
  watch(() => JSON.stringify([dependencies.contextKey(), canRead.value, canCreate.value, canUpdate.value, canDisable.value, canSubmit.value]), () => {
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
    const request = beginRequest(enterpriseRequestPermissions.read);
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
        'client.enterprise_request_enterprise_requests_load_failed'
      );
      return false;
    } finally {
      if (request.current()) loading.value = false; request.finish();
    }
  }

  async function create(
    input: CreateEnterpriseRequestInput
  ): Promise<boolean> {
    if (!canCreate.value || changing.value) return false;
    const request = beginRequest(enterpriseRequestPermissions.create);
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
        'client.enterprise_request_enterprise_requests_operation_failed'
      );
      return false;
    } finally {
      if (request.current()) changing.value = false; request.finish();
    }
  }


  async function update(
    item: EnterpriseRequestResponse,
    input: EnterpriseRequestPageUpdate
  ): Promise<boolean> {
    if (!canUpdate.value || changing.value) return false;
    if (!isCurrentItem(item)) return false;
    const request = beginRequest(enterpriseRequestPermissions.update);
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
        'client.enterprise_request_enterprise_requests_operation_failed'
      );
      return false;
    } finally {
      if (request.current()) changing.value = false; request.finish();
    }
  }

  async function submitForApproval(
    item: EnterpriseRequestResponse
  ): Promise<boolean> {
    if (!canSubmit.value || changing.value) return false;
    if (item.status !== 'Draft') return false;
    if (!isCurrentItem(item)) return false;
    const request = beginRequest(enterpriseRequestPermissions.submit);
    if (!request) return false;
    changeRequest = request;
    changing.value = true;
    try {
      await api.submitForApproval(item.id, request.signal);
      if (!request.current()) return false;
      await load();
      return request.current();
    } catch (problem: unknown) {
      if (!request.current()) return false;
      dependencies.onProblem(
        problem,
        'client.enterprise_request_enterprise_requests_operation_failed'
      );
      return false;
    } finally {
      if (request.current()) changing.value = false; request.finish();
    }
  }

  async function remove(
    item: EnterpriseRequestResponse
  ): Promise<boolean> {
    if (!canDisable.value || changing.value) return false;
    if (!isCurrentItem(item)) return false;
    const request = beginRequest(enterpriseRequestPermissions.disable);
    if (!request) return false;
    changeRequest = request;
    changing.value = true;
    try {
      await api.delete(item.id, {
        version: item.version
      }, request.signal);
      if (!request.current()) return false;
      await load();
      return request.current();
    } catch (problem: unknown) {
      if (!request.current()) return false;
      dependencies.onProblem(
        problem,
        'client.enterprise_request_enterprise_requests_operation_failed'
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
    canCreate,
    canUpdate,
    canDisable,
    canWrite,
    canSubmit,
    load,
    create,
    update,
    remove,
    submitForApproval
  };
}
