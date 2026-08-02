import assert from 'node:assert/strict';
import { test } from 'node:test';
import { toOrganizationOwnedExplicitSchema } from '../tests/support/organization-owned-codegen-schema.mjs';

test('组织归属 Schema 助手声明 tenant.required 与 organization.unit', () => {
  const base = {
    hasVersion: true,
    dataScope: 'TenantRequired',
    columns: [
      { databaseName: 'Id' },
      { databaseName: 'TenantId' },
      { databaseName: 'Name' }
    ]
  };
  const schema = toOrganizationOwnedExplicitSchema(base);

  assert.equal(schema.dataScope, 'tenant.required');
  assert.equal(schema.entityCapabilities.ownershipMode, 'organization.unit');
  assert.equal(schema.entityCapabilities.deleteMode, 'soft.delete');
  assert.equal(schema.scene, 'single');
  assert.deepEqual(schema.relationships, []);
  assert.equal(schema.hasVersion, undefined);
  assert.ok(
    schema.columns.some(column => column.databaseName === 'OrganizationUnitId')
  );
  assert.ok(
    schema.columns.some(column => column.databaseName === 'IsDeleted')
  );
  assert.ok(
    schema.columns.some(column => column.databaseName === 'CreatedById')
  );
});

test('组织归属 Schema 助手不修改传入对象的原始列数组', () => {
  const columns = [{ databaseName: 'Id' }];
  const base = { hasVersion: true, columns };
  const schema = toOrganizationOwnedExplicitSchema(base);

  assert.equal(columns.length, 1);
  assert.ok(schema.columns.length > 1);
});