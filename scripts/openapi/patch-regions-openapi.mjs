import { readFile, writeFile } from 'node:fs/promises';
import path from 'node:path';
import { fileURLToPath } from 'node:url';

const root = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '..', '..');
const openapiPath = path.join(root, 'contracts/openapi/fullnet-client-v1.openapi.json');

const doc = JSON.parse(await readFile(openapiPath, 'utf8'));
const ref = (name) => ({ $ref: `#/components/schemas/${name}` });
const security = [{ Bearer: [] }, { ApiKey: [] }];

function withSecurity(operation) {
  return { ...operation, security };
}

const tag = 'RegionsAdministrativeRegions';

doc.paths['/api/v1/regions/administrative-regions/children'] = {
  get: withSecurity({
    operationId: 'regionsListAdministrativeRegionChildren',
    parameters: [{ in: 'query', name: 'parentId', schema: { type: 'string', format: 'uuid' } }],
    responses: {
      200: {
        description: 'OK',
        content: {
          'application/json': {
            schema: { type: 'array', items: ref('AdministrativeRegionChildResponse') }
          }
        }
      },
      401: { description: 'Unauthorized', content: { 'application/problem+json': { schema: ref('ProblemDetails') } } },
      403: { description: 'Forbidden', content: { 'application/problem+json': { schema: ref('ProblemDetails') } } }
    },
    tags: [tag]
  })
};

doc.paths['/api/v1/regions/administrative-regions/tree'] = {
  get: withSecurity({
    operationId: 'regionsGetAdministrativeRegionTree',
    parameters: [
      { in: 'query', name: 'parentId', schema: { type: 'string', format: 'uuid' } },
      { in: 'query', name: 'maxDepth', schema: { type: 'integer' } }
    ],
    responses: {
      200: {
        description: 'OK',
        content: {
          'application/json': {
            schema: { type: 'array', items: ref('AdministrativeRegionTreeNodeResponse') }
          }
        }
      },
      401: { description: 'Unauthorized', content: { 'application/problem+json': { schema: ref('ProblemDetails') } } },
      403: { description: 'Forbidden', content: { 'application/problem+json': { schema: ref('ProblemDetails') } } }
    },
    tags: [tag]
  })
};

doc.paths['/api/v1/regions/administrative-regions'] = {
  get: withSecurity({
    operationId: 'regionsListAdministrativeRegions',
    parameters: [
      { in: 'query', name: 'page', schema: { type: 'integer' } },
      { in: 'query', name: 'pageSize', schema: { type: 'integer' } },
      { in: 'query', name: 'parentId', schema: { type: 'string', format: 'uuid' } },
      { in: 'query', name: 'name', schema: { type: 'string' } },
      { in: 'query', name: 'code', schema: { type: 'string' } },
      { in: 'query', name: 'level', schema: { type: 'integer' } }
    ],
    responses: {
      200: {
        description: 'OK',
        content: { 'application/json': { schema: ref('PagedResultOfAdministrativeRegionResponse') } }
      },
      401: { description: 'Unauthorized', content: { 'application/problem+json': { schema: ref('ProblemDetails') } } },
      403: { description: 'Forbidden', content: { 'application/problem+json': { schema: ref('ProblemDetails') } } }
    },
    tags: [tag]
  }),
  post: withSecurity({
    operationId: 'regionsCreateAdministrativeRegion',
    requestBody: {
      required: true,
      content: { 'application/json': { schema: ref('CreateAdministrativeRegionRequest') } }
    },
    responses: {
      201: {
        description: 'Created',
        content: { 'application/json': { schema: ref('AdministrativeRegionResponse') } }
      },
      400: { description: 'Bad Request', content: { 'application/problem+json': { schema: ref('ProblemDetails') } } },
      401: { description: 'Unauthorized', content: { 'application/problem+json': { schema: ref('ProblemDetails') } } },
      403: { description: 'Forbidden', content: { 'application/problem+json': { schema: ref('ProblemDetails') } } },
      409: { description: 'Conflict', content: { 'application/problem+json': { schema: ref('ProblemDetails') } } }
    },
    tags: [tag]
  })
};

doc.paths['/api/v1/regions/administrative-regions/dataset-manifest/latest'] = {
  get: withSecurity({
    operationId: 'regionsGetLatestAdministrativeRegionDatasetManifest',
    parameters: [{ in: 'query', name: 'datasetKey', schema: { type: 'string' } }],
    responses: {
      200: {
        description: 'OK',
        content: { 'application/json': { schema: ref('AdministrativeRegionDatasetManifestResponse') } }
      },
      401: { description: 'Unauthorized', content: { 'application/problem+json': { schema: ref('ProblemDetails') } } },
      403: { description: 'Forbidden', content: { 'application/problem+json': { schema: ref('ProblemDetails') } } },
      404: { description: 'Not Found', content: { 'application/problem+json': { schema: ref('ProblemDetails') } } }
    },
    tags: [tag]
  })
};

doc.paths['/api/v1/regions/administrative-regions/{regionId}'] = {
  get: withSecurity({
    operationId: 'regionsGetAdministrativeRegion',
    parameters: [{ in: 'path', name: 'regionId', required: true, schema: { type: 'string', format: 'uuid' } }],
    responses: {
      200: { description: 'OK', content: { 'application/json': { schema: ref('AdministrativeRegionResponse') } } },
      401: { description: 'Unauthorized', content: { 'application/problem+json': { schema: ref('ProblemDetails') } } },
      403: { description: 'Forbidden', content: { 'application/problem+json': { schema: ref('ProblemDetails') } } },
      404: { description: 'Not Found', content: { 'application/problem+json': { schema: ref('ProblemDetails') } } }
    },
    tags: [tag]
  }),
  put: withSecurity({
    operationId: 'regionsUpdateAdministrativeRegion',
    parameters: [{ in: 'path', name: 'regionId', required: true, schema: { type: 'string', format: 'uuid' } }],
    requestBody: {
      required: true,
      content: { 'application/json': { schema: ref('UpdateAdministrativeRegionRequest') } }
    },
    responses: {
      200: { description: 'OK', content: { 'application/json': { schema: ref('AdministrativeRegionResponse') } } },
      400: { description: 'Bad Request', content: { 'application/problem+json': { schema: ref('ProblemDetails') } } },
      401: { description: 'Unauthorized', content: { 'application/problem+json': { schema: ref('ProblemDetails') } } },
      403: { description: 'Forbidden', content: { 'application/problem+json': { schema: ref('ProblemDetails') } } },
      404: { description: 'Not Found', content: { 'application/problem+json': { schema: ref('ProblemDetails') } } },
      409: { description: 'Conflict', content: { 'application/problem+json': { schema: ref('ProblemDetails') } } }
    },
    tags: [tag]
  })
};

doc.paths['/api/v1/regions/administrative-regions/{regionId}/delete'] = {
  post: withSecurity({
    operationId: 'regionsDeleteAdministrativeRegion',
    parameters: [{ in: 'path', name: 'regionId', required: true, schema: { type: 'string', format: 'uuid' } }],
    requestBody: {
      required: true,
      content: { 'application/json': { schema: ref('DeleteAdministrativeRegionRequest') } }
    },
    responses: {
      204: { description: 'No Content' },
      400: { description: 'Bad Request', content: { 'application/problem+json': { schema: ref('ProblemDetails') } } },
      401: { description: 'Unauthorized', content: { 'application/problem+json': { schema: ref('ProblemDetails') } } },
      403: { description: 'Forbidden', content: { 'application/problem+json': { schema: ref('ProblemDetails') } } },
      404: { description: 'Not Found', content: { 'application/problem+json': { schema: ref('ProblemDetails') } } },
      409: { description: 'Conflict', content: { 'application/problem+json': { schema: ref('ProblemDetails') } } }
    },
    tags: [tag]
  })
};

doc.paths['/api/v1/regions/administrative-regions/import/preview'] = {
  post: withSecurity({
    operationId: 'regionsPreviewAdministrativeRegionImport',
    requestBody: {
      required: true,
      content: { 'application/json': { schema: ref('ImportAdministrativeRegionsRequest') } }
    },
    responses: {
      200: {
        description: 'OK',
        content: { 'application/json': { schema: ref('ImportAdministrativeRegionsPreviewResponse') } }
      },
      400: { description: 'Bad Request', content: { 'application/problem+json': { schema: ref('ProblemDetails') } } },
      401: { description: 'Unauthorized', content: { 'application/problem+json': { schema: ref('ProblemDetails') } } },
      403: { description: 'Forbidden', content: { 'application/problem+json': { schema: ref('ProblemDetails') } } }
    },
    tags: [tag]
  })
};

doc.paths['/api/v1/regions/administrative-regions/import/apply'] = {
  post: withSecurity({
    operationId: 'regionsApplyAdministrativeRegionImport',
    requestBody: {
      required: true,
      content: { 'application/json': { schema: ref('ImportAdministrativeRegionsRequest') } }
    },
    responses: {
      200: {
        description: 'OK',
        content: { 'application/json': { schema: ref('ImportAdministrativeRegionsApplyResponse') } }
      },
      400: { description: 'Bad Request', content: { 'application/problem+json': { schema: ref('ProblemDetails') } } },
      401: { description: 'Unauthorized', content: { 'application/problem+json': { schema: ref('ProblemDetails') } } },
      403: { description: 'Forbidden', content: { 'application/problem+json': { schema: ref('ProblemDetails') } } }
    },
    tags: [tag]
  })
};

const schemas = {
  AdministrativeRegionResponse: {
    type: 'object',
    properties: {
      id: { type: 'string', format: 'uuid' },
      parentId: { type: 'string', format: 'uuid', nullable: true },
      code: { type: 'string' },
      name: { type: 'string' },
      shortName: { type: 'string', nullable: true },
      mergerName: { type: 'string', nullable: true },
      zipCode: { type: 'string', nullable: true },
      cityCode: { type: 'string', nullable: true },
      level: { type: 'integer' },
      regionType: { type: 'string', nullable: true },
      pinYin: { type: 'string', nullable: true },
      longitude: { type: 'number', format: 'decimal', nullable: true },
      latitude: { type: 'number', format: 'decimal', nullable: true },
      displayOrder: { type: 'integer' },
      remark: { type: 'string', nullable: true },
      createdAtUtc: { type: 'string', format: 'date-time' },
      updatedAtUtc: { type: 'string', format: 'date-time', nullable: true },
      version: { type: 'integer' }
    }
  },
  AdministrativeRegionChildResponse: {
    type: 'object',
    properties: {
      id: { type: 'string', format: 'uuid' },
      parentId: { type: 'string', format: 'uuid', nullable: true },
      code: { type: 'string' },
      name: { type: 'string' },
      level: { type: 'integer' },
      displayOrder: { type: 'integer' },
      hasChildren: { type: 'boolean' }
    }
  },
  AdministrativeRegionTreeNodeResponse: {
    type: 'object',
    properties: {
      id: { type: 'string', format: 'uuid' },
      parentId: { type: 'string', format: 'uuid', nullable: true },
      code: { type: 'string' },
      name: { type: 'string' },
      level: { type: 'integer' },
      displayOrder: { type: 'integer' },
      children: { type: 'array', items: ref('AdministrativeRegionTreeNodeResponse') }
    }
  },
  AdministrativeRegionDatasetManifestResponse: {
    type: 'object',
    properties: {
      id: { type: 'string', format: 'uuid' },
      datasetKey: { type: 'string' },
      datasetVersion: { type: 'string' },
      sourceDigest: { type: 'string' },
      recordCount: { type: 'integer' },
      appliedAtUtc: { type: 'string', format: 'date-time' },
      appliedByUserId: { type: 'string', format: 'uuid' }
    }
  },
  CreateAdministrativeRegionRequest: {
    type: 'object',
    properties: {
      parentId: { type: 'string', format: 'uuid', nullable: true },
      code: { type: 'string' },
      name: { type: 'string' },
      shortName: { type: 'string', nullable: true },
      mergerName: { type: 'string', nullable: true },
      zipCode: { type: 'string', nullable: true },
      cityCode: { type: 'string', nullable: true },
      level: { type: 'integer' },
      regionType: { type: 'string', nullable: true },
      pinYin: { type: 'string', nullable: true },
      longitude: { type: 'number', format: 'decimal', nullable: true },
      latitude: { type: 'number', format: 'decimal', nullable: true },
      displayOrder: { type: 'integer' },
      remark: { type: 'string', nullable: true }
    }
  },
  UpdateAdministrativeRegionRequest: {
    type: 'object',
    properties: {
      parentId: { type: 'string', format: 'uuid', nullable: true },
      name: { type: 'string' },
      shortName: { type: 'string', nullable: true },
      mergerName: { type: 'string', nullable: true },
      zipCode: { type: 'string', nullable: true },
      cityCode: { type: 'string', nullable: true },
      level: { type: 'integer' },
      regionType: { type: 'string', nullable: true },
      pinYin: { type: 'string', nullable: true },
      longitude: { type: 'number', format: 'decimal', nullable: true },
      latitude: { type: 'number', format: 'decimal', nullable: true },
      displayOrder: { type: 'integer' },
      remark: { type: 'string', nullable: true },
      version: { type: 'integer' }
    }
  },
  DeleteAdministrativeRegionRequest: {
    type: 'object',
    properties: { version: { type: 'integer' } }
  },
  ImportAdministrativeRegionItem: {
    type: 'object',
    properties: {
      code: { type: 'string' },
      parentCode: { type: 'string', nullable: true },
      name: { type: 'string' },
      shortName: { type: 'string', nullable: true },
      mergerName: { type: 'string', nullable: true },
      zipCode: { type: 'string', nullable: true },
      cityCode: { type: 'string', nullable: true },
      level: { type: 'integer' },
      regionType: { type: 'string', nullable: true },
      pinYin: { type: 'string', nullable: true },
      longitude: { type: 'number', format: 'decimal', nullable: true },
      latitude: { type: 'number', format: 'decimal', nullable: true },
      displayOrder: { type: 'integer', nullable: true }
    }
  },
  ImportAdministrativeRegionsRequest: {
    type: 'object',
    properties: {
      datasetKey: { type: 'string' },
      datasetVersion: { type: 'string' },
      sourceDigest: { type: 'string' },
      mergeMode: { type: 'string' },
      items: { type: 'array', items: ref('ImportAdministrativeRegionItem') }
    }
  },
  ImportAdministrativeRegionsPreviewResponse: {
    type: 'object',
    properties: {
      added: { type: 'array', items: { type: 'object' } },
      updated: { type: 'array', items: { type: 'object' } },
      removed: { type: 'array', items: { type: 'object' } },
      skippedCount: { type: 'integer' }
    }
  },
  ImportAdministrativeRegionsApplyResponse: {
    type: 'object',
    properties: {
      addedCount: { type: 'integer' },
      updatedCount: { type: 'integer' },
      removedCount: { type: 'integer' },
      skippedCount: { type: 'integer' },
      manifest: ref('AdministrativeRegionDatasetManifestResponse')
    }
  },
  PagedResultOfAdministrativeRegionResponse: {
    type: 'object',
    properties: {
      items: { type: 'array', items: ref('AdministrativeRegionResponse') },
      page: { type: 'integer' },
      pageSize: { type: 'integer' },
      totalCount: { type: 'integer' }
    }
  }
};

Object.assign(doc.components.schemas, schemas);

await writeFile(openapiPath, `${JSON.stringify(doc, null, 2)}\n`, 'utf8');
console.log('Patched regions administrative regions OpenAPI paths and schemas.');
