import { computed, readonly, ref } from 'vue';
import {
  createProductsApi,
  productPermissions,
  type CreateProductRequest,
  type GeneratedRequest,
  type ProductResponse,
  type UpdateProductRequest
} from './products.generated';

export type ProductPageUpdate = Omit<UpdateProductRequest, 'version'>;

export type ProductPageProblemCode =
  | 'client.catalog_products_load_failed'
  | 'client.catalog_products_operation_failed';

export interface ProductPageDependencies {
  request: GeneratedRequest;
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
        'client.catalog_products_load_failed'
      );
      return false;
    } finally {
      loading.value = false;
    }
  }

  async function create(
    input: CreateProductRequest
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
        'client.catalog_products_operation_failed'
      );
      return false;
    } finally {
      changing.value = false;
    }
  }

  async function update(
    item: ProductResponse,
    input: ProductPageUpdate
  ): Promise<boolean> {
    if (!canWrite.value || changing.value) return false;
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
        'client.catalog_products_operation_failed'
      );
      return false;
    } finally {
      changing.value = false;
    }
  }

  async function disable(
    item: ProductResponse
  ): Promise<boolean> {
    if (!canWrite.value || changing.value) return false;
    changing.value = true;
    try {
      await api.disable(item.id, {
        version: item.version
      });
      await load();
      return true;
    } catch (problem: unknown) {
      dependencies.onProblem(
        problem,
        'client.catalog_products_operation_failed'
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
