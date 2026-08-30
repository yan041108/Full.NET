import { isWorkflowFormSchema, type WorkflowFormSchema } from '@fullnet/client-contracts';
import { describe, expect, it } from 'vitest';
import workflowFixture from '../../../packages/client-contracts/src/fixtures/workflow-form-schema-v1.json';
import {
  createWorkflowSchemaCache,
  type WorkflowSchemaStorage
} from '../src/features/workflow/workflow-schema-cache';

const versionId = '01912345-6789-7abc-8def-0123456789af';
const schemaHash = workflowFixture.contentHash;

function readFixtureSchema(): WorkflowFormSchema {
  const value: unknown = workflowFixture.formSchema;
  if (!isWorkflowFormSchema(value)) {
    throw new Error('Invalid workflow Golden Schema fixture.');
  }
  return value;
}

function createStorage(): WorkflowSchemaStorage & { readonly values: Map<string, unknown> } {
  const values = new Map<string, unknown>();
  return {
    values,
    get: key => values.get(key),
    set: (key, value) => values.set(key, value),
    remove: key => values.delete(key)
  };
}

describe('workflow schema cache', () => {
  it('keys immutable visible schemas by form version and schema hash only', () => {
    const storage = createStorage();
    const cache = createWorkflowSchemaCache(storage);

    cache.write(versionId, schemaHash, readFixtureSchema());

    expect(cache.read(versionId, schemaHash)).toEqual(workflowFixture.formSchema);
    expect([...storage.values.keys()].some(key =>
      key.includes(versionId) && key.includes(schemaHash))).toBe(true);
    expect(JSON.stringify([...storage.values.values()])).not.toContain('fieldPolicies');
    expect(JSON.stringify([...storage.values.values()])).not.toContain('submission');
  });

  it('rejects and removes a corrupted or unsupported cached schema', () => {
    const storage = createStorage();
    const cache = createWorkflowSchemaCache(storage);
    cache.write(versionId, schemaHash, readFixtureSchema());
    const entryKey = [...storage.values.keys()].find(key => key.includes(versionId))!;
    storage.values.set(entryKey, JSON.stringify({ schemaVersion: 99 }));

    expect(cache.read(versionId, schemaHash)).toBeUndefined();
    expect(storage.values.has(entryKey)).toBe(false);
  });

  it('keeps storage bounded and evicts the oldest immutable schema', () => {
    const storage = createStorage();
    const cache = createWorkflowSchemaCache(storage, { maximumEntries: 2 });
    const hashes = ['1', '2', '3'].map(value => value.repeat(64));

    for (const hash of hashes) {
      cache.write(versionId, hash, readFixtureSchema());
    }

    expect(cache.read(versionId, hashes[0]!)).toBeUndefined();
    expect(cache.read(versionId, hashes[1]!)).toEqual(workflowFixture.formSchema);
    expect(cache.read(versionId, hashes[2]!)).toEqual(workflowFixture.formSchema);
  });
});
