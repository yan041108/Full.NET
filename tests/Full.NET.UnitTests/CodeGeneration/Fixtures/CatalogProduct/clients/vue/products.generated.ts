import {
  catalogCreateProduct,
  catalogDisableProduct,
  catalogListProducts,
  catalogUpdateProduct,
  type CreateProductRequest,
  type DisableProductRequest,
  type HttpClient,
  type ProductResponse,
  type UpdateProductRequest
} from '@fullnet/client-contracts';

export {
  type CreateProductRequest,
  type DisableProductRequest,
  type ProductResponse,
  type UpdateProductRequest
} from '@fullnet/client-contracts';

export type GeneratedRequest = HttpClient;

export const productPermissions = {
  read: 'catalog.products.read',
  write: 'catalog.products.write'
} as const;

export function createProductsApi(
  http: GeneratedRequest
) {
  return {
    list: (page = 1, pageSize = 20, signal?: AbortSignal) =>
      catalogListProducts(http, { page, pageSize }, signal),
    create: (input: CreateProductRequest, signal?: AbortSignal) =>
      catalogCreateProduct(http, { body: input }, signal),
    update: (id: string, input: UpdateProductRequest, signal?: AbortSignal) =>
      catalogUpdateProduct(
        http,
        { productId: id, body: input }, signal
      ),
    disable: (id: string, input: DisableProductRequest, signal?: AbortSignal) =>
      catalogDisableProduct(
        http,
        { productId: id, body: input }, signal
      )
  };
}
