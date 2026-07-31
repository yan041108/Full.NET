export interface ProductResponse {
  id: string;
  tenantId: string;
  displayName: string;
  description: string | null;
  isActive: boolean;
  version: string;
  createdAtUtc: string;
}

export interface CreateProductRequest {
  displayName: string;
  description: string | null;
  isActive: boolean;
}

export interface UpdateProductRequest {
  displayName: string;
  description: string | null;
  isActive: boolean;
  version: string;
}

export interface DisableProductRequest {
  version: string;
}

export interface GeneratedPage<T> {
  items: T[];
  page: number;
  pageSize: number;
  total: number;
}

export type GeneratedRequest = <T>(
  path: string,
  init?: RequestInit
) => Promise<T>;

export const productPermissions = {
  read: 'catalog.products.read',
  write: 'catalog.products.write'
} as const;

export function createProductsApi(
  request: GeneratedRequest
) {
  const basePath = '/api/v1/catalog/products';
  return {
    list: (page = 1, pageSize = 20) =>
      request<GeneratedPage<ProductResponse>>(
        `${basePath}?page=${page}&pageSize=${pageSize}`
      ),
    create: (input: CreateProductRequest) =>
      request<ProductResponse>(basePath, jsonRequest('POST', input)),
    update: (id: string, input: UpdateProductRequest) =>
      request<ProductResponse>(
        `${basePath}/${encodeURIComponent(id)}`,
        jsonRequest('PUT', input)
      ),
    disable: (id: string, input: DisableProductRequest) =>
      request<ProductResponse>(
        `${basePath}/${encodeURIComponent(id)}/disable`,
        jsonRequest('POST', input)
      )
  };
}

function jsonRequest(method: 'POST' | 'PUT', body: unknown): RequestInit {
  return {
    method,
    headers: { 'content-type': 'application/json' },
    body: JSON.stringify(body)
  };
}
