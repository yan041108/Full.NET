import { spawnSync } from 'node:child_process';
import { readFile, readdir } from 'node:fs/promises';
import path from 'node:path';
import { isDeepStrictEqual } from 'node:util';

const stableOperationFields = [
  'permission',
  'successStatus',
  'requestSchema',
  'responseSchema'
];

function formatValue(value) {
  return value === undefined ? '<missing>' : JSON.stringify(value);
}

function indexPaths(contract) {
  return new Map(
    (Array.isArray(contract.paths) ? contract.paths : []).map((entry) => [
      entry.path,
      entry
    ])
  );
}

function indexOperations(pathEntry) {
  return new Map(
    (
      Array.isArray(pathEntry.operations) ? pathEntry.operations : []
    ).map((operation) => [String(operation.method).toUpperCase(), operation])
  );
}

function validateContractIdentities(contracts, changes) {
  const identities = new Map();

  for (const [fileName, contract] of contracts) {
    const hasId = Object.hasOwn(contract, 'id');
    const hasVersion = Object.hasOwn(contract, 'version');
    if (!hasId && !hasVersion) {
      continue;
    }

    if (
      typeof contract.id !== 'string' ||
      contract.id.trim().length === 0 ||
      !Number.isInteger(contract.version) ||
      contract.version < 1
    ) {
      changes.push(
        `invalid contract identity: ${fileName} requires a non-empty ` +
          'string id and positive integer version'
      );
      continue;
    }

    const versionSuffixMatch = /-v([1-9]\d*)$/u.exec(contract.id);
    const identityVersion = versionSuffixMatch
      ? Number(versionSuffixMatch[1])
      : undefined;
    if (identityVersion !== contract.version) {
      const versionSuffix = versionSuffixMatch
        ? `v${versionSuffixMatch[1]}`
        : '<missing>';
      changes.push(
        `contract version mismatch: ${fileName} id suffix ${versionSuffix} ` +
          `does not match version=${String(contract.version)}`
      );
    }

    const expectedFileName = `${contract.id}.json`;
    if (fileName !== expectedFileName) {
      changes.push(
        `contract identity mismatch: ${fileName} must be named ` +
          expectedFileName
      );
    }

    const identityKey = JSON.stringify([contract.id, contract.version]);
    const existingFileName = identities.get(identityKey);
    if (existingFileName) {
      changes.push(
        `duplicate contract identity: ${existingFileName} and ${fileName} ` +
          `use id=${String(contract.id)}, version=${String(contract.version)}`
      );
      continue;
    }

    identities.set(identityKey, fileName);
  }
}

function validateStructuredContractKeys(contracts, changes) {
  for (const [fileName, contract] of contracts) {
    const paths = Array.isArray(contract.paths) ? contract.paths : [];
    const schemas = contract.schemas ?? {};
    const seenPaths = new Set();

    for (const pathEntry of paths) {
      if (seenPaths.has(pathEntry.path)) {
        changes.push(`duplicate contract path: ${fileName} ${pathEntry.path}`);
        continue;
      }

      seenPaths.add(pathEntry.path);
      const seenMethods = new Set();
      const operations = Array.isArray(pathEntry.operations)
        ? pathEntry.operations
        : [];
      for (const operation of operations) {
        const method = String(operation.method).toUpperCase();
        if (seenMethods.has(method)) {
          changes.push(
            `duplicate contract operation: ${fileName} ${method} ` +
              pathEntry.path
          );
          continue;
        }

        seenMethods.add(method);
        for (const fieldName of ['requestSchema', 'responseSchema']) {
          const schemaName = operation[fieldName];
          if (schemaName && !Object.hasOwn(schemas, schemaName)) {
            changes.push(
              `unknown schema reference: ${fileName} ${method} ` +
                `${pathEntry.path} ${fieldName}=${schemaName}`
            );
          }
        }
      }
    }

    for (const [schemaName, schema] of Object.entries(schemas)) {
      const seenProperties = new Set();
      const properties = Array.isArray(schema.properties)
        ? schema.properties
        : [];
      for (const propertyName of properties) {
        if (seenProperties.has(propertyName)) {
          changes.push(
            `duplicate schema property: ${fileName} ` +
              `${schemaName}.${propertyName}`
          );
          continue;
        }

        seenProperties.add(propertyName);
      }

      if (schema.itemSchema && !Object.hasOwn(schemas, schema.itemSchema)) {
        changes.push(
          `unknown schema reference: ${fileName} ${schemaName} ` +
            `itemSchema=${schema.itemSchema}`
        );
      }
    }
  }
}

function compareStructuredContract(fileName, baseline, current, changes) {
  for (const fieldName of Object.keys(baseline)) {
    if (
      fieldName === 'description' ||
      fieldName === 'paths' ||
      fieldName === 'schemas'
    ) {
      continue;
    }

    if (
      !isDeepStrictEqual(baseline[fieldName], current[fieldName])
      && !isContractIdentityRepair(fileName, fieldName, baseline, current)
    ) {
      changes.push(
        `stable setting changed: ${fileName} ${fieldName} ` +
          `(baseline=${formatValue(baseline[fieldName])}, ` +
          `current=${formatValue(current[fieldName])})`
      );
    }
  }

  const currentPaths = indexPaths(current);
  for (const baselinePath of Array.isArray(baseline.paths)
    ? baseline.paths
    : []) {
    const currentPath = currentPaths.get(baselinePath.path);
    if (!currentPath) {
      changes.push(`path removed: ${fileName} ${baselinePath.path}`);
      continue;
    }

    const currentOperations = indexOperations(currentPath);
    for (const baselineOperation of Array.isArray(baselinePath.operations)
      ? baselinePath.operations
      : []) {
      const method = String(baselineOperation.method).toUpperCase();
      const currentOperation = currentOperations.get(method);
      if (!currentOperation) {
        changes.push(
          `operation removed: ${fileName} ${method} ${baselinePath.path}`
        );
        continue;
      }

      for (const fieldName of stableOperationFields) {
        if (
          !isDeepStrictEqual(
            baselineOperation[fieldName],
            currentOperation[fieldName]
          )
        ) {
          changes.push(
            `operation changed: ${fileName} ${method} ` +
              `${baselinePath.path} ${fieldName} ` +
              `(baseline=${formatValue(baselineOperation[fieldName])}, ` +
              `current=${formatValue(currentOperation[fieldName])})`
          );
        }
      }
    }
  }

  const baselineSchemas = baseline.schemas ?? {};
  const currentSchemas = current.schemas ?? {};
  for (const [schemaName, baselineSchema] of Object.entries(baselineSchemas)) {
    const currentSchema = currentSchemas[schemaName];
    if (!currentSchema) {
      changes.push(`schema removed: ${fileName} ${schemaName}`);
      continue;
    }

    const currentProperties = new Set(
      Array.isArray(currentSchema.properties) ? currentSchema.properties : []
    );
    for (const propertyName of Array.isArray(baselineSchema.properties)
      ? baselineSchema.properties
      : []) {
      if (!currentProperties.has(propertyName)) {
        changes.push(
          `schema property removed: ${fileName} ${schemaName}.${propertyName}`
        );
      }
    }

    if (!isDeepStrictEqual(baselineSchema.itemSchema, currentSchema.itemSchema)) {
      changes.push(
        `schema itemSchema changed: ${fileName} ${schemaName} ` +
          `(baseline=${formatValue(baselineSchema.itemSchema)}, ` +
          `current=${formatValue(currentSchema.itemSchema)})`
      );
    }
  }
}

function isContractIdentityRepair(fileName, fieldName, baseline, current) {
  if (fieldName !== 'version' || baseline.id !== current.id) {
    return false;
  }

  const versionSuffixMatch = /-v([1-9]\d*)$/u.exec(current.id);
  if (!versionSuffixMatch || fileName !== `${current.id}.json`) {
    return false;
  }

  const identityVersion = Number(versionSuffixMatch[1]);
  // 只豁免“历史元数据本身无效、当前修成文件名所声明版本”的单向修复；
  // 路径、操作和 schema 仍由后续兼容比较完整约束。
  return baseline.version !== identityVersion && current.version === identityVersion;
}

function compareStableSettings(fileName, baseline, current, changes) {
  for (const [fieldName, baselineValue] of Object.entries(baseline)) {
    if (fieldName === 'description') {
      continue;
    }

    if (
      !isDeepStrictEqual(baselineValue, current[fieldName])
      && !isAdditiveCoverageManifestChange(
        fileName,
        baselineValue,
        current[fieldName]
      )
      && !isAllowedClientGenerationManifestChange(
        fileName,
        baselineValue,
        current[fieldName]
      )
      && !isAllowedClientOpenApiSnapshotChange(
        fileName,
        fieldName,
        baselineValue,
        current[fieldName]
      )
    ) {
      changes.push(
        `stable setting changed: ${fileName} ${fieldName} ` +
          `(baseline=${formatValue(baselineValue)}, ` +
          `current=${formatValue(current[fieldName])})`
      );
    }
  }
}

function isAdditiveCoverageManifestChange(fileName, baselineValue, currentValue) {
  if (
    fileName !== 'vue-client-coverage-v1.json'
    || !Array.isArray(baselineValue)
    || !Array.isArray(currentValue)
  ) {
    return false;
  }

  // 覆盖清单允许随新增 API 追加绑定，但既有绑定不得删除或静默改写。
  return baselineValue.every((baselineEntry) =>
    currentValue.some((currentEntry) =>
      isDeepStrictEqual(baselineEntry, currentEntry)));
}

function isAllowedClientGenerationManifestChange(fileName, baselineValue, currentValue) {
  if (
    fileName !== 'client-generation-manifest-v1.json'
    || !Array.isArray(baselineValue)
    || !Array.isArray(currentValue)
  ) {
    return false;
  }

  // 允许追加条目，以及既有条目仅做 pilot→generated 晋升；禁止删除、改写绑定或降级。
  return baselineValue.every((baselineEntry) =>
    currentValue.some((currentEntry) =>
      isClientGenerationManifestEntryCompatible(baselineEntry, currentEntry)));
}

function isClientGenerationManifestEntryCompatible(baselineEntry, currentEntry) {
  if (
    !isDeepStrictEqual(
      {
        operationId: baselineEntry?.operationId,
        apiModule: baselineEntry?.apiModule,
        generatedGroup: baselineEntry?.generatedGroup
      },
      {
        operationId: currentEntry?.operationId,
        apiModule: currentEntry?.apiModule,
        generatedGroup: currentEntry?.generatedGroup
      }
    )
  ) {
    return false;
  }

  if (isDeepStrictEqual(baselineEntry?.status, currentEntry?.status)) {
    return true;
  }

  return baselineEntry?.status === 'pilot' && currentEntry?.status === 'generated';
}

function isAllowedClientOpenApiSnapshotChange(fileName, fieldName, baselineValue, currentValue) {
  if (fileName !== 'fullnet-client-v1.openapi.json') {
    return false;
  }

  // 标准客户端快照允许按清单扩容：追加 path/method、schema、tag；既有 Operation/Schema 不得改写或删除。
  if (fieldName === 'paths') {
    return isAdditiveOpenApiPaths(baselineValue, currentValue);
  }

  if (fieldName === 'tags') {
    return isAdditiveOpenApiTags(baselineValue, currentValue);
  }

  if (fieldName === 'components') {
    return isAdditiveOpenApiComponents(baselineValue, currentValue);
  }

  return false;
}

function isAdditiveOpenApiPaths(baselinePaths, currentPaths) {
  if (
    !isPlainObject(baselinePaths)
    || !isPlainObject(currentPaths)
  ) {
    return false;
  }

  for (const [pathKey, baselinePathItem] of Object.entries(baselinePaths)) {
    const currentPathItem = currentPaths[pathKey];
    if (!isPlainObject(baselinePathItem) || !isPlainObject(currentPathItem)) {
      return false;
    }

    for (const [method, baselineOperation] of Object.entries(baselinePathItem)) {
      if (!isAdditiveOpenApiOperation(baselineOperation, currentPathItem[method])) {
        return false;
      }
    }
  }

  return true;
}

function isAdditiveOpenApiOperation(baselineOperation, currentOperation) {
  if (!isPlainObject(baselineOperation) || !isPlainObject(currentOperation)) {
    return false;
  }

  const allowsOptionalQueryExpansion = approvedOptionalQueryExpansionOperationIds.has(
    baselineOperation.operationId
  );
  const ignoredFields = allowsOptionalQueryExpansion
    ? new Set(['responses', 'parameters'])
    : new Set(['responses']);
  const baselineFields = Object.keys(baselineOperation).filter(field => !ignoredFields.has(field));
  const currentFields = Object.keys(currentOperation).filter(field => !ignoredFields.has(field));
  if (!isDeepStrictEqual(baselineFields, currentFields)
    || baselineFields.some(field =>
      !isDeepStrictEqual(baselineOperation[field], currentOperation[field]))) {
    return false;
  }

  if (allowsOptionalQueryExpansion
    && !isApprovedOptionalQueryParameterExpansion(
      baselineOperation.parameters,
      currentOperation.parameters
    )) {
    return false;
  }

  const baselineResponses = baselineOperation.responses;
  const currentResponses = currentOperation.responses;
  return isPlainObject(baselineResponses)
    && isPlainObject(currentResponses)
    && Object.entries(baselineResponses).every(([statusCode, response]) =>
      isDeepStrictEqual(response, currentResponses[statusCode]));
}

const approvedOptionalQueryExpansionOperationIds = new Set([
  'notificationsListHostAnnouncements',
  'notificationsListMyInboxMessages',
  'serialNumbersListRules'
]);

function isApprovedOptionalQueryParameterExpansion(baselineParameters, currentParameters) {
  const baseline = Array.isArray(baselineParameters) ? baselineParameters : [];
  if (!Array.isArray(currentParameters) || currentParameters.length < baseline.length) {
    return false;
  }

  const findBaselineParameter = (parameter) => baseline.find((candidate) =>
    candidate?.in === parameter?.in && candidate?.name === parameter?.name);

  // OpenAPI 参数数组顺序不承载语义；既有参数必须按位置和名称原样保留，新增项只能是非必填 query 参数。
  return baseline.every((parameter) => currentParameters.some((candidate) =>
    candidate?.in === parameter?.in
    && candidate?.name === parameter?.name
    && isDeepStrictEqual(parameter, candidate)))
    && currentParameters.filter((parameter) => !findBaselineParameter(parameter)).every(parameter =>
      isPlainObject(parameter)
      && parameter.in === 'query'
      && parameter.required !== true);
}

function isAdditiveOpenApiTags(baselineTags, currentTags) {
  if (!Array.isArray(baselineTags) || !Array.isArray(currentTags)) {
    return false;
  }

  return baselineTags.every((baselineTag) =>
    currentTags.some((currentTag) => isDeepStrictEqual(baselineTag, currentTag)));
}

function isAdditiveOpenApiComponents(baselineComponents, currentComponents) {
  if (!isPlainObject(baselineComponents) || !isPlainObject(currentComponents)) {
    return false;
  }

  for (const [sectionName, baselineSection] of Object.entries(baselineComponents)) {
    const currentSection = currentComponents[sectionName];
    if (sectionName === 'schemas') {
      if (!isPlainObject(baselineSection) || !isPlainObject(currentSection)) {
        return false;
      }

      for (const [schemaName, baselineSchema] of Object.entries(baselineSection)) {
        if (!isCompatibleOpenApiSchemaRepair(
          schemaName,
          baselineSchema,
          currentSection[schemaName]
        )) {
          return false;
        }
      }
      continue;
    }

    if (!isDeepStrictEqual(baselineSection, currentSection)) {
      return false;
    }
  }

  return true;
}

const strictWorkflowSchemaMetadataRepairs = new Set([
  'WorkflowDefinitionDraft',
  'WorkflowNodeDraft'
]);

function isCompatibleOpenApiSchemaRepair(schemaName, baselineSchema, currentSchema) {
  if (isDeepStrictEqual(baselineSchema, currentSchema)) {
    return true;
  }

  if (isApprovedAdditiveSchemaEvolution(schemaName, baselineSchema, currentSchema)) {
    return true;
  }

  if (isApprovedJsonOmissionOptionalityRepair(
    schemaName,
    baselineSchema,
    currentSchema
  )) {
    return true;
  }

  // 这两个草稿类型在历史运行时已经拒绝未知字段，只是标准客户端快照遗漏了对应元数据。
  // 豁免精确限制为补上 additionalProperties=false，禁止借纠正快照改写其它 Schema 结构。
  if (!strictWorkflowSchemaMetadataRepairs.has(schemaName)
    || !isPlainObject(baselineSchema)
    || !isPlainObject(currentSchema)
    || Object.hasOwn(baselineSchema, 'additionalProperties')
    || currentSchema.additionalProperties !== false) {
    return false;
  }

  const repairedSchema = { ...currentSchema };
  delete repairedSchema.additionalProperties;
  return isDeepStrictEqual(baselineSchema, repairedSchema);
}

const approvedJsonOmissionOptionalityRepairs = new Map([
  ['CodeGenerationPreviewRequest', new Set(['hasVersion'])]
]);

function isApprovedJsonOmissionOptionalityRepair(
  schemaName,
  baselineSchema,
  currentSchema
) {
  const optionalProperties = approvedJsonOmissionOptionalityRepairs.get(schemaName);
  if (!optionalProperties
    || !isPlainObject(baselineSchema)
    || !isPlainObject(currentSchema)) {
    return false;
  }

  const baselineWithoutRequired = { ...baselineSchema };
  const currentWithoutRequired = { ...currentSchema };
  delete baselineWithoutRequired.required;
  delete currentWithoutRequired.required;
  if (!isDeepStrictEqual(baselineWithoutRequired, currentWithoutRequired)) {
    return false;
  }

  const baselineRequired = Array.isArray(baselineSchema.required)
    ? baselineSchema.required
    : [];
  const currentRequired = Array.isArray(currentSchema.required)
    ? currentSchema.required
    : [];

  // 豁免只允许移除已核实会被 System.Text.Json 省略的键，禁止借机改写其它必填性或 Schema 结构。
  return baselineRequired.some(propertyName => optionalProperties.has(propertyName))
    && isDeepStrictEqual(
      baselineRequired.filter(propertyName => !optionalProperties.has(propertyName)),
      currentRequired
    );
}

const approvedOptionalRequestSchemaEvolutions = new Set([
  'CreateHostAnnouncementRequest',
  'PreviewSerialNumberRequest',
  'UpdateHostAnnouncementRequest'
]);

const approvedResponseSchemaEvolutions = new Set([
  'HostAnnouncementResponse',
  'SerialNumberPreviewResponse'
]);

function isApprovedAdditiveSchemaEvolution(schemaName, baselineSchema, currentSchema) {
  const isOptionalRequest = approvedOptionalRequestSchemaEvolutions.has(schemaName);
  const isResponse = approvedResponseSchemaEvolutions.has(schemaName);
  if ((!isOptionalRequest && !isResponse)
    || !isPlainObject(baselineSchema)
    || !isPlainObject(currentSchema)
    || !isPlainObject(baselineSchema.properties)
    || !isPlainObject(currentSchema.properties)) {
    return false;
  }

  const baselineWithoutShape = { ...baselineSchema };
  const currentWithoutShape = { ...currentSchema };
  delete baselineWithoutShape.properties;
  delete baselineWithoutShape.required;
  delete currentWithoutShape.properties;
  delete currentWithoutShape.required;
  if (!isDeepStrictEqual(baselineWithoutShape, currentWithoutShape)) {
    return false;
  }

  if (!Object.entries(baselineSchema.properties).every(([propertyName, propertySchema]) =>
    isDeepStrictEqual(propertySchema, currentSchema.properties[propertyName]))) {
    return false;
  }

  const baselineRequired = Array.isArray(baselineSchema.required) ? baselineSchema.required : [];
  const currentRequired = Array.isArray(currentSchema.required) ? currentSchema.required : [];
  if (!baselineRequired.every((propertyName) => currentRequired.includes(propertyName))) {
    return false;
  }

  // required 数组的顺序不承载 OpenAPI 语义；请求扩展仍禁止新增必填项，响应可声明新增字段为必返事实。
  return isResponse
    || (currentRequired.length === baselineRequired.length
      && currentRequired.every((propertyName) => baselineRequired.includes(propertyName)));
}

function isPlainObject(value) {
  return value !== null && typeof value === 'object' && !Array.isArray(value);
}

export function compareContractSets(baselineContracts, currentContracts) {
  const changes = [];

  validateContractIdentities(currentContracts, changes);
  validateStructuredContractKeys(currentContracts, changes);

  for (const [fileName, baseline] of baselineContracts) {
    const current = currentContracts.get(fileName);
    if (!current) {
      changes.push(`contract removed: ${fileName}`);
      continue;
    }

    if (Array.isArray(baseline.paths) && baseline.schemas) {
      compareStructuredContract(fileName, baseline, current, changes);
    } else {
      compareStableSettings(fileName, baseline, current, changes);
    }
  }

  return changes.sort((left, right) => left.localeCompare(right, 'en'));
}

async function parseContract(fileName, content) {
  try {
    return JSON.parse(content);
  } catch (error) {
    throw new Error(`Invalid OpenAPI contract JSON in ${fileName}: ${error.message}`);
  }
}

export async function loadContractsFromDirectory(directoryPath) {
  const entries = await readdir(directoryPath, { withFileTypes: true });
  const fileNames = entries
    .filter((entry) => entry.isFile() && entry.name.endsWith('.json'))
    .map((entry) => entry.name)
    .sort((left, right) => left.localeCompare(right, 'en'));
  const contracts = new Map();

  for (const fileName of fileNames) {
    const content = await readFile(path.join(directoryPath, fileName), 'utf8');
    contracts.set(fileName, await parseContract(fileName, content));
  }

  return contracts;
}

function runGit(repositoryRoot, argumentsList) {
  return spawnSync('git', ['-C', repositoryRoot, ...argumentsList], {
    encoding: 'utf8',
    maxBuffer: 16 * 1024 * 1024
  });
}

export async function loadContractsAtGitRef(repositoryRoot, baseRef) {
  const listResult = runGit(repositoryRoot, [
    'ls-tree',
    '-r',
    '--name-only',
    baseRef,
    '--',
    'contracts/openapi'
  ]);

  if (listResult.status !== 0) {
    const detail = listResult.stderr.trim() || `git exited ${listResult.status}`;
    throw new Error(
      `Unable to load OpenAPI baseline from Git ref "${baseRef}": ${detail}`
    );
  }

  const relativePaths = listResult.stdout
    .split(/\r?\n/u)
    .filter((relativePath) => relativePath.endsWith('.json'))
    .sort((left, right) => left.localeCompare(right, 'en'));
  const contracts = new Map();

  for (const relativePath of relativePaths) {
    const showResult = runGit(repositoryRoot, [
      'show',
      `${baseRef}:${relativePath}`
    ]);
    if (showResult.status !== 0) {
      const detail = showResult.stderr.trim() || `git exited ${showResult.status}`;
      throw new Error(
        `Unable to load OpenAPI baseline from Git ref "${baseRef}": ${detail}`
      );
    }

    const fileName = path.basename(relativePath);
    contracts.set(fileName, await parseContract(relativePath, showResult.stdout));
  }

  return contracts;
}
