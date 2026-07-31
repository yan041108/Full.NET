export const productPermissions = Object.freeze({
  read: 'catalog.products.read',
  write: 'catalog.products.write'
});

export function createProductsApi(request) {
  const basePath = '/api/v1/catalog/products';
  return Object.freeze({
    list(page = 1, pageSize = 20) {
      return request(`${basePath}?page=${page}&pageSize=${pageSize}`);
    },
    create(input) {
      return request(basePath, jsonRequest('POST', input));
    },
    update(id, input) {
      return request(
        `${basePath}/${encodeURIComponent(id)}`,
        jsonRequest('PUT', input)
      );
    },
    disable(id, input) {
      return request(
        `${basePath}/${encodeURIComponent(id)}/disable`,
        jsonRequest('POST', input)
      );
    }
  });
}

function jsonRequest(method, body) {
  return {
    method,
    headers: { 'content-type': 'application/json' },
    body: JSON.stringify(body)
  };
}
