import { readFile, writeFile } from 'node:fs/promises';
import path from 'node:path';
import { fileURLToPath } from 'node:url';

const root = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '..', '..');
const targetPath = path.join(root, 'contracts/openapi/fullnet-client-v1.openapi.json');
const document = JSON.parse(await readFile(targetPath, 'utf8'));

const problem = (description) => ({
  content: {
    'application/problem+json': {
      schema: { $ref: '#/components/schemas/ProblemDetails' }
    }
  },
  description
});

const security = [{ Bearer: [] }, { ApiKey: [] }];

const pathEntries = {
  '/api/v1/import-export/schemas': {
    get: {
      operationId: 'importExportListStaticSchemas',
      responses: {
        '200': {
          content: {
            'application/json': {
              schema: {
                items: { $ref: '#/components/schemas/StaticImportSchemaDefinition' },
                type: 'array'
              }
            }
          },
          description: 'OK'
        },
        '401': problem('Unauthorized'),
        '403': problem('Forbidden')
      },
      security,
      tags: ['ImportExportStaticSchemas']
    }
  },
  '/api/v1/import-export/schemas/{schemaKey}': {
    get: {
      operationId: 'importExportGetStaticSchema',
      parameters: [{ in: 'path', name: 'schemaKey', required: true, schema: { type: 'string' } }],
      responses: {
        '200': {
          content: {
            'application/json': {
              schema: { $ref: '#/components/schemas/StaticImportSchemaDefinition' }
            }
          },
          description: 'OK'
        },
        '401': problem('Unauthorized'),
        '403': problem('Forbidden'),
        '404': problem('Not Found')
      },
      security,
      tags: ['ImportExportStaticSchemas']
    }
  },
  '/api/v1/import-export/schemas/{schemaKey}/worksheets/{worksheetKey}/template': {
    get: {
      operationId: 'importExportDownloadStaticSchemaTemplate',
      parameters: [
        { in: 'path', name: 'schemaKey', required: true, schema: { type: 'string' } },
        { in: 'path', name: 'worksheetKey', required: true, schema: { type: 'string' } }
      ],
      responses: {
        '200': {
          content: {
            'application/octet-stream': {
              schema: { $ref: '#/components/schemas/Stream' }
            }
          },
          description: 'OK'
        },
        '401': problem('Unauthorized'),
        '403': problem('Forbidden'),
        '404': problem('Not Found')
      },
      security,
      tags: ['ImportExportStaticSchemas']
    }
  },
  '/api/v1/import-export/tasks': {
    get: {
      operationId: 'importExportListImportTasks',
      parameters: [
        { in: 'query', name: 'page', schema: { format: 'int32', type: ['integer', 'string'] } },
        { in: 'query', name: 'pageSize', schema: { format: 'int32', type: ['integer', 'string'] } },
        { in: 'query', name: 'schemaKey', schema: { type: ['null', 'string'] } }
      ],
      responses: {
        '200': {
          content: {
            'application/json': {
              schema: { $ref: '#/components/schemas/PagedResultOfImportExportTaskResponse' }
            }
          },
          description: 'OK'
        },
        '401': problem('Unauthorized'),
        '403': problem('Forbidden')
      },
      security,
      tags: ['ImportExportTasks']
    },
    post: {
      operationId: 'importExportCreateImportTask',
      requestBody: {
        content: {
          'multipart/form-data': {
            schema: {
              properties: {
                schemaKey: { type: 'string' },
                worksheetKey: { type: 'string' },
                file: { $ref: '#/components/schemas/IFormFile' }
              },
              required: ['schemaKey', 'worksheetKey', 'file'],
              type: 'object'
            }
          }
        },
        required: true
      },
      responses: {
        '201': {
          content: {
            'application/json': {
              schema: { $ref: '#/components/schemas/ImportExportTaskDetailResponse' }
            }
          },
          description: 'Created'
        },
        '400': problem('Bad Request'),
        '401': problem('Unauthorized'),
        '403': problem('Forbidden'),
        '404': problem('Not Found'),
        '422': problem('Unprocessable Entity')
      },
      security,
      tags: ['ImportExportTasks']
    }
  },
  '/api/v1/import-export/tasks/{taskId}': {
    get: {
      operationId: 'importExportGetImportTask',
      parameters: [{ in: 'path', name: 'taskId', required: true, schema: { format: 'uuid', type: 'string' } }],
      responses: {
        '200': {
          content: {
            'application/json': {
              schema: { $ref: '#/components/schemas/ImportExportTaskDetailResponse' }
            }
          },
          description: 'OK'
        },
        '401': problem('Unauthorized'),
        '403': problem('Forbidden'),
        '404': problem('Not Found')
      },
      security,
      tags: ['ImportExportTasks']
    }
  }
};

const schemaEntries = {
  StaticImportWorksheetDefinition: {
    properties: {
      worksheetKey: { type: 'string' },
      displayName: { type: 'string' },
      headerColumns: { items: { type: 'string' }, type: 'array' }
    },
    required: ['worksheetKey', 'displayName', 'headerColumns'],
    type: 'object'
  },
  StaticImportSchemaDefinition: {
    properties: {
      schemaKey: { type: 'string' },
      displayName: { type: 'string' },
      scopeKey: { type: 'string' },
      requiredPermission: { type: 'string' },
      worksheets: {
        items: { $ref: '#/components/schemas/StaticImportWorksheetDefinition' },
        type: 'array'
      }
    },
    required: ['schemaKey', 'displayName', 'scopeKey', 'requiredPermission', 'worksheets'],
    type: 'object'
  },
  StaticImportRowPreviewResult: {
    properties: {
      lineNumber: { format: 'int32', type: ['integer', 'string'] },
      isValid: { type: 'boolean' },
      errorCode: { type: ['null', 'string'] },
      message: { type: ['null', 'string'] }
    },
    required: ['lineNumber', 'isValid'],
    type: 'object'
  },
  ImportExportTaskResponse: {
    properties: {
      id: { format: 'uuid', type: 'string' },
      tenantId: { format: 'uuid', type: 'string' },
      schemaKey: { type: 'string' },
      schemaDisplayName: { type: 'string' },
      worksheetKey: { type: 'string' },
      sourceFileId: { format: 'uuid', type: 'string' },
      sourceFileName: { type: ['null', 'string'] },
      statusKey: { type: 'string' },
      totalRows: { format: 'int32', type: ['integer', 'string'] },
      validRowCount: { format: 'int32', type: ['integer', 'string'] },
      invalidRowCount: { format: 'int32', type: ['integer', 'string'] },
      errorCode: { type: ['null', 'string'] },
      requestedByUserId: { format: 'uuid', type: 'string' },
      createdAtUtc: { format: 'date-time', type: 'string' },
      previewCompletedAtUtc: { format: 'date-time', type: ['null', 'string'] },
      version: { format: 'int64', type: ['integer', 'string'] }
    },
    required: [
      'id', 'tenantId', 'schemaKey', 'schemaDisplayName', 'worksheetKey', 'sourceFileId',
      'statusKey', 'totalRows', 'validRowCount', 'invalidRowCount', 'requestedByUserId',
      'createdAtUtc', 'version'
    ],
    type: 'object'
  },
  ImportExportTaskDetailResponse: {
    properties: {
      id: { format: 'uuid', type: 'string' },
      tenantId: { format: 'uuid', type: 'string' },
      schemaKey: { type: 'string' },
      schemaDisplayName: { type: 'string' },
      worksheetKey: { type: 'string' },
      sourceFileId: { format: 'uuid', type: 'string' },
      sourceFileName: { type: ['null', 'string'] },
      statusKey: { type: 'string' },
      totalRows: { format: 'int32', type: ['integer', 'string'] },
      validRowCount: { format: 'int32', type: ['integer', 'string'] },
      invalidRowCount: { format: 'int32', type: ['integer', 'string'] },
      errorCode: { type: ['null', 'string'] },
      requestedByUserId: { format: 'uuid', type: 'string' },
      createdAtUtc: { format: 'date-time', type: 'string' },
      previewCompletedAtUtc: { format: 'date-time', type: ['null', 'string'] },
      version: { format: 'int64', type: ['integer', 'string'] },
      previewRows: {
        items: { $ref: '#/components/schemas/StaticImportRowPreviewResult' },
        type: 'array'
      }
    },
    required: [
      'id', 'tenantId', 'schemaKey', 'schemaDisplayName', 'worksheetKey', 'sourceFileId',
      'statusKey', 'totalRows', 'validRowCount', 'invalidRowCount', 'requestedByUserId',
      'createdAtUtc', 'version', 'previewRows'
    ],
    type: 'object'
  },
  PagedResultOfImportExportTaskResponse: {
    properties: {
      items: {
        items: { $ref: '#/components/schemas/ImportExportTaskResponse' },
        type: 'array'
      },
      page: { format: 'int32', type: ['integer', 'string'] },
      pageSize: { format: 'int32', type: ['integer', 'string'] },
      total: { format: 'int64', type: ['integer', 'string'] }
    },
    required: ['items', 'page', 'pageSize', 'total'],
    type: 'object'
  }
};

document.paths = { ...document.paths, ...pathEntries };
document.components.schemas = { ...document.components.schemas, ...schemaEntries };
await writeFile(targetPath, `${JSON.stringify(document, null, 2)}\n`, 'utf8');
console.log('Merged ImportExport OpenAPI paths and schemas.');
