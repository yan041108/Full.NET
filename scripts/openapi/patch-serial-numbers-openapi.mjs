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

doc.paths['/api/v1/serial-numbers/rules/{ruleId}/update-approval-preview'] = {
  post: withSecurity({
    operationId: 'serialNumbersPreviewRuleUpdateApproval',
    parameters: [{ in: 'path', name: 'ruleId', required: true, schema: { type: 'string', format: 'uuid' } }],
    requestBody: { required: true, content: { 'application/json': { schema: ref('UpdateSerialNumberRuleRequest') } } },
    responses: {
      200: { description: 'OK', content: { 'application/json': { schema: ref('SerialRuleUpdateApprovalPreviewResponse') } } },
      400: { description: 'Bad Request', content: { 'application/problem+json': { schema: ref('ProblemDetails') } } },
      401: { description: 'Unauthorized', content: { 'application/problem+json': { schema: ref('ProblemDetails') } } },
      403: { description: 'Forbidden', content: { 'application/problem+json': { schema: ref('ProblemDetails') } } },
      404: { description: 'Not Found', content: { 'application/problem+json': { schema: ref('ProblemDetails') } } },
      409: { description: 'Conflict', content: { 'application/problem+json': { schema: ref('ProblemDetails') } } }
    },
    tags: ['SerialNumbersHostRules']
  })
};

doc.paths['/api/v1/serial-numbers/rules/{ruleId}/update-approval-requests'] = {
  post: withSecurity({
    operationId: 'serialNumbersSubmitRuleUpdateApproval',
    parameters: [{ in: 'path', name: 'ruleId', required: true, schema: { type: 'string', format: 'uuid' } }],
    requestBody: { required: true, content: { 'application/json': { schema: ref('SubmitSerialRuleUpdateApprovalRequest') } } },
    responses: {
      201: { description: 'Created', content: { 'application/json': { schema: ref('SerialRuleUpdateApprovalSubmissionResponse') } } },
      400: { description: 'Bad Request', content: { 'application/problem+json': { schema: ref('ProblemDetails') } } },
      401: { description: 'Unauthorized', content: { 'application/problem+json': { schema: ref('ProblemDetails') } } },
      403: { description: 'Forbidden', content: { 'application/problem+json': { schema: ref('ProblemDetails') } } },
      404: { description: 'Not Found', content: { 'application/problem+json': { schema: ref('ProblemDetails') } } },
      409: { description: 'Conflict', content: { 'application/problem+json': { schema: ref('ProblemDetails') } } }
    },
    tags: ['SerialNumbersHostRules']
  })
};

doc.components.schemas.SerialRuleFieldChange = {
  type: 'object',
  required: ['fieldKey', 'changed'],
  properties: {
    fieldKey: { type: 'string' },
    beforeValue: { type: ['string', 'null'] },
    afterValue: { type: ['string', 'null'] },
    changed: { type: 'boolean' }
  }
};
doc.components.schemas.SubmitSerialRuleUpdateApprovalRequest = {
  type: 'object',
  required: ['update', 'idempotencyKey'],
  properties: {
    update: ref('UpdateSerialNumberRuleRequest'),
    idempotencyKey: { type: 'string' }
  }
};
doc.components.schemas.SerialRuleUpdateApprovalPreviewResponse = {
  type: 'object',
  required: ['ruleId', 'ruleKey', 'displayName', 'changes', 'beforeSnapshotJson', 'afterSnapshotJson'],
  properties: {
    ruleId: { type: 'string', format: 'uuid' },
    ruleKey: { type: 'string' },
    displayName: { type: 'string' },
    changes: { type: 'array', items: ref('SerialRuleFieldChange') },
    beforeSnapshotJson: { type: 'string' },
    afterSnapshotJson: { type: 'string' }
  }
};
doc.components.schemas.SerialRuleUpdateApprovalSubmissionResponse = {
  type: 'object',
  required: ['requestId', 'statusKey', 'changes', 'afterSnapshotJson', 'workflowDefinitionVersionId', 'requestVersion'],
  properties: {
    requestId: { type: 'string', format: 'uuid' },
    statusKey: { type: 'string' },
    changes: { type: 'array', items: ref('SerialRuleFieldChange') },
    beforeSnapshotJson: { type: ['string', 'null'] },
    afterSnapshotJson: { type: 'string' },
    workflowDefinitionVersionId: { type: 'string', format: 'uuid' },
    requestVersion: { type: 'integer', format: 'int64' }
  }
};

await writeFile(openapiPath, `${JSON.stringify(doc, null, 2)}\n`, 'utf8');

const manifest = JSON.parse(await readFile(manifestPath, 'utf8'));
for (const operationId of [
  'serialNumbersPreviewRuleUpdateApproval',
  'serialNumbersSubmitRuleUpdateApproval'
]) {
  if (!manifest.entries.some((item) => item.operationId === operationId)) {
    manifest.entries.push({
      operationId,
      apiModule: 'ui/admin/src/api/serial-number-rules.ts',
      generatedGroup: 'serial-numbers-rules',
      status: 'generated'
    });
  }
}
await writeFile(manifestPath, `${JSON.stringify(manifest, null, 2)}\n`, 'utf8');
console.log('patched serial numbers openapi and manifest');
