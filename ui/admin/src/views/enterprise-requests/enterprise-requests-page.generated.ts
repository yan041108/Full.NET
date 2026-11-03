import { computed, readonly, ref } from 'vue';
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
  hasPermission: (permission: string) => boolean;
  onProblem: (
    problem: unknown,
    fallbackCode: EnterpriseRequestPageProblemCode
  ) => void;
}

export function useEnterpriseRequestPage(
  dependencies: EnterpriseRequestPageDependencies
) {
  let generation = 0, cancellation = new AbortController();
  const api = createEnterpriseRequestsApi(dependencies.request, () => cancellation.signal);
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
  const canSubmit = computed(() =>
    dependencies.hasPermission(enterpriseRequestPermissions.submit)
  );

  /** 会话切换作废所有在途读写结果；取消请求不等于撤销已经提交到服务端的业务。 */
  function reset(): void {
    generation++; cancellation.abort(); cancellation = new AbortController();
    items.value = []; page.value = 1; total.value = 0; loading.value = false; changing.value = false;
  }

  async function load(
    nextPage = page.value,
    nextPageSize = pageSize.value
  ): Promise<boolean> {
    if (!canRead.value || loading.value) return false;
    const current = generation;
    loading.value = true;
    try {
      const result = await api.list(nextPage, nextPageSize);
      if (current !== generation) return false;
      items.value = result.items;
      page.value = result.page;
      pageSize.value = result.pageSize;
      total.value = result.total;
      return true;
    } catch (problem: unknown) {
      if (current === generation) dependencies.onProblem(
        problem,
        'client.enterprise_request_enterprise_requests_load_failed'
      );
      return false;
    } finally {
      if (current === generation) loading.value = false;
    }
  }

  async function create(
    input: CreateEnterpriseRequestInput
  ): Promise<boolean> {
    return change(canCreate.value, () => api.create(input));
  }


  async function update(
    item: EnterpriseRequestResponse,
    input: EnterpriseRequestPageUpdate
  ): Promise<boolean> {
    return change(canUpdate.value, () => api.update(item.id, { ...input, version: item.version }));
  }

  async function submitForApproval(
    item: EnterpriseRequestResponse
  ): Promise<boolean> {
    if (item.status !== 'Draft') return false;
    return change(canSubmit.value, () => api.submitForApproval(item.id));
  }

  async function remove(
    item: EnterpriseRequestResponse
  ): Promise<boolean> {
    return change(canDisable.value, () => api.delete(item.id, { version: item.version }));
  }

  async function change(allowed: boolean, action: () => Promise<unknown>): Promise<boolean> {
    if (!allowed || changing.value) return false;
    const current = generation;
    changing.value = true;
    try {
      await action();
      if (current !== generation) return false;
      await load();
      return current === generation;
    } catch (problem: unknown) {
      if (current === generation) dependencies.onProblem(
        problem,
        'client.enterprise_request_enterprise_requests_operation_failed'
      );
      return false;
    } finally {
      if (current === generation) changing.value = false;
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
    canCreate,
    canUpdate,
    canDisable,
    canWrite,
    canSubmit,
    reset,
    load,
    create,
    update,
    remove,
    submitForApproval
  };
}
