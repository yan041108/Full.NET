import {
  isWorkflowFormSchema,
  type WorkflowFormSchema
} from '@fullnet/client-contracts';

export interface WorkflowSchemaStorage {
  get(key: string): unknown;
  set(key: string, value: unknown): void;
  remove(key: string): void;
}

export interface WorkflowSchemaCacheOptions {
  readonly maximumEntries?: number;
}

export interface WorkflowSchemaCache {
  read(formVersionId: string, formSchemaHash: string): WorkflowFormSchema | undefined;
  write(formVersionId: string, formSchemaHash: string, schema: WorkflowFormSchema): void;
}

const cachePrefix = 'fullnet.workflow.visible-schema.v1.';
const indexKey = `${cachePrefix}index`;
const guidPattern = /^[0-9a-f]{8}-[0-9a-f]{4}-[1-8][0-9a-f]{3}-[89ab][0-9a-f]{3}-[0-9a-f]{12}$/i;
const hashPattern = /^[0-9a-f]{64}$/;

/**
 * 缓存服务端按节点策略裁剪后的不可变 Schema；缓存键包含投影摘要，且永不保存字段策略、提交值或授权结果。
 */
export function createWorkflowSchemaCache(
  storage: WorkflowSchemaStorage,
  options: WorkflowSchemaCacheOptions = {}
): WorkflowSchemaCache {
  const maximumEntries = normalizeMaximumEntries(options.maximumEntries);

  return {
    read(formVersionId, formSchemaHash) {
      const key = createEntryKey(formVersionId, formSchemaHash);
      if (!key) {
        return undefined;
      }

      try {
        const value = storage.get(key);
        if (typeof value !== 'string') {
          return undefined;
        }

        const parsed = JSON.parse(value) as unknown;
        if (!isCacheEntry(parsed, formVersionId, formSchemaHash)) {
          removeEntry(storage, key);
          return undefined;
        }

        return cloneSchema(parsed.schema);
      } catch {
        removeEntry(storage, key);
        return undefined;
      }
    },
    write(formVersionId, formSchemaHash, schema) {
      const key = createEntryKey(formVersionId, formSchemaHash);
      if (!key || !isWorkflowFormSchema(schema)) {
        throw new Error('workflow.form.invalid-cache-entry');
      }

      try {
        storage.set(key, JSON.stringify({
          formVersionId,
          formSchemaHash,
          schema
        }));
        const index = readIndex(storage).filter(entry => entry !== key);
        index.push(key);
        while (index.length > maximumEntries) {
          const evicted = index.shift();
          if (evicted) {
            storage.remove(evicted);
          }
        }
        storage.set(indexKey, JSON.stringify(index));
      } catch {
        // 缓存是纯优化；平台存储满额或不可用时继续使用当前 API 响应。
      }
    }
  };
}

/** 创建使用 uni-app 同步存储的三端缓存适配器。 */
export function createUniWorkflowSchemaCache(
  options: WorkflowSchemaCacheOptions = {}
): WorkflowSchemaCache {
  return createWorkflowSchemaCache({
    get: key => uni.getStorageSync(key),
    set: (key, value) => uni.setStorageSync(key, value),
    remove: key => uni.removeStorageSync(key)
  }, options);
}

interface WorkflowSchemaCacheEntry {
  readonly formVersionId: string;
  readonly formSchemaHash: string;
  readonly schema: WorkflowFormSchema;
}

function isCacheEntry(
  value: unknown,
  formVersionId: string,
  formSchemaHash: string
): value is WorkflowSchemaCacheEntry {
  if (typeof value !== 'object' || value === null || Array.isArray(value)) {
    return false;
  }

  const entry = value as Record<string, unknown>;
  return entry.formVersionId === formVersionId
    && entry.formSchemaHash === formSchemaHash
    && isWorkflowFormSchema(entry.schema);
}

function createEntryKey(formVersionId: string, formSchemaHash: string): string | undefined {
  return guidPattern.test(formVersionId) && hashPattern.test(formSchemaHash)
    ? `${cachePrefix}${formVersionId.toLowerCase()}.${formSchemaHash}`
    : undefined;
}

function readIndex(storage: WorkflowSchemaStorage): string[] {
  try {
    const value = storage.get(indexKey);
    if (typeof value !== 'string') {
      return [];
    }
    const parsed = JSON.parse(value) as unknown;
    return Array.isArray(parsed)
      ? parsed.filter((entry): entry is string =>
        typeof entry === 'string' && entry.startsWith(cachePrefix) && entry !== indexKey)
      : [];
  } catch {
    return [];
  }
}

function removeEntry(storage: WorkflowSchemaStorage, key: string): void {
  try {
    storage.remove(key);
    const index = readIndex(storage).filter(entry => entry !== key);
    storage.set(indexKey, JSON.stringify(index));
  } catch {
    // 损坏缓存无法删除时仍失败开放，调用方继续使用网络响应。
  }
}

function normalizeMaximumEntries(value: number | undefined): number {
  return Number.isSafeInteger(value) && Number(value) > 0 && Number(value) <= 100
    ? Number(value)
    : 24;
}

function cloneSchema(schema: WorkflowFormSchema): WorkflowFormSchema {
  return JSON.parse(JSON.stringify(schema)) as WorkflowFormSchema;
}
