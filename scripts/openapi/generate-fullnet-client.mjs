import {
  mkdir,
  readFile,
  readdir,
  writeFile
} from 'node:fs/promises';
import path from 'node:path';
import process from 'node:process';
import { fileURLToPath } from 'node:url';

import { validateClientGenerationReadiness } from './validate-client-generation-readiness.mjs';

const scriptDirectory = path.dirname(fileURLToPath(import.meta.url));
const repositoryRoot = path.resolve(scriptDirectory, '..', '..');
const defaultInputPath = path.join(
  repositoryRoot,
  'contracts',
  'openapi',
  'fullnet-client-v1.openapi.json'
);
const defaultOutputDirectory = path.join(
  repositoryRoot,
  'packages',
  'client-contracts',
  'src',
  'generated'
);
const generatedFileNames = [
  'guards.generated.ts',
  'index.generated.ts',
  'models.generated.ts',
  'operations.generated.ts'
];
const httpMethods = new Set([
  'delete',
  'get',
  'head',
  'options',
  'patch',
  'post',
  'put',
  'trace'
]);

export async function generateFullNetClient({
  inputPath = defaultInputPath,
  outputDirectory = defaultOutputDirectory,
  check = false
} = {}) {
  const document = JSON.parse(await readFile(inputPath, 'utf8'));
  const violations = validateClientGenerationReadiness(document);
  if (violations.length > 0) {
    throw new Error(`客户端 OpenAPI 未通过生成就绪门禁：\n${violations.join('\n')}`);
  }

  const files = renderGeneratedFiles(document);
  if (check) {
    await assertGeneratedFilesMatch(outputDirectory, files);
    return files;
  }

  await mkdir(outputDirectory, { recursive: true });
  await Promise.all(Object.entries(files).map(([fileName, content]) =>
    writeFile(path.join(outputDirectory, fileName), content, 'utf8')));
  return files;
}

export function renderGeneratedFiles(document) {
  const schemas = document.components?.schemas ?? {};
  const operations = collectOperations(document);
  return {
    'guards.generated.ts': renderGuards(schemas, operations),
    'index.generated.ts': renderIndex(),
    'models.generated.ts': renderModels(schemas),
    'operations.generated.ts': renderOperations(operations, schemas)
  };
}

function renderModels(schemas) {
  const blocks = Object.entries(schemas)
    .sort(([left], [right]) => compareText(left, right))
    .map(([name, schema]) => renderModel(name, schema));
  return generatedHeader('OpenAPI 数据模型') + blocks.join('\n\n') + '\n';
}

function renderModel(name, schema) {
  if (isObjectSchema(schema) && schema.properties) {
    const required = new Set(schema.required ?? []);
    const properties = Object.entries(schema.properties)
      .sort(([left], [right]) => compareText(left, right))
      .map(([propertyName, propertySchema]) =>
        `  readonly ${typescriptProperty(propertyName)}${required.has(propertyName) ? '' : '?'}: ${schemaType(propertySchema)};`)
      .join('\n');
    return `export interface ${name} {\n${properties}\n}`;
  }
  return `export type ${name} = ${schemaType(schema)};`;
}

function renderGuards(schemas, operations) {
  const schemaNames = Object.keys(schemas).sort(compareText);
  const imports = schemaNames.length > 0
    ? `import type {\n${schemaNames.map(name => `  ${name}`).join(',\n')}\n} from './models.generated.js';\n\n`
    : '';
  const schemaGuards = schemaNames.flatMap(name => [
    renderReader(
      `read${name}`,
      name,
      schemas[name],
      toErrorKey(name),
      `is${name}`
    ),
    renderPredicate(`is${name}`, name, schemas[name])
  ]);
  const inlineReaders = operations
    .filter(operation => operation.response.kind === 'json'
      && !isReference(operation.response.schema))
    .map(operation => renderReader(
      responseReaderName(operation),
      schemaType(operation.response.schema),
      operation.response.schema,
      toErrorKey(`${operation.operationId}Response`)
    ));
  return generatedHeader('OpenAPI 运行时响应守卫')
    + imports
    + [...schemaGuards, ...inlineReaders].join('\n\n')
    + '\n\n'
    + "const guidPattern = /^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/i;\n\n"
    + "function isRecord(value: unknown): value is Record<string, unknown> {\n"
    + "  return typeof value === 'object' && value !== null && !Array.isArray(value);\n"
    + '}\n';
}

function renderReader(functionName, returnType, schema, errorKey, predicateName = null) {
  const condition = predicateName
    ? `${predicateName}(value)`
    : guardExpression(schema, 'value');
  return `export function ${functionName}(value: unknown): ${returnType} {\n`
    + `  if (!(${condition})) {\n`
    + `    throw new Error('${errorKey}');\n`
    + '  }\n'
    + (predicateName
      ? '  return value;\n'
      : `  return value as ${returnType};\n`)
    + '}';
}

function renderPredicate(functionName, returnType, schema) {
  return `function ${functionName}(value: unknown): value is ${returnType} {\n`
    + `  return ${guardExpression(schema, 'value')};\n`
    + '}';
}

function renderOperations(operations, schemas) {
  const schemaNames = Object.keys(schemas).sort(compareText);
  const modelImports = schemaNames.length > 0
    ? `import type {\n${schemaNames.map(name => `  ${name}`).join(',\n')}\n} from './models.generated.js';\n`
    : '';
  const readerNames = [...new Set(operations
    .filter(operation => operation.response.kind === 'json')
    .map(responseReaderName))]
    .sort(compareText);
  const guardImports = readerNames.length > 0
    ? `import {\n${readerNames.map(name => `  ${name}`).join(',\n')}\n} from './guards.generated.js';\n`
    : '';
  const blocks = operations.map(operation => [
    renderOperationParameters(operation),
    renderOperation(operation)
  ].join('\n\n'));
  return generatedHeader('OpenAPI 低层 HttpClient Operation')
    + "import type { HttpClient } from '../http.js';\n"
    + modelImports
    + guardImports
    + '\n'
    + 'export type GeneratedJsonOperation<T> = (\n'
    + '  http: HttpClient,\n'
    + '  parameters: Readonly<Record<string, unknown>>,\n'
    + '  signal?: AbortSignal\n'
    + ') => Promise<T>;\n\n'
    + blocks.join('\n\n')
    + '\n';
}

function renderOperationParameters(operation) {
  const fields = [];
  for (const parameter of operation.parameters) {
    fields.push(
      `  readonly ${typescriptProperty(parameter.name)}${parameter.required ? '' : '?'}: ${schemaType(parameter.schema)};`
    );
  }
  if (operation.request.kind === 'json') {
    fields.push(
      `  readonly body${operation.request.required ? '' : '?'}: ${schemaType(operation.request.schema)};`
    );
  } else if (operation.request.kind === 'multipart') {
    const required = new Set(operation.request.schema.required ?? []);
    for (const [name, schema] of Object.entries(operation.request.schema.properties ?? {})
      .sort(([left], [right]) => compareText(left, right))) {
      fields.push(
        `  readonly ${typescriptProperty(name)}${required.has(name) ? '' : '?'}: ${schemaType(schema)};`
      );
    }
  }
  return `export interface ${operationParametersName(operation)} {\n${fields.join('\n')}\n}`;
}

function renderOperation(operation) {
  const returnType = operation.response.kind === 'blob'
    ? 'Blob'
    : operation.response.kind === 'void'
      ? 'void'
      : schemaType(operation.response.schema);
  const lines = [
    `export async function ${operation.operationId}(`,
    '  http: HttpClient,',
    `  parameters: ${operationParametersName(operation)},`,
    '  signal?: AbortSignal',
    `): Promise<${returnType}> {`
  ];
  lines.push(...renderPath(operation));
  lines.push(...renderRequestInitialization(operation));
  if (operation.response.kind === 'blob') {
    lines.push('  return await http.requestBlob(path, init, signal);');
  } else if (operation.response.kind === 'void') {
    lines.push('  await http.request<void>(path, init, signal);');
  } else {
    lines.push('  const value = await http.request<unknown>(path, init, signal);');
    lines.push(`  return ${responseReaderName(operation)}(value);`);
  }
  lines.push('}');
  return lines.join('\n');
}

function renderPath(operation) {
  const template = operation.path.replace(/\{([^}]+)\}/gu, (_, name) =>
    `\${encodeURIComponent(String(parameters.${typescriptProperty(name)}))}`);
  const queryParameters = operation.parameters.filter(parameter => parameter.in === 'query');
  if (queryParameters.length === 0) {
    return [`  const path = \`${template}\`;`];
  }
  const lines = ['  const query = new URLSearchParams();'];
  for (const parameter of queryParameters) {
    const access = `parameters.${typescriptProperty(parameter.name)}`;
    if (parameter.required) {
      lines.push(`  query.set('${parameter.name}', String(${access}));`);
    } else {
      lines.push(`  if (${access} !== undefined) {`);
      lines.push(`    query.set('${parameter.name}', String(${access}));`);
      lines.push('  }');
    }
  }
  lines.push(`  const path = query.size === 0 ? \`${template}\` : \`${template}?\${query.toString()}\`;`);
  return lines;
}

function renderRequestInitialization(operation) {
  const method = operation.method.toUpperCase();
  if (operation.request.kind === 'multipart') {
    const lines = ['  const body = new FormData();'];
    const required = new Set(operation.request.schema.required ?? []);
    for (const [name, schema] of Object.entries(operation.request.schema.properties ?? {})
      .sort(([left], [right]) => compareText(left, right))) {
      const access = `parameters.${typescriptProperty(name)}`;
      const appendValue = schemaType(schema) === 'Blob' ? access : `String(${access})`;
      if (!required.has(name)) {
        lines.push(`  if (${access} !== undefined) {`);
        lines.push(`    body.append('${name}', ${appendValue});`);
        lines.push('  }');
      } else {
        lines.push(`  body.append('${name}', ${appendValue});`);
      }
    }
    lines.push(`  const init: RequestInit = { method: '${method}', body };`);
    return lines;
  }
  if (operation.request.kind === 'json') {
    return [
      '  const init: RequestInit = {',
      `    method: '${method}',`,
      "    headers: { 'content-type': 'application/json' },",
      '    body: JSON.stringify(parameters.body)',
      '  };'
    ];
  }
  if (operation.response.kind === 'blob') {
    return [
      '  const init: RequestInit = {',
      `    method: '${method}',`,
      "    headers: { accept: 'application/octet-stream' }",
      '  };'
    ];
  }
  return [`  const init: RequestInit = { method: '${method}' };`];
}

function renderIndex() {
  return generatedHeader('OpenAPI 客户端唯一公开入口')
    + "export * from './models.generated.js';\n"
    + "export * from './guards.generated.js';\n"
    + "export * from './operations.generated.js';\n";
}

function collectOperations(document) {
  const operations = [];
  for (const [endpointPath, pathItem] of Object.entries(document.paths ?? {})) {
    for (const [method, operation] of Object.entries(pathItem)) {
      if (!httpMethods.has(method)) {
        continue;
      }
      operations.push({
        operationId: operation.operationId,
        method,
        path: endpointPath,
        parameters: (operation.parameters ?? []).map(parameter => ({
          in: parameter.in,
          name: parameter.name,
          required: parameter.required === true,
          schema: parameter.schema
        })),
        request: describeRequest(operation.requestBody),
        response: describeResponse(operation.responses)
      });
    }
  }
  return operations.sort((left, right) => compareText(left.operationId, right.operationId));
}

function describeRequest(requestBody) {
  if (!requestBody) {
    return { kind: 'none' };
  }
  const content = requestBody.content ?? {};
  if (content['application/json']) {
    return {
      kind: 'json',
      required: requestBody.required === true,
      schema: content['application/json'].schema
    };
  }
  if (content['multipart/form-data']) {
    return {
      kind: 'multipart',
      required: requestBody.required === true,
      schema: content['multipart/form-data'].schema
    };
  }
  throw new Error('客户端生成器遇到不支持的 requestBody media type。');
}

function describeResponse(responses) {
  const [status, response] = Object.entries(responses)
    .filter(([candidate]) => /^2\d\d$/u.test(candidate))
    .sort(([left], [right]) => left.localeCompare(right, 'en'))[0] ?? [];
  if (!response) {
    throw new Error('客户端生成器未找到 2xx 响应。');
  }
  if (status === '204') {
    return { kind: 'void' };
  }
  const content = response.content ?? {};
  if (content['application/octet-stream']) {
    return { kind: 'blob' };
  }
  const json = Object.entries(content).find(([mediaType]) =>
    mediaType === 'application/json' || mediaType.endsWith('+json'))?.[1];
  if (!json?.schema) {
    throw new Error(`客户端生成器不支持成功响应 ${status} 的 media type。`);
  }
  return { kind: 'json', schema: json.schema };
}

function responseReaderName(operation) {
  return isReference(operation.response.schema)
    ? `read${referenceName(operation.response.schema)}`
    : `read${toPascalCase(operation.operationId)}Response`;
}

function operationParametersName(operation) {
  return `${toPascalCase(operation.operationId)}Parameters`;
}

function schemaType(schema) {
  if (isReference(schema)) {
    return referenceName(schema);
  }
  if (Array.isArray(schema.oneOf)) {
    return schema.oneOf.map(item => schemaType(item)).join(' | ');
  }
  if (Array.isArray(schema.anyOf)) {
    return schema.anyOf.map(item => schemaType(item)).join(' | ');
  }
  const types = effectiveTypes(schema);
  if (types.length > 1) {
    return [...new Set(types.map(type => schemaType({ ...schema, type })))]
      .join(' | ');
  }
  const type = types[0];
  if (type === 'null') {
    return 'null';
  }
  if (type === 'string') {
    if (schema.format === 'binary') {
      return 'Blob';
    }
    if (Array.isArray(schema.enum) && schema.enum.length > 0) {
      return schema.enum.map(value => JSON.stringify(value)).join(' | ');
    }
    return 'string';
  }
  if (type === 'integer' || type === 'number') {
    return 'number';
  }
  if (type === 'boolean') {
    return 'boolean';
  }
  if (type === 'array') {
    return `ReadonlyArray<${schemaType(schema.items)}> ` .trim();
  }
  if (type === 'object' || schema.properties || schema.additionalProperties) {
    return 'Readonly<Record<string, unknown>>';
  }
  throw new Error(`客户端生成器不支持 Schema：${JSON.stringify(schema)}`);
}

function guardExpression(schema, valueExpression) {
  if (isReference(schema)) {
    return `is${referenceName(schema)}(${valueExpression})`;
  }
  const combination = schema.oneOf ?? schema.anyOf;
  if (Array.isArray(combination)) {
    return combination
      .map(item => `(${guardExpression(item, valueExpression)})`)
      .join(' || ');
  }
  const types = effectiveTypes(schema);
  if (types.length > 1) {
    return types
      .map(type => `(${guardExpression({ ...schema, type }, valueExpression)})`)
      .join(' || ');
  }
  const type = types[0];
  if (type === 'null') {
    return `${valueExpression} === null`;
  }
  if (type === 'string') {
    if (schema.format === 'binary') {
      return `${valueExpression} instanceof Blob`;
    }
    if (schema.format === 'uuid') {
      return `typeof ${valueExpression} === 'string' && guidPattern.test(${valueExpression})`;
    }
    if (Array.isArray(schema.enum) && schema.enum.length > 0) {
      return `typeof ${valueExpression} === 'string' && [${schema.enum.map(value => JSON.stringify(value)).join(', ')}].includes(${valueExpression})`;
    }
    return `typeof ${valueExpression} === 'string'`;
  }
  if (type === 'integer') {
    return `typeof ${valueExpression} === 'number' && Number.isInteger(${valueExpression})`;
  }
  if (type === 'number') {
    return `typeof ${valueExpression} === 'number' && Number.isFinite(${valueExpression})`;
  }
  if (type === 'boolean') {
    return `typeof ${valueExpression} === 'boolean'`;
  }
  if (type === 'array') {
    const itemName = `item${Math.abs(valueExpression.length)}`;
    return `Array.isArray(${valueExpression}) && ${valueExpression}.every(${itemName} => ${guardExpression(schema.items, itemName)})`;
  }
  if (type === 'object' || schema.properties || schema.additionalProperties) {
    const required = new Set(schema.required ?? []);
    const properties = Object.entries(schema.properties ?? {})
      .sort(([left], [right]) => compareText(left, right))
      .map(([name, propertySchema]) => {
        const access = `${valueExpression}[${JSON.stringify(name)}]`;
        const expression = guardExpression(propertySchema, access);
        return required.has(name)
          ? `(${expression})`
          : `(${access} === undefined || (${expression}))`;
      });
    return [`isRecord(${valueExpression})`, ...properties].join(' && ');
  }
  throw new Error(`客户端生成器无法生成 Schema guard：${JSON.stringify(schema)}`);
}

function effectiveTypes(schema) {
  const source = Array.isArray(schema.type)
    ? [...schema.type]
    : schema.type
      ? [schema.type]
      : schema.properties || schema.additionalProperties
        ? ['object']
        : [];
  if (source.includes('integer')
    && source.includes('string')
    && typeof schema.pattern === 'string'
    && schema.pattern.includes('\\d')) {
    return source.filter(type => type !== 'string');
  }
  return source;
}

function isObjectSchema(schema) {
  return effectiveTypes(schema).includes('object');
}

function isReference(schema) {
  return typeof schema?.$ref === 'string';
}

function referenceName(schema) {
  return decodeURIComponent(schema.$ref.split('/').at(-1));
}

function typescriptProperty(name) {
  return /^[A-Za-z_$][A-Za-z0-9_$]*$/u.test(name) ? name : JSON.stringify(name);
}

function toPascalCase(value) {
  return value.length === 0 ? value : value[0].toUpperCase() + value.slice(1);
}

function toErrorKey(value) {
  return `client.invalid_${value
    .replace(/([a-z0-9])([A-Z])/gu, '$1_$2')
    .replace(/[^A-Za-z0-9]+/gu, '_')
    .toLowerCase()}`;
}

function generatedHeader(subject) {
  return `// 此文件由 OpenAPI 快照确定性生成，禁止手工修改。\n// 内容：${subject}。\n\n`;
}

async function assertGeneratedFilesMatch(outputDirectory, expectedFiles) {
  let actualNames;
  try {
    actualNames = (await readdir(outputDirectory))
      .filter(fileName => fileName.endsWith('.generated.ts'))
      .sort(compareText);
  } catch (error) {
    if (error?.code === 'ENOENT') {
      throw new Error('客户端生成目录不存在；请先执行不带 --check 的生成命令。');
    }
    throw error;
  }
  if (JSON.stringify(actualNames) !== JSON.stringify(generatedFileNames)) {
    throw new Error('客户端生成文件清单漂移。');
  }
  for (const [fileName, expected] of Object.entries(expectedFiles)) {
    const actual = await readFile(path.join(outputDirectory, fileName), 'utf8');
    if (actual !== expected) {
      throw new Error(`客户端生成文件漂移：${fileName}。`);
    }
  }
}

function parseArguments(args) {
  const options = {
    inputPath: defaultInputPath,
    outputDirectory: defaultOutputDirectory,
    check: false
  };
  for (let index = 0; index < args.length; index += 1) {
    const argument = args[index];
    if (argument === '--check') {
      options.check = true;
    } else if (argument === '--input') {
      options.inputPath = path.resolve(args[index + 1] ?? '');
      index += 1;
    } else if (argument === '--output') {
      options.outputDirectory = path.resolve(args[index + 1] ?? '');
      index += 1;
    } else {
      throw new Error(`未知参数：${argument}`);
    }
  }
  return options;
}

function compareText(left, right) {
  return left.localeCompare(right, 'en');
}

const isCli = process.argv[1]
  && path.resolve(process.argv[1]) === fileURLToPath(import.meta.url);
if (isCli) {
  try {
    const options = parseArguments(process.argv.slice(2));
    await generateFullNetClient(options);
    process.stdout.write(options.check
      ? '客户端 OpenAPI 生成产物零漂移。\n'
      : `已生成客户端 OpenAPI 产物：${options.outputDirectory}\n`);
  } catch (error) {
    console.error(error instanceof Error ? error.message : String(error));
    process.exitCode = 1;
  }
}
