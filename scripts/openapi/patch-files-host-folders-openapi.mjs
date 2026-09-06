import { readFile, writeFile } from 'node:fs/promises';
import path from 'node:path';
import { fileURLToPath } from 'node:url';

const root = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '..', '..');
const openapiPath = path.join(root, 'contracts/openapi/fullnet-client-v1.openapi.json');
const manifestPath = path.join(root, 'contracts/openapi/client-generation-manifest-v1.json');

const doc = JSON.parse(await readFile(openapiPath, 'utf8'));
const manifest = JSON.parse(await readFile(manifestPath, 'utf8'));
const ref = (name) => ({ $ref: `#/components/schemas/${name}` });
const security = [{ Bearer: [] }, { ApiKey: [] }];
const intQuery = { in: 'query', schema: { type: 'integer' } };
const uuidPath = { in: 'path', required: true, schema: { type: 'string', format: 'uuid' } };
const problem = (status) => ({
  [status]: {
    description: status === 401 ? 'Unauthorized' : status === 403 ? 'Forbidden' : status === 404 ? 'Not Found' : 'Conflict',
    content: { 'application/problem+json': { schema: ref('ProblemDetails') } }
  }
});

function withSecurity(operation) {
  return { ...operation, security };
}

doc.paths['/api/v1/files/host-files'].get.parameters.push(
  { in: 'query', name: 'folderId', schema: { type: 'string' } },
  { in: 'query', name: 'fileNameContains', schema: { type: 'string' } }
);

doc.paths['/api/v1/files/host-files'].post.requestBody.content['multipart/form-data'].schema.properties.folderId = {
  type: 'string',
  format: 'uuid'
};

doc.paths['/api/v1/files/host-files/{fileId}/update'] = {
  post: withSecurity({
    operationId: 'filesUpdateHostFileMetadata',
    parameters: [{ ...uuidPath, name: 'fileId' }],
    requestBody: {
      required: true,
      content: { 'application/json': { schema: ref('UpdateHostFileMetadataRequest') } }
    },
    responses: {
      200: { description: 'OK', content: { 'application/json': { schema: ref('HostFileResponse') } } },
      ...problem(401),
      ...problem(403)
    },
    tags: ['FilesHostFiles']
  })
};

doc.paths['/api/v1/files/host-files/{fileId}/references'] = {
  get: withSecurity({
    operationId: 'filesListHostFileReferences',
    parameters: [
      { ...uuidPath, name: 'fileId' },
      { ...intQuery, name: 'page' },
      { ...intQuery, name: 'pageSize' }
    ],
    responses: {
      200: {
        description: 'OK',
        content: { 'application/json': { schema: ref('PagedResultOfHostFileReferenceClaimResponse') } }
      },
      ...problem(401),
      ...problem(403)
    },
    tags: ['FilesHostFiles']
  })
};

doc.paths['/api/v1/files/host-folders/tree'] = {
  get: withSecurity({
    operationId: 'filesGetHostFolderTree',
    responses: {
      200: {
        description: 'OK',
        content: { 'application/json': { schema: { type: 'array', items: ref('HostFolderTreeNode') } } }
      },
      ...problem(401),
      ...problem(403)
    },
    tags: ['FilesHostFolders']
  })
};

doc.paths['/api/v1/files/host-folders'] = {
  post: withSecurity({
    operationId: 'filesCreateHostFolder',
    requestBody: {
      required: true,
      content: { 'application/json': { schema: ref('CreateHostFolderRequest') } }
    },
    responses: {
      200: { description: 'OK', content: { 'application/json': { schema: ref('HostFolderResponse') } } },
      ...problem(401),
      ...problem(403)
    },
    tags: ['FilesHostFolders']
  })
};

doc.paths['/api/v1/files/host-folders/{folderId}/update'] = {
  post: withSecurity({
    operationId: 'filesUpdateHostFolder',
    parameters: [{ ...uuidPath, name: 'folderId' }],
    requestBody: {
      required: true,
      content: { 'application/json': { schema: ref('UpdateHostFolderRequest') } }
    },
    responses: {
      200: { description: 'OK', content: { 'application/json': { schema: ref('HostFolderResponse') } } },
      ...problem(401),
      ...problem(403)
    },
    tags: ['FilesHostFolders']
  })
};

doc.paths['/api/v1/files/host-folders/{folderId}/delete'] = {
  post: withSecurity({
    operationId: 'filesDeleteHostFolder',
    parameters: [{ ...uuidPath, name: 'folderId' }],
    requestBody: {
      required: true,
      content: { 'application/json': { schema: ref('DeleteHostFolderRequest') } }
    },
    responses: {
      200: { description: 'OK', content: { 'application/json': { schema: ref('HostFolderResponse') } } },
      ...problem(401),
      ...problem(403)
    },
    tags: ['FilesHostFolders']
  })
};

Object.assign(doc.components.schemas.HostFileResponse.properties, {
  folderId: { type: ['null', 'string'], format: 'uuid' },
  revision: { type: ['integer', 'string'], format: 'int64' },
  updatedAtUtc: { type: ['null', 'string'], format: 'date-time' },
  updatedByUserId: { type: ['null', 'string'], format: 'uuid' }
});
doc.components.schemas.HostFileResponse.required.push(
  'folderId',
  'revision',
  'updatedAtUtc',
  'updatedByUserId'
);

doc.components.schemas.UpdateHostFileMetadataRequest = {
  type: 'object',
  required: ['expectedRevision', 'originalFileName', 'folderId'],
  properties: {
    expectedRevision: { type: ['integer', 'string'], format: 'int64' },
    originalFileName: { type: 'string' },
    folderId: { type: ['null', 'string'], format: 'uuid' }
  }
};

doc.components.schemas.HostFileReferenceClaimResponse = {
  type: 'object',
  required: [
    'id',
    'idempotencyKey',
    'consumerModule',
    'consumerReferenceId',
    'state',
    'createdAtUtc',
    'updatedAtUtc',
    'confirmedAtUtc',
    'releasedAtUtc'
  ],
  properties: {
    id: { type: 'string', format: 'uuid' },
    idempotencyKey: { type: 'string' },
    consumerModule: { type: 'string' },
    consumerReferenceId: { type: 'string', format: 'uuid' },
    state: { type: 'string' },
    createdAtUtc: { type: 'string', format: 'date-time' },
    updatedAtUtc: { type: 'string', format: 'date-time' },
    confirmedAtUtc: { type: ['null', 'string'], format: 'date-time' },
    releasedAtUtc: { type: ['null', 'string'], format: 'date-time' }
  }
};

doc.components.schemas.PagedResultOfHostFileReferenceClaimResponse = {
  type: 'object',
  required: ['items', 'page', 'pageSize', 'total'],
  properties: {
    items: { type: 'array', items: ref('HostFileReferenceClaimResponse') },
    page: { type: 'integer' },
    pageSize: { type: 'integer' },
    total: { type: 'integer' }
  }
};

doc.components.schemas.HostFolderTreeNode = {
  type: 'object',
  required: ['id', 'parentId', 'name', 'displayOrder', 'revision', 'children'],
  properties: {
    id: { type: 'string', format: 'uuid' },
    parentId: { type: ['null', 'string'], format: 'uuid' },
    name: { type: 'string' },
    displayOrder: { type: 'integer' },
    revision: { type: ['integer', 'string'], format: 'int64' },
    children: { type: 'array', items: ref('HostFolderTreeNode') }
  }
};

doc.components.schemas.HostFolderResponse = {
  type: 'object',
  required: [
    'id',
    'parentId',
    'name',
    'displayOrder',
    'revision',
    'createdAtUtc',
    'createdByUserId',
    'updatedAtUtc',
    'updatedByUserId'
  ],
  properties: {
    id: { type: 'string', format: 'uuid' },
    parentId: { type: ['null', 'string'], format: 'uuid' },
    name: { type: 'string' },
    displayOrder: { type: 'integer' },
    revision: { type: ['integer', 'string'], format: 'int64' },
    createdAtUtc: { type: 'string', format: 'date-time' },
    createdByUserId: { type: 'string', format: 'uuid' },
    updatedAtUtc: { type: ['null', 'string'], format: 'date-time' },
    updatedByUserId: { type: ['null', 'string'], format: 'uuid' }
  }
};

doc.components.schemas.CreateHostFolderRequest = {
  type: 'object',
  required: ['name'],
  properties: {
    parentId: { type: ['null', 'string'], format: 'uuid' },
    name: { type: 'string' },
    displayOrder: { type: 'integer' }
  }
};

doc.components.schemas.UpdateHostFolderRequest = {
  type: 'object',
  required: ['expectedRevision', 'name', 'displayOrder'],
  properties: {
    expectedRevision: { type: ['integer', 'string'], format: 'int64' },
    name: { type: 'string' },
    displayOrder: { type: 'integer' }
  }
};

doc.components.schemas.DeleteHostFolderRequest = {
  type: 'object',
  required: ['expectedRevision'],
  properties: {
    expectedRevision: { type: ['integer', 'string'], format: 'int64' }
  }
};

const newManifestEntries = [
  ['filesUpdateHostFileMetadata', 'files-host-files'],
  ['filesListHostFileReferences', 'files-host-files'],
  ['filesGetHostFolderTree', 'files-host-folders'],
  ['filesCreateHostFolder', 'files-host-folders'],
  ['filesUpdateHostFolder', 'files-host-folders'],
  ['filesDeleteHostFolder', 'files-host-folders']
];

for (const [operationId, generatedGroup] of newManifestEntries) {
  if (!manifest.entries.some((entry) => entry.operationId === operationId)) {
    manifest.entries.push({
      operationId,
      apiModule: 'ui/admin/src/api/host-files.ts',
      generatedGroup,
      status: 'generated'
    });
  }
}

await writeFile(openapiPath, `${JSON.stringify(doc, null, 2)}\n`, 'utf8');
await writeFile(manifestPath, `${JSON.stringify(manifest, null, 2)}\n`, 'utf8');
