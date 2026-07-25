export const FULLNET_API_TITLE = 'Full.NET API';

export const FULLNET_OPENAPI_DOCUMENT_NAME = 'v1';

export const FULLNET_OPENAPI_JSON_PATH = '/openapi/v1.json';

export const FULLNET_SCALAR_UI_PATH = '/scalar/v1';

export interface PlatformApiDocumentationCatalog {
  apiTitle: string;
  documentName: string;
  openApiJsonPath: string;
  scalarUiPath: string;
  securitySchemeName: string;
  securitySchemeType: string;
  securitySchemeScheme: string;
}

export function isPlatformApiDocumentationCatalog(
  value: unknown
): value is PlatformApiDocumentationCatalog {
  return isRecord(value)
    && value.apiTitle === FULLNET_API_TITLE
    && value.documentName === FULLNET_OPENAPI_DOCUMENT_NAME
    && value.openApiJsonPath === FULLNET_OPENAPI_JSON_PATH
    && value.scalarUiPath === FULLNET_SCALAR_UI_PATH
    && value.securitySchemeName === 'Bearer'
    && value.securitySchemeType === 'http'
    && value.securitySchemeScheme === 'bearer';
}

export function resolveFullNetApiUrl(baseUrl: string, path: string): string {
  const normalizedBase = baseUrl.endsWith('/') ? baseUrl.slice(0, -1) : baseUrl;
  return `${normalizedBase}${path}`;
}

function isRecord(value: unknown): value is Record<string, unknown> {
  return typeof value === 'object' && value !== null && !Array.isArray(value);
}
