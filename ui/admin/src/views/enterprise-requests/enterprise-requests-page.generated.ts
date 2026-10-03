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
        'client.enterprise_request_enterprise_requests_load_failed'
      );
      return false;
    } finally {
      loading.value = false;
    }
  }

  async function create(
    input: CreateEnterpriseRequestInput
  ): Promise<boolean> {
    if (!canCreate.value || changing.value) return false;
    changing.value = true;
    try {
      await api.create(input);
      await load();
      return true;
    } catch (problem: unknown) {
      dependencies.onProblem(
        problem,
        'client.enterprise_request_enterprise_requests_operation_failed'
      );
      return false;
    } finally {
      changing.value = false;
    }
  }


  async function update(
    item: EnterpriseRequestResponse,
    input: EnterpriseRequestPageUpdate
  ): Promise<boolean> {
    if (!canUpdate.value || changing.value) return false;
    changing.value = true;
    try {
      await api.update(item.id, {
        ...input,
        version: item.version
      });
      await load();
      return true;
    } catch (problem: unknown) {
      dependencies.onProblem(
        problem,
        'client.enterprise_request_enterprise_requests_operation_failed'
      );
      return false;
    } finally {
      changing.value = false;
    }
  }

  async function submitForApproval(
    item: EnterpriseRequestResponse
  ): Promise<boolean> {
    if (!canUpdate.value || changing.value) return false;
    if (item.status !== 'Draft') return false;
    changing.value = true;
    try {
      await api.submitForApproval(item.id);
      await load();
      return true;
    } catch (problem: unknown) {
      dependencies.onProblem(
        problem,
        'client.enterprise_request_enterprise_requests_operation_failed'
      );
      return false;
    } finally {
      changing.value = false;
    }
  }

  async function remove(
    item: EnterpriseRequestResponse
  ): Promise<boolean> {
    if (!canDisable.value || changing.value) return false;
    changing.value = true;
    try {
      await api.delete(item.id, {
        version: item.version
      });
      await load();
      return true;
    } catch (problem: unknown) {
      dependencies.onProblem(
        problem,
        'client.enterprise_request_enterprise_requests_operation_failed'
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
    canCreate,
    canUpdate,
    canDisable,
    canWrite,
    load,
    create,
    update,
    remove,
    submitForApproval
  };
}
