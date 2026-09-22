/**
 * 在无 Docker / 无法跑 OpenApiDocumentation 集成测试时，将 Document 模块新增端点合并进客户端快照。
 * 正式环境仍应优先使用：pnpm openapi:client:snapshot -- --update
 */
import { readFile, writeFile } from 'node:fs/promises';
import path from 'node:path';
import { fileURLToPath } from 'node:url';

const repositoryRoot = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '..', '..');
const snapshotPath = path.join(
  repositoryRoot,
  'contracts',
  'openapi',
  'fullnet-client-v1.openapi.json'
);

const problem = status => ({
  content: {
    'application/problem+json': {
      schema: { $ref: '#/components/schemas/ProblemDetails' }
    }
  },
  description: status === 400 ? 'Bad Request'
    : status === 401 ? 'Unauthorized'
      : status === 403 ? 'Forbidden'
        : status === 404 ? 'Not Found'
          : status === 409 ? 'Conflict'
            : status === 422 ? 'Unprocessable Entity'
              : status === 429 ? 'Too Many Requests'
                : 'Error'
});

function publicSharePost(
  operationId,
  subPath,
  successStatus,
  successMedia
) {
  const responses = {
    [String(successStatus)]: successMedia,
    400: problem(400),
    401: problem(401),
    403: problem(403),
    404: problem(404),
    409: problem(409),
    429: problem(429)
  };
  if (operationId === 'documentPublicCreateDocumentSharePreviewTask') {
    responses['422'] = problem(422);
  }
  if (operationId === 'documentPublicContentDocumentSharePreviewTask') {
    responses['422'] = problem(422);
  }

  return {
    post: {
      operationId,
      parameters: [
        {
          in: 'path',
          name: 'shareCode',
          required: true,
          schema: { type: 'string' }
        },
        ...(subPath.includes('{taskId}')
          ? [{
            in: 'path',
            name: 'taskId',
            required: true,
            schema: { format: 'uuid', type: 'string' }
          }]
          : [])
      ],
      requestBody: {
        content: {
          'application/json': {
            schema: { $ref: '#/components/schemas/AccessHostDocumentShareRequest' }
          }
        },
        required: true
      },
      responses,
      security: [],
      tags: ['DocumentPublicShares']
    }
  };
}

const int32 = {
  format: 'int32',
  pattern: '^-?(?:0|[1-9]\\d*)$',
  type: ['integer', 'string']
};

const boolQuery = {
  schema: { type: 'boolean' }
};

async function main() {
  const document = JSON.parse(await readFile(snapshotPath, 'utf8'));
  const paths = document.paths ?? {};
  const schemas = document.components?.schemas ?? {};

  if (!paths['/api/v1/document/host/shares/batch']) {
    paths['/api/v1/document/host/shares/batch'] = {
      post: {
        operationId: 'documentHostBatchCreateDocumentShares',
        requestBody: {
          content: {
            'application/json': {
              schema: { $ref: '#/components/schemas/BatchCreateHostDocumentSharesRequest' }
            }
          },
          required: true
        },
        responses: {
          200: {
            content: {
              'application/json': {
                schema: { $ref: '#/components/schemas/BatchCreateHostDocumentSharesResponse' }
              }
            },
            description: 'OK'
          },
          400: problem(400),
          401: problem(401),
          403: problem(403)
        },
        security: [{ Bearer: [] }, { ApiKey: [] }],
        tags: ['DocumentHostShares']
      }
    };
  }

  if (!paths['/api/v1/document/host/version-retention']) {
    paths['/api/v1/document/host/version-retention'] = {
      get: {
        operationId: 'documentHostGetVersionRetentionSettings',
        responses: {
          200: {
            content: {
              'application/json': {
                schema: {
                  $ref: '#/components/schemas/HostDocumentVersionRetentionSettingsResponse'
                }
              }
            },
            description: 'OK'
          },
          401: problem(401),
          403: problem(403)
        },
        security: [{ Bearer: [] }, { ApiKey: [] }],
        tags: ['DocumentHostSettings']
      }
    };
  }

  const accessLogs = paths['/api/v1/document/host/access-logs']?.get;
  if (accessLogs && !accessLogs.parameters?.some(p => p.name === 'accessTypeKey')) {
    accessLogs.parameters = [
      ...(accessLogs.parameters ?? []),
      { in: 'query', name: 'accessTypeKey', schema: { type: 'string' } },
      { in: 'query', name: 'sourceKey', schema: { type: 'string' } }
    ];
  }

  const listTags = paths['/api/v1/document/host/tags']?.get;
  if (listTags && !listTags.parameters?.some(p => p.name === 'isHot')) {
    listTags.parameters = [
      { in: 'query', name: 'isHot', ...boolQuery },
      { in: 'query', name: 'isRecommended', ...boolQuery }
    ];
  }

  if (!paths['/api/v1/document/public/shares/{shareCode}/preview-task']) {
    paths['/api/v1/document/public/shares/{shareCode}/preview-task'] = publicSharePost(
      'documentPublicCreateDocumentSharePreviewTask',
      'preview-task',
      201,
      {
        content: {
          'application/json': {
            schema: { $ref: '#/components/schemas/HostDocumentPreviewTaskResponse' }
          }
        },
        description: 'Created'
      }
    );
  }

  if (!paths['/api/v1/document/public/shares/{shareCode}/preview-tasks/{taskId}']) {
    paths['/api/v1/document/public/shares/{shareCode}/preview-tasks/{taskId}'] = publicSharePost(
      'documentPublicGetDocumentSharePreviewTask',
      'preview-tasks/{taskId}',
      200,
      {
        content: {
          'application/json': {
            schema: { $ref: '#/components/schemas/HostDocumentPreviewTaskResponse' }
          }
        },
        description: 'OK'
      }
    );
  }

  if (!paths['/api/v1/document/public/shares/{shareCode}/preview-tasks/{taskId}/content']) {
    paths['/api/v1/document/public/shares/{shareCode}/preview-tasks/{taskId}/content'] =
      publicSharePost(
        'documentPublicContentDocumentSharePreviewTask',
        'preview-tasks/{taskId}/content',
        200,
        {
          content: {
            'application/octet-stream': {
              schema: { $ref: '#/components/schemas/Stream' }
            }
          },
          description: 'OK'
        }
      );
  }

  const boolProp = { type: 'boolean' };

  schemas.BatchCreateHostDocumentSharesRequest = {
    additionalProperties: false,
    properties: {
      documentIds: {
        items: { format: 'uuid', type: 'string' },
        type: 'array'
      },
      maxAccessCount: { ...int32, type: ['null', 'integer', 'string'] },
      password: { type: ['null', 'string'] },
      validDays: int32
    },
    required: ['documentIds', 'validDays'],
    type: 'object'
  };

  schemas.BatchCreateHostDocumentShareItem = {
    properties: {
      documentId: { format: 'uuid', type: 'string' },
      errorCode: { type: ['null', 'string'] },
      message: { type: ['null', 'string'] },
      share: {
        oneOf: [{ type: 'null' }, { $ref: '#/components/schemas/HostDocumentShareResponse' }]
      },
      succeeded: { type: 'boolean' }
    },
    required: ['documentId', 'succeeded', 'share', 'errorCode', 'message'],
    type: 'object'
  };

  schemas.BatchCreateHostDocumentSharesResponse = {
    properties: {
      results: {
        items: { $ref: '#/components/schemas/BatchCreateHostDocumentShareItem' },
        type: 'array'
      },
      succeededCount: int32
    },
    required: ['succeededCount', 'results'],
    type: 'object'
  };

  const retentionPath = paths['/api/v1/document/host/version-retention'] ?? {};
  if (!retentionPath.put) {
    retentionPath.put = {
      operationId: 'documentHostUpdateVersionRetentionSettings',
      requestBody: {
        content: {
          'application/json': {
            schema: { $ref: '#/components/schemas/UpdateHostDocumentVersionRetentionRequest' }
          }
        },
        required: true
      },
      responses: {
        200: {
          content: {
            'application/json': {
              schema: { $ref: '#/components/schemas/HostDocumentVersionRetentionSettingsResponse' }
            }
          },
          description: 'OK'
        },
        400: problem(400),
        401: problem(401),
        403: problem(403)
      },
      security: [{ Bearer: [] }, { ApiKey: [] }],
      tags: ['DocumentHostSettings']
    };
    paths['/api/v1/document/host/version-retention'] = retentionPath;
  }

  schemas.UpdateHostDocumentVersionRetentionRequest = {
    properties: {
      batchSize: int32,
      maximumRetainedHistoryVersions: int32,
      minimumRetainedVersionsPerItem: int32,
      pollSeconds: int32
    },
    required: [
      'minimumRetainedVersionsPerItem',
      'maximumRetainedHistoryVersions',
      'pollSeconds',
      'batchSize'
    ],
    type: 'object'
  };

  schemas.HostDocumentVersionRetentionSettingsResponse = {
    properties: {
      batchSize: int32,
      maximumRetainedHistoryVersions: int32,
      minimumRetainedVersionsPerItem: int32,
      pollSeconds: int32
    },
    required: [
      'minimumRetainedVersionsPerItem',
      'maximumRetainedHistoryVersions',
      'pollSeconds',
      'batchSize'
    ],
    type: 'object'
  };

  for (const name of ['CreateHostDocumentTagRequest', 'UpdateHostDocumentTagRequest']) {
    const schema = schemas[name];
    if (schema?.properties && !schema.properties.isHot) {
      schema.properties.isHot = boolProp;
      schema.properties.isRecommended = boolProp;
    }
  }

  const itemsList = paths['/api/v1/document/host/items']?.get;
  if (itemsList) {
    const existingItemParams = new Set((itemsList.parameters ?? []).map(p => p.name));
    if (!existingItemParams.has('tagId')) {
      itemsList.parameters = [
        ...(itemsList.parameters ?? []),
        { in: 'query', name: 'tagId', schema: { format: 'uuid', type: 'string' } }
      ];
    }
  }

  const sharesList = paths['/api/v1/document/host/shares']?.get;
  if (sharesList) {
    const existing = new Set((sharesList.parameters ?? []).map(p => p.name));
    const extraParams = [
      { in: 'query', name: 'isEnabled', ...boolQuery },
      { in: 'query', name: 'shareCode', schema: { type: 'string' } },
      { in: 'query', name: 'documentId', schema: { type: 'string' } },
      { in: 'query', name: 'expiredOnly', ...boolQuery },
      { in: 'query', name: 'activeOnly', ...boolQuery },
      { in: 'query', name: 'minAccessCount', schema: int32 },
      { in: 'query', name: 'maxAccessCount', schema: int32 },
      { in: 'query', name: 'sortBy', schema: { type: 'string' } },
      { in: 'query', name: 'sortDir', schema: { type: 'string' } }
    ].filter(p => !existing.has(p.name));
    if (extraParams.length > 0) {
      sharesList.parameters = [...(sharesList.parameters ?? []), ...extraParams];
    }
  }

  const tagResponse = schemas.HostDocumentTagResponse;
  if (tagResponse?.properties && !tagResponse.properties.isHot) {
    tagResponse.properties.isHot = boolProp;
    tagResponse.properties.isRecommended = boolProp;
    tagResponse.required = [
      ...(tagResponse.required ?? []),
      'isHot',
      'isRecommended'
    ];
  }

  document.paths = paths;
  document.components.schemas = schemas;

  await writeFile(snapshotPath, `${JSON.stringify(document, null, 2)}\n`, 'utf8');
  process.stdout.write(`已合并 Document OpenAPI 增量：${snapshotPath}\n`);
}

await main();
