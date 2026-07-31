import {
  createProductsApi,
  productPermissions
} from './products.generated.js';

export function createProductPageModel(options) {
  const api = createProductsApi(options.request);
  let items = [];
  let page = 1;
  let pageSize = 20;
  let total = 0;
  let loading = false;
  let changing = false;

  const canRead = () =>
    Boolean(options.hasPermission(productPermissions.read));
  const canWrite = () =>
    Boolean(options.hasPermission(productPermissions.write));

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
        'client.catalog_products_load_failed'
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
        'client.catalog_products_operation_failed'
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
      await api.update(item.id, {
        ...input,
        version: item.version
      });
      await load();
      return true;
    } catch (problem) {
      options.onProblem(
        problem,
        'client.catalog_products_operation_failed'
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
      await api.disable(item.id, {
        version: item.version
      });
      await load();
      return true;
    } catch (problem) {
      options.onProblem(
        problem,
        'client.catalog_products_operation_failed'
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
