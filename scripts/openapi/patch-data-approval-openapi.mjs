import { readFile, writeFile } from 'node:fs/promises';
import path from 'node:path';
import { fileURLToPath } from 'node:url';

const root = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '..', '..');
const openapiPath = path.join(root, 'contracts/openapi/fullnet-client-v1.openapi.json');
const manifestPath = path.join(root, 'contracts/openapi/client-generation-manifest-v1.json');

const doc = JSON.parse(await readFile(openapiPath, 'utf8'));
const ref = (name) => ({ $ref: `#/components/schemas/${name}` });
const security = [{ Bearer: [] }, { ApiKey: [] }];

function withSecurity(operation) {
  return { ...operation, security };
}

doc.paths['/api/v1/data-approvals/requests'] = {
  get: withSecurity({
    operationId: 'dataApprovalsListRequests',
    parameters: [
      { in: 'query', name: 'page', schema: { type: 'integer' } },
      { in: 'query', name: 'pageSize', schema: { type: 'integer' } },
      { in: 'query', name: 'scenarioKey', schema: { type: 'string' } },
      { in: 'query', name: 'statusKey', schema: { type: 'string' } }
    ],
    responses: {
      200: { description: 'OK', content: { 'application/json': { schema: ref('PagedResultOfDataApprovalRequestResponse') } } },
      401: { description: 'Unauthorized', content: { 'application/problem+json': { schema: ref('ProblemDetails') } } },
      403: { description: 'Forbidden', content: { 'application/problem+json': { schema: ref('ProblemDetails') } } }
    },
    tags: ['DataApprovalRequests']
  }),
  post: withSecurity({
    operationId: 'dataApprovalsCreateRequest',
    requestBody: { required: true, content: { 'application/json': { schema: ref('CreateDataApprovalRequestBody') } } },
    responses: {
      201: { description: 'Created', content: { 'application/json': { schema: ref('DataApprovalRequestResponse') } } },
      400: { description: 'Bad Request', content: { 'application/problem+json': { schema: ref('ProblemDetails') } } },
      401: { description: 'Unauthorized', content: { 'application/problem+json': { schema: ref('ProblemDetails') } } },
      403: { description: 'Forbidden', content: { 'application/problem+json': { schema: ref('ProblemDetails') } } },
      409: { description: 'Conflict', content: { 'application/problem+json': { schema: ref('ProblemDetails') } } }
    },
    tags: ['DataApprovalRequests']
  })
};

doc.paths['/api/v1/data-approvals/requests/{requestId}'] = {
  get: withSecurity({
    operationId: 'dataApprovalsGetRequest',
    parameters: [{ in: 'path', name: 'requestId', required: true, schema: { type: 'string', format: 'uuid' } }],
    responses: {
      200: { description: 'OK', content: { 'application/json': { schema: ref('DataApprovalRequestResponse') } } },
      401: { description: 'Unauthorized', content: { 'application/problem+json': { schema: ref('ProblemDetails') } } },
      403: { description: 'Forbidden', content: { 'application/problem+json': { schema: ref('ProblemDetails') } } },
      404: { description: 'Not Found', content: { 'application/problem+json': { schema: ref('ProblemDetails') } } }
    },
    tags: ['DataApprovalRequests']
  })
};

doc.paths['/api/v1/data-approvals/requests/{requestId}/cancel'] = {
  post: withSecurity({
    operationId: 'dataApprovalsCancelRequest',
    parameters: [{ in: 'path', name: 'requestId', required: true, schema: { type: 'string', format: 'uuid' } }],
    requestBody: { required: true, content: { 'application/json': { schema: ref('CancelDataApprovalRequestBody') } } },
    responses: {
      200: { description: 'OK', content: { 'application/json': { schema: ref('DataApprovalRequestResponse') } } },
      401: { description: 'Unauthorized', content: { 'application/problem+json': { schema: ref('ProblemDetails') } } },
      403: { description: 'Forbidden', content: { 'application/problem+json': { schema: ref('ProblemDetails') } } },
      404: { description: 'Not Found', content: { 'application/problem+json': { schema: ref('ProblemDetails') } } },
      409: { description: 'Conflict', content: { 'application/problem+json': { schema: ref('ProblemDetails') } } }
    },
    tags: ['DataApprovalRequests']
  })
};

doc.components.schemas.CreateDataApprovalRequestBody = {
  type: 'object',
  required: ['scenarioKey', 'targetEntityId', 'proposedChangeJson', 'workflowDefinitionKey', 'idempotencyKey'],
  properties: {
    scenarioKey: { type: 'string' },
    targetEntityId: { type: 'string', format: 'uuid' },
    proposedChangeJson: { type: 'string' },
    workflowDefinitionKey: { type: 'string' },
    idempotencyKey: { type: 'string' }
  }
};
doc.components.schemas.CancelDataApprovalRequestBody = {
  type: 'object',
  required: ['idempotencyKey'],
  properties: { idempotencyKey: { type: 'string' } }
};
doc.components.schemas.DataApprovalRequestResponse = {
  type: 'object',
  required: ['id', 'scenarioKey', 'targetEntityId', 'statusKey', 'afterSnapshotJson', 'workflowDefinitionVersionId', 'submittedByUserId', 'submittedAtUtc', 'version'],
  properties: {
    id: { type: 'string', format: 'uuid' },
    scenarioKey: { type: 'string' },
    targetEntityId: { type: 'string', format: 'uuid' },
    statusKey: { type: 'string' },
    beforeSnapshotJson: { type: ['string', 'null'] },
    afterSnapshotJson: { type: 'string' },
    workflowInstanceId: { type: ['string', 'null'], format: 'uuid' },
    workflowRevision: { type: ['integer', 'null'], format: 'int64' },
    workflowDefinitionVersionId: { type: 'string', format: 'uuid' },
    submittedByUserId: { type: 'string', format: 'uuid' },
    submittedAtUtc: { type: 'string', format: 'date-time' },
    resolvedAtUtc: { type: ['string', 'null'], format: 'date-time' },
    version: { type: 'integer', format: 'int64' }
  }
};
doc.components.schemas.PagedResultOfDataApprovalRequestResponse = {
  type: 'object',
  required: ['items', 'page', 'pageSize', 'total'],
  properties: {
    items: { type: 'array', items: ref('DataApprovalRequestResponse') },
    page: { type: 'integer', format: 'int32' },
    pageSize: { type: 'integer', format: 'int32' },
    total: { type: 'integer', format: 'int64' }
  }
};

await writeFile(openapiPath, `${JSON.stringify(doc, null, 2)}\n`, 'utf8');

const manifest = JSON.parse(await readFile(manifestPath, 'utf8'));
const entries = [
  'dataApprovalsListRequests',
  'dataApprovalsCreateRequest',
  'dataApprovalsGetRequest',
  'dataApprovalsCancelRequest'
];
for (const operationId of entries) {
  if (!manifest.entries.some((item) => item.operationId === operationId)) {
    manifest.entries.push({
      operationId,
      apiModule: 'ui/admin/src/api/data-approval-requests.ts',
      generatedGroup: 'data-approval-requests',
      status: 'generated'
    });
  }
}
await writeFile(manifestPath, `${JSON.stringify(manifest, null, 2)}\n`, 'utf8');
console.log('patched openapi and manifest');
