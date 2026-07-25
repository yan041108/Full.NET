import { describe, expect, it } from 'vitest';
import {
  FULLNET_API_TITLE,
  FULLNET_OPENAPI_DOCUMENT_NAME,
  FULLNET_OPENAPI_JSON_PATH,
  FULLNET_SCALAR_UI_PATH,
  isPlatformApiDocumentationCatalog,
  resolveFullNetApiUrl
} from '../src/platform-api-documentation';

describe('platform-api-documentation contracts', () => {
  const catalog = {
    apiTitle: FULLNET_API_TITLE,
    documentName: FULLNET_OPENAPI_DOCUMENT_NAME,
    openApiJsonPath: FULLNET_OPENAPI_JSON_PATH,
    scalarUiPath: FULLNET_SCALAR_UI_PATH,
    securitySchemeName: 'Bearer',
    securitySchemeType: 'http',
    securitySchemeScheme: 'bearer'
  };

  it('matches the repository OpenAPI catalog fixture', () => {
    expect(isPlatformApiDocumentationCatalog(catalog)).toBe(true);
  });

  it('resolves API documentation URLs against a base origin', () => {
    expect(resolveFullNetApiUrl('http://localhost:5149', FULLNET_SCALAR_UI_PATH))
      .toBe('http://localhost:5149/scalar/v1');
    expect(resolveFullNetApiUrl('http://localhost:5149/', FULLNET_OPENAPI_JSON_PATH))
      .toBe('http://localhost:5149/openapi/v1.json');
  });
});
