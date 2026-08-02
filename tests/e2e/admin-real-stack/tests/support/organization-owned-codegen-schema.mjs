/**
 * 将默认 CRUD Schema 转为组织归属显式能力形状，供双端代码生成 E2E 复用。
 */
export function toOrganizationOwnedExplicitSchema(baseSchema) {
  const explicitSchema = structuredClone(baseSchema);
  delete explicitSchema.hasVersion;
  explicitSchema.dataScope = 'tenant.required';
  explicitSchema.entityCapabilities = {
    deleteMode: 'soft.delete',
    hasCreatedAudit: true,
    hasUpdatedAudit: true,
    hasDeletedAudit: true,
    hasVersion: true,
    ownershipMode: 'organization.unit'
  };
  explicitSchema.scene = 'single';
  explicitSchema.relationships = [];
  explicitSchema.columns.push(
    {
      databaseName: 'OrganizationUnitId',
      clrPropertyName: 'OrganizationUnitId',
      jsonPropertyName: 'organizationUnitId',
      scalarType: 'uuid',
      isNullable: false,
      maxLength: null,
      numericPrecision: null,
      numericScale: null
    },
    {
      databaseName: 'CreatedAtUtc',
      clrPropertyName: 'CreatedAtUtc',
      jsonPropertyName: 'createdAtUtc',
      scalarType: 'date.time.utc',
      isNullable: false,
      maxLength: null,
      numericPrecision: null,
      numericScale: null
    },
    {
      databaseName: 'CreatedById',
      clrPropertyName: 'CreatedById',
      jsonPropertyName: 'createdById',
      scalarType: 'uuid',
      isNullable: false,
      maxLength: null,
      numericPrecision: null,
      numericScale: null
    },
    {
      databaseName: 'UpdatedAtUtc',
      clrPropertyName: 'UpdatedAtUtc',
      jsonPropertyName: 'updatedAtUtc',
      scalarType: 'date.time.utc',
      isNullable: true,
      maxLength: null,
      numericPrecision: null,
      numericScale: null
    },
    {
      databaseName: 'UpdatedById',
      clrPropertyName: 'UpdatedById',
      jsonPropertyName: 'updatedById',
      scalarType: 'uuid',
      isNullable: true,
      maxLength: null,
      numericPrecision: null,
      numericScale: null
    },
    {
      databaseName: 'IsDeleted',
      clrPropertyName: 'IsDeleted',
      jsonPropertyName: 'isDeleted',
      scalarType: 'boolean',
      isNullable: false,
      maxLength: null,
      numericPrecision: null,
      numericScale: null
    },
    {
      databaseName: 'DeletedAtUtc',
      clrPropertyName: 'DeletedAtUtc',
      jsonPropertyName: 'deletedAtUtc',
      scalarType: 'date.time.utc',
      isNullable: true,
      maxLength: null,
      numericPrecision: null,
      numericScale: null
    },
    {
      databaseName: 'DeletedById',
      clrPropertyName: 'DeletedById',
      jsonPropertyName: 'deletedById',
      scalarType: 'uuid',
      isNullable: true,
      maxLength: null,
      numericPrecision: null,
      numericScale: null
    }
  );
  return explicitSchema;
}