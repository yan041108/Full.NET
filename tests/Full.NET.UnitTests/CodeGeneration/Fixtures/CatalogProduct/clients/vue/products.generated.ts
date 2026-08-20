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
    list: (page = 1, pageSize = 20) =>
      catalogListProducts(http, { page, pageSize }),
    create: (input: CreateProductRequest) =>
      catalogCreateProduct(http, { body: input }),
    update: (id: string, input: UpdateProductRequest) =>
      catalogUpdateProduct(
        http,
        { productId: id, body: input }
      ),
    disable: (id: string, input: DisableProductRequest) =>
      catalogDisableProduct(
        http,
        { productId: id, body: input }
      )
  };
}
