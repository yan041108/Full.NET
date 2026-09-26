import assert from 'node:assert/strict';
import { access, mkdtemp, readFile, readdir, rm, writeFile } from 'node:fs/promises';
import os from 'node:os';
import path from 'node:path';
import test from 'node:test';
import { fileURLToPath } from 'node:url';
import { createRequire, stripTypeScriptTypes } from 'node:module';

const repositoryRoot = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '../..');
const snapshotPath = path.join(
  repositoryRoot,
  'contracts',
  'openapi',
  'fullnet-client-v1.openapi.json'
);
const expectedFileNames = [
  'guards.generated.ts',
  'index.generated.ts',
  'models.generated.ts',
  'operations.generated.ts'
];

test('生成守卫读取服务端整数字符串且拒绝精度丢失，嵌套引用和数组保持一致', async () => {
  const { renderGeneratedFiles } = await import('../../scripts/openapi/generate-fullnet-client.mjs');
  const files = renderGeneratedFiles({ openapi: '3.1.0', paths: {}, components: { schemas: {
    Row: { type: 'object', required: ['version', 'amount', 'code'], properties: {
      version: { type: ['integer', 'string'], format: 'int64', pattern: '^-?(?:0|[1-9]\\d*)$' },
      amount: { type: ['number', 'string'] }, code: { type: 'string' }
    } },
    Page: { type: 'object', required: ['items'], properties: {
      items: { type: 'array', items: { $ref: '#/components/schemas/Row' } }
    } },
    Wrapped: { allOf: [{ $ref: '#/components/schemas/Page' }] },
    NativeInteger: { type: 'integer' }
  } } });
  const code = stripTypeScriptTypes(files['guards.generated.ts']) + '\n//# sourceURL=fullnet-test-generated-guards.mjs';
  const readers = await import(`data:text/javascript;base64,${Buffer.from(code).toString('base64')}`);
  const row = { version: '42', amount: '12.50', code: '00042' };
  assert.deepEqual(readers.readWrapped({ items: [row] }), { items: [{ ...row, version: 42 }] });
  assert.equal(row.version, '42', '读取器不能修改调用者的原始响应对象');
  for (const version of ['9007199254740993', 9007199254740992, '1.5', 'abc', '1e3', ' 1']) {
    assert.throws(() => readers.readRow({ ...row, version }), /invalid_row/);
  }
  assert.equal(readers.readRow({ ...row, version: Number.MAX_SAFE_INTEGER }).version, Number.MAX_SAFE_INTEGER);
  assert.throws(() => readers.readNativeInteger('42'), /invalid_native_integer/);
});

test('联合 Schema 按匹配分支归一，不改写另一分支的普通字符串', async () => {
  const { renderGeneratedFiles } = await import('../../scripts/openapi/generate-fullnet-client.mjs');
  const integer = { type: ['integer', 'string'], pattern: '^-?(?:0|[1-9]\\d*)$' };
  const branch = (kind, value) => ({ type: 'object', required: ['kind', 'value'], properties: {
    kind: { type: 'string', enum: [kind] }, value
  } });
  const files = renderGeneratedFiles({ openapi: '3.1.0', paths: {}, components: { schemas: {
    Count: branch('count', integer),
    Union: { anyOf: [branch('label', { type: 'string' }), { $ref: '#/components/schemas/Count' }] },
    Nullable: { oneOf: [{ type: 'null' }, { $ref: '#/components/schemas/Count' }] }
  } } });
  const code = stripTypeScriptTypes(files['guards.generated.ts']) + '\n//# sourceURL=fullnet-test-union-guards.mjs';
  const readers = await import(`data:text/javascript;base64,${Buffer.from(code).toString('base64')}`);
  assert.deepEqual(readers.readUnion({ kind: 'label', value: '42' }), { kind: 'label', value: '42' });
  assert.deepEqual(readers.readUnion({ kind: 'count', value: '42' }), { kind: 'count', value: 42 });
  assert.equal(readers.readNullable(null), null);
  assert.deepEqual(readers.readNullable({ kind: 'count', value: '42' }), { kind: 'count', value: 42 });
});

test('内联 allOf 的多个整数编码对象可以通过 TypeScript 编译并正确读取', async () => {
  const { renderGeneratedFiles } = await import('../../scripts/openapi/generate-fullnet-client.mjs');
  const integer = { type: ['integer', 'string'], pattern: '^-?(?:0|[1-9]\\d*)$' };
  const field = name => ({ type: 'object', required: [name], properties: { [name]: integer } });
  const files = renderGeneratedFiles({ openapi: '3.1.0', paths: {}, components: { schemas: {
    Combined: { allOf: [field('first'), field('second')] }
  } } });
  const ts = createRequire(path.join(repositoryRoot, 'ui/admin/package.json'))('typescript');
  const options = { noEmit: true, strict: true, types: [], target: ts.ScriptTarget.ES2022,
    module: ts.ModuleKind.ESNext, moduleResolution: ts.ModuleResolutionKind.Bundler };
  const host = ts.createCompilerHost(options);
  const sourceRoot = '/fullnet-generator-regression';
  const sources = new Map(Object.entries(files).map(([name, content]) => [`${sourceRoot}/${name}`, content]));
  const originalGetSourceFile = host.getSourceFile.bind(host);
  const originalFileExists = host.fileExists.bind(host);
  const originalReadFile = host.readFile.bind(host);
  host.fileExists = name => sources.has(name) || originalFileExists(name);
  host.readFile = name => sources.get(name) ?? originalReadFile(name);
  host.resolveModuleNames = names => names.map(name => name === './models.generated.js'
    ? { resolvedFileName: `${sourceRoot}/models.generated.ts`, extension: ts.Extension.Ts }
    : undefined);
  host.getSourceFile = (name, languageVersion, onError, shouldCreateNewSourceFile) => sources.has(name)
    ? ts.createSourceFile(name, sources.get(name), languageVersion)
    : originalGetSourceFile(name, languageVersion, onError, shouldCreateNewSourceFile);
  const program = ts.createProgram([`${sourceRoot}/guards.generated.ts`], options, host);
  assert.deepEqual(ts.getPreEmitDiagnostics(program).map(diagnostic =>
    ts.flattenDiagnosticMessageText(diagnostic.messageText, '\n')), []);
  const code = stripTypeScriptTypes(files['guards.generated.ts']) + '\n//# sourceURL=fullnet-test-allof-guards.mjs';
  const readers = await import(`data:text/javascript;base64,${Buffer.from(code).toString('base64')}`);
  assert.deepEqual(readers.readCombined({ first: '1', second: '2' }), { first: 1, second: 2 });
});

test('生成器只产生 Full.NET models、guards、operations 与公开入口', async () => {
  const { generateFullNetClient } = await import(
    '../../scripts/openapi/generate-fullnet-client.mjs'
  );
  const temporaryRoot = await mkdtemp(path.join(os.tmpdir(), 'fullnet-generator-'));
  try {
    const firstOutput = path.join(temporaryRoot, 'first');
    const secondOutput = path.join(temporaryRoot, 'second');
    await generateFullNetClient({ inputPath: snapshotPath, outputDirectory: firstOutput });
    await generateFullNetClient({ inputPath: snapshotPath, outputDirectory: secondOutput });

    assert.deepEqual((await readdir(firstOutput)).sort(), expectedFileNames);
    assert.deepEqual(await readGeneratedFiles(firstOutput), await readGeneratedFiles(secondOutput));

    const files = await readGeneratedFiles(firstOutput);
    const combined = Object.values(files).join('\n');
    assert.doesNotMatch(
      combined,
      /\b(?:Configuration|BaseAPI|fetch|axios|localStorage)\b/u
    );
    assert.equal(Object.keys(files).every(fileName => fileName.endsWith('.generated.ts')), true);
    assert.match(files['models.generated.ts'], /export interface HostUserResponse/u);
    assert.match(files['guards.generated.ts'], /export function readHostUserResponse/u);
    assert.match(files['guards.generated.ts'], /client\.invalid_host_user_response/u);
    assert.match(files['operations.generated.ts'], /http\.request<unknown>/u);
    assert.match(files['operations.generated.ts'], /return readHostUserResponse\(value\)/u);
    assert.match(files['operations.generated.ts'], /http\.requestBlob\(/u);
    assert.match(files['operations.generated.ts'], /http\.request<void>\(/u);
    assert.match(
      files['operations.generated.ts'],
      /body\.append\('file', parameters\.file\)/u
    );
    assert.doesNotMatch(
      files['operations.generated.ts'],
      /body\.append\('file', String\(parameters\.file\)\)/u
    );
    assert.doesNotMatch(files['operations.generated.ts'], /requestBlob[\s\S]*readStream/u);
    assert.match(files['operations.generated.ts'], /readonly files: IFormFileCollection/u);
    assert.match(files['models.generated.ts'], /type IFormFileCollection = Array<IFormFile>/u);
    assert.match(files['models.generated.ts'], /type IFormFile = Blob/u);
    assert.match(files['operations.generated.ts'], /for \(const file of parameters.files\) \{\s+body.append\('files', file\)/u);
    assert.match(
      files['operations.generated.ts'],
      /export async function reportingDownloadExportTask\(/u
    );
    assert.doesNotMatch(
      files['operations.generated.ts'],
      /export async function aiStreamChatMessage\(/u
    );
    assert.match(
      files['index.generated.ts'],
      /^export \* from '\.\/(?:models|guards|operations)\.generated\.js';$/mu
    );
    assert.doesNotMatch(
      files['index.generated.ts'],
      /(?:Configuration|BaseAPI|runtime|http\.js)/u
    );
  } finally {
    await rm(temporaryRoot, { recursive: true, force: true });
  }
});

test('生成器把 JsonElement 空 Schema 收紧为 JSON 值守卫', async () => {
  const { renderGeneratedFiles } = await import(
    '../../scripts/openapi/generate-fullnet-client.mjs'
  );
  const files = renderGeneratedFiles({
    openapi: '3.1.0',
    paths: {},
    components: {
      schemas: {
        JsonElement: {}
      }
    }
  });

  assert.match(files['models.generated.ts'], /export type JsonElement = unknown;/u);
  assert.match(files['guards.generated.ts'], /isJsonValue\(value\)/u);
  assert.match(
    files['guards.generated.ts'],
    /function isJsonValue\(value: unknown\): boolean/u
  );
});

test('生成器把 allOf 收成交叉类型与合取守卫', async () => {
  const { renderGeneratedFiles } = await import(
    '../../scripts/openapi/generate-fullnet-client.mjs'
  );
  const files = renderGeneratedFiles({
    openapi: '3.1.0',
    paths: {},
    components: {
      schemas: {
        QuotaListItem: {
          type: 'object',
          required: ['id'],
          properties: { id: { type: 'string' } }
        },
        QuotaResponse: {
          allOf: [{ $ref: '#/components/schemas/QuotaListItem' }]
        }
      }
    }
  });

  assert.match(files['models.generated.ts'], /export type QuotaResponse = QuotaListItem;/u);
  assert.match(files['guards.generated.ts'], /isQuotaListItem\(value\)/u);
});

test('零漂移检查接受 Git autocrlf 产生的 CRLF 工作树文件', async () => {
  const { generateFullNetClient } = await import(
    '../../scripts/openapi/generate-fullnet-client.mjs'
  );
  const temporaryRoot = await mkdtemp(path.join(os.tmpdir(), 'fullnet-generator-crlf-'));
  try {
    await generateFullNetClient({ inputPath: snapshotPath, outputDirectory: temporaryRoot });
    await Promise.all(expectedFileNames.map(async fileName => {
      const filePath = path.join(temporaryRoot, fileName);
      const content = await readFile(filePath, 'utf8');
      await writeFile(filePath, content.replaceAll('\n', '\r\n'), 'utf8');
    }));

    await generateFullNetClient({
      inputPath: snapshotPath,
      outputDirectory: temporaryRoot,
      check: true
    });
  } finally {
    await rm(temporaryRoot, { recursive: true, force: true });
  }
});

test('候选停止后只保留零外部依赖的仓库内生成实现', async () => {
  const packageJson = JSON.parse(await readFile(path.join(repositoryRoot, 'package.json'), 'utf8'));
  const evaluation = JSON.parse(await readFile(path.join(
    repositoryRoot,
    'eng',
    'openapi-generator',
    'openapi-generator-config.json'
  ), 'utf8'));

  assert.equal(
    packageJson.scripts['openapi:client:generate'],
    'node scripts/openapi/generate-fullnet-client.mjs'
  );
  assert.equal(
    packageJson.devDependencies?.['@openapitools/openapi-generator-cli'],
    undefined
  );
  assert.equal(evaluation.implementation, 'repository-node');
  assert.equal(evaluation.candidate.status, 'rejected');
  assert.deepEqual(evaluation.selected.dependencies, []);
  await assert.rejects(
    access(path.join(repositoryRoot, 'openapitools.json')),
    error => error?.code === 'ENOENT'
  );
});

test('生成器把 Excel 工作簿下载收成 Blob，并跳过 SSE 流式 Operation', async () => {
  const { renderGeneratedFiles } = await import(
    '../../scripts/openapi/generate-fullnet-client.mjs'
  );
  const files = renderGeneratedFiles({
    openapi: '3.1.0',
    paths: {
      '/api/v1/reporting/export-tasks/{taskId}/download': {
        get: {
          operationId: 'reportingDownloadExportTask',
          parameters: [
            {
              in: 'path',
              name: 'taskId',
              required: true,
              schema: { type: 'string', format: 'uuid' }
            }
          ],
          responses: {
            200: {
              content: {
                'application/vnd.openxmlformats-officedocument.spreadsheetml.sheet': {
                  schema: { type: 'string', format: 'binary' }
                }
              }
            }
          }
        }
      },
      '/api/v1/ai/chat/sessions/{sessionId}/messages/stream': {
        post: {
          operationId: 'aiStreamChatMessage',
          parameters: [
            {
              in: 'path',
              name: 'sessionId',
              required: true,
              schema: { type: 'string', format: 'uuid' }
            }
          ],
          requestBody: {
            required: true,
            content: {
              'application/json': {
                schema: { type: 'object' }
              }
            }
          },
          responses: {
            200: {
              content: {
                'text/event-stream': {
                  schema: { type: 'string' }
                }
              }
            }
          }
        }
      },
      '/api/v1/files/host-files/batch-upload': {
        post: {
          operationId: 'filesBatchUploadHostFiles',
          requestBody: {
            required: true,
            content: {
              'multipart/form-data': {
                schema: {
                  type: 'object',
                  required: ['files'],
                  properties: {
                    files: {
                      type: 'array',
                      items: { type: 'string', format: 'binary' }
                    },
                    folderId: { type: 'string', format: 'uuid' }
                  }
                }
              }
            }
          },
          responses: {
            200: {
              content: {
                'application/json': {
                  schema: { type: 'object' }
                }
              }
            }
          }
        }
      }
    },
    components: { schemas: {} }
  });

  assert.match(files['operations.generated.ts'], /export async function reportingDownloadExportTask\(/u);
  assert.match(files['operations.generated.ts'], /http\.requestBlob\(/u);
  assert.doesNotMatch(
    files['operations.generated.ts'],
    /export async function aiStreamChatMessage\(/u
  );
  assert.match(
    files['operations.generated.ts'],
    /for \(const file of parameters\.files\) \{\s*body\.append\('files', file\);/u
  );
  assert.doesNotMatch(
    files['operations.generated.ts'],
    /body\.append\('files', String\(parameters\.files\)\)/u
  );
});

test('三个 Vue 试点 API 只保留生成 Operation 薄适配层', async () => {
  const adapters = await Promise.all([
    'users.ts',
    'host-files.ts',
    'config-entries.ts'
  ].map(fileName => readFile(path.join(
    repositoryRoot,
    'ui',
    'admin',
    'src',
    'api',
    fileName
  ), 'utf8')));
  const combined = adapters.join('\n');

  assert.doesNotMatch(combined, /\/api\/v1\//u);
  assert.doesNotMatch(combined, /\brequest(?:Blob)?\s*(?:<|\()/u);
  assert.match(combined, /identityListHostUsers\(http,/u);
  assert.match(combined, /identityRevealHostUserProfileFields\(http,/u);
  assert.match(combined, /identityUnlockHostUserLogin\(http,/u);
  assert.match(combined, /filesUploadHostFile\(http,/u);
  assert.match(combined, /filesDownloadHostFileContent\(http,/u);
  assert.match(combined, /filesBatchUploadHostFiles\(http,/u);
  assert.match(combined, /filesBatchDeleteHostFiles\(http,/u);
  assert.match(combined, /filesPreviewHostFileContent\(http,/u);
  assert.match(combined, /settingsDeleteHostConfigEntry\(/u);
  assert.match(combined, /settingsBatchDeleteHostConfigEntries\(/u);
});

test('CRUD Golden OpenAPI 通过就绪门禁并进入同一客户端生成入口', async () => {
  const fixturePath = path.join(
    repositoryRoot,
    'tests',
    'Full.NET.UnitTests',
    'CodeGeneration',
    'Fixtures',
    'CatalogProduct',
    'contracts',
    'openapi',
    'products.generated.openapi.json'
  );
  const document = JSON.parse(await readFile(fixturePath, 'utf8'));
  const { validateClientGenerationReadiness } = await import(
    '../../scripts/openapi/validate-client-generation-readiness.mjs'
  );
  const { generateFullNetClient } = await import(
    '../../scripts/openapi/generate-fullnet-client.mjs'
  );
  assert.deepEqual(validateClientGenerationReadiness(document), []);

  const temporaryRoot = await mkdtemp(path.join(os.tmpdir(), 'fullnet-crud-openapi-'));
  try {
    await generateFullNetClient({
      inputPath: fixturePath,
      outputDirectory: temporaryRoot
    });
    const files = await readGeneratedFiles(temporaryRoot);
    assert.match(files['models.generated.ts'], /export interface ProductResponse/u);
    assert.match(files['guards.generated.ts'], /export function readProductResponse/u);
    assert.match(files['operations.generated.ts'], /export async function catalogListProducts/u);
    assert.match(files['operations.generated.ts'], /export async function catalogCreateProduct/u);
    assert.match(files['operations.generated.ts'], /export async function catalogUpdateProduct/u);
    assert.match(files['operations.generated.ts'], /export async function catalogDisableProduct/u);
    assert.doesNotMatch(files['operations.generated.ts'], /request<ProductResponse>/u);
  } finally {
    await rm(temporaryRoot, { recursive: true, force: true });
  }
});

async function readGeneratedFiles(directory) {
  const entries = await Promise.all(expectedFileNames.map(async fileName => [
    fileName,
    await readFile(path.join(directory, fileName), 'utf8')
  ]));
  return Object.fromEntries(entries);
}
