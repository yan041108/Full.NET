const httpMethods = ['get', 'post', 'put', 'patch', 'delete'];
const rootKeyOrder = [
  'openapi',
  'info',
  'jsonSchemaDialect',
  'security',
  'tags',
  'paths',
  'components'
];
const volatileKeyPattern = /^(?:x-)?(?:generated[-_]?at|timestamp)$/iu;

export function normalizeClientOpenApi(document, operationIds) {
  if (!isObject(document)) {
    throw new TypeError('OpenAPI document must be an object');
  }
  if (!Array.isArray(operationIds) || operationIds.length === 0) {
    throw new TypeError('operationIds must be a non-empty array');
  }

  const requestedOperationIds = new Set(operationIds);
  if (requestedOperationIds.size !== operationIds.length) {
    throw new Error('manifest operationId values must be unique');
  }

  const foundOperationIds = new Set();
  const selectedPaths = selectPaths(
    document.paths,
    requestedOperationIds,
    foundOperationIds
  );
  const missingOperationIds = operationIds
    .filter(operationId => !foundOperationIds.has(operationId))
    .sort(compareText);
  if (missingOperationIds.length > 0) {
    throw new Error(`manifest operationId not found: ${missingOperationIds.join(', ')}`);
  }

  const selectedComponents = selectReferencedComponents(document, selectedPaths);
  const normalized = {
    openapi: document.openapi,
    info: stripVolatile(document.info),
    paths: selectedPaths
  };
  if (document.jsonSchemaDialect !== undefined) {
    normalized.jsonSchemaDialect = document.jsonSchemaDialect;
  }
  if (document.security !== undefined) {
    normalized.security = stripVolatile(document.security);
  }

  const selectedTagNames = collectTagNames(selectedPaths);
  if (Array.isArray(document.tags)) {
    const tags = document.tags
      .filter(tag => isObject(tag) && selectedTagNames.has(tag.name))
      .map(stripVolatile);
    if (tags.length > 0) {
      normalized.tags = tags;
    }
  }
  if (Object.keys(selectedComponents).length > 0) {
    normalized.components = selectedComponents;
  }

  return canonicalize(normalized, true);
}

export function serializeClientOpenApi(document) {
  return `${JSON.stringify(canonicalize(document, true), null, 2)}\n`;
}

function selectPaths(paths, requestedOperationIds, foundOperationIds) {
  const selectedPaths = {};
  if (!isObject(paths)) {
    return selectedPaths;
  }

  for (const pathName of Object.keys(paths).sort(compareText)) {
    const pathItem = paths[pathName];
    if (!isObject(pathItem)) {
      continue;
    }

    const selectedPathItem = {};
    for (const method of httpMethods) {
      const operation = pathItem[method];
      if (!isObject(operation)
        || !requestedOperationIds.has(operation.operationId)) {
        continue;
      }
      if (foundOperationIds.has(operation.operationId)) {
        throw new Error(`duplicate selected operationId: ${operation.operationId}`);
      }
      foundOperationIds.add(operation.operationId);
      selectedPathItem[method] = stripVolatile(operation);
    }

    if (Object.keys(selectedPathItem).length === 0) {
      continue;
    }
    for (const key of ['summary', 'description', 'parameters']) {
      if (pathItem[key] !== undefined) {
        selectedPathItem[key] = stripVolatile(pathItem[key]);
      }
    }
    selectedPaths[pathName] = selectedPathItem;
  }

  return selectedPaths;
}

function selectReferencedComponents(document, selectedPaths) {
  const components = isObject(document.components) ? document.components : {};
  const selected = {};
  const pendingReferences = collectReferences(selectedPaths);
  const visitedReferences = new Set();

  while (pendingReferences.length > 0) {
    const reference = pendingReferences.pop();
    if (visitedReferences.has(reference)) {
      continue;
    }
    visitedReferences.add(reference);

    const target = parseComponentReference(reference);
    if (target === null) {
      throw new Error(`unsupported OpenAPI reference: ${reference}`);
    }
    const section = components[target.section];
    if (!isObject(section) || !Object.hasOwn(section, target.name)) {
      throw new Error(`OpenAPI reference target not found: ${reference}`);
    }

    selected[target.section] ??= {};
    if (!Object.hasOwn(selected[target.section], target.name)) {
      const value = stripVolatile(section[target.name]);
      selected[target.section][target.name] = value;
      pendingReferences.push(...collectReferences(value));
    }
  }

  const securitySchemeNames = collectSecuritySchemeNames(
    selectedPaths,
    document.security
  );
  for (const name of [...securitySchemeNames].sort(compareText)) {
    if (!isObject(components.securitySchemes)
      || !Object.hasOwn(components.securitySchemes, name)) {
      throw new Error(`OpenAPI security scheme not found: ${name}`);
    }
    selected.securitySchemes ??= {};
    selected.securitySchemes[name] = stripVolatile(components.securitySchemes[name]);
  }

  return selected;
}

function collectReferences(value, references = []) {
  if (Array.isArray(value)) {
    for (const item of value) {
      collectReferences(item, references);
    }
    return references;
  }
  if (!isObject(value)) {
    return references;
  }

  if (typeof value.$ref === 'string') {
    references.push(value.$ref);
  }
  for (const [key, child] of Object.entries(value)) {
    if (key !== '$ref') {
      collectReferences(child, references);
    }
  }
  return references;
}

function parseComponentReference(reference) {
  const match = /^#\/components\/([^/]+)\/([^/]+)$/u.exec(reference);
  if (!match) {
    return null;
  }
  return {
    section: decodePointerSegment(match[1]),
    name: decodePointerSegment(match[2])
  };
}

function decodePointerSegment(value) {
  return value
    .replaceAll('~1', '/')
    .replaceAll('~0', '~');
}

function collectSecuritySchemeNames(paths, rootSecurity) {
  const names = new Set();
  visitSecurityRequirements(rootSecurity, names);
  for (const pathItem of Object.values(paths)) {
    for (const method of httpMethods) {
      if (isObject(pathItem[method])) {
        visitSecurityRequirements(pathItem[method].security, names);
      }
    }
  }
  return names;
}

function visitSecurityRequirements(security, names) {
  if (!Array.isArray(security)) {
    return;
  }
  for (const requirement of security) {
    if (!isObject(requirement)) {
      continue;
    }
    for (const name of Object.keys(requirement)) {
      names.add(name);
    }
  }
}

function collectTagNames(paths) {
  const names = new Set();
  for (const pathItem of Object.values(paths)) {
    for (const method of httpMethods) {
      const tags = pathItem[method]?.tags;
      if (!Array.isArray(tags)) {
        continue;
      }
      for (const tag of tags) {
        if (typeof tag === 'string') {
          names.add(tag);
        }
      }
    }
  }
  return names;
}

function stripVolatile(value) {
  if (Array.isArray(value)) {
    return value.map(stripVolatile);
  }
  if (!isObject(value)) {
    return value;
  }

  const result = {};
  for (const [key, child] of Object.entries(value)) {
    if (key === 'servers' || volatileKeyPattern.test(key)) {
      continue;
    }
    result[key] = stripVolatile(child);
  }
  return result;
}

function canonicalize(value, isRoot = false) {
  if (Array.isArray(value)) {
    return value.map(item => canonicalize(item));
  }
  if (!isObject(value)) {
    return value;
  }

  const keys = Object.keys(value);
  keys.sort(isRoot ? compareRootKeys : compareText);
  const result = {};
  for (const key of keys) {
    result[key] = canonicalize(value[key]);
  }
  return result;
}

function compareRootKeys(left, right) {
  const leftIndex = rootKeyOrder.indexOf(left);
  const rightIndex = rootKeyOrder.indexOf(right);
  if (leftIndex !== -1 || rightIndex !== -1) {
    if (leftIndex === -1) {
      return 1;
    }
    if (rightIndex === -1) {
      return -1;
    }
    return leftIndex - rightIndex;
  }
  return compareText(left, right);
}

function isObject(value) {
  return value !== null && typeof value === 'object' && !Array.isArray(value);
}

function compareText(left, right) {
  return left.localeCompare(right, 'en');
}
