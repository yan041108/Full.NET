export type FieldProjectionSensitivity = 'public' | 'internal' | 'sensitive' | 0 | 1 | 2;
export type FieldProjectionDefaultVisibility = 'mandatory' | 'restricted' | 0 | 1;

export interface FieldProjectionFieldDefinition {
  fieldKey: string;
  displayName: string;
  sensitivity: FieldProjectionSensitivity;
  defaultVisibility: FieldProjectionDefaultVisibility;
  assignable: boolean;
}

export interface FieldProjectionResourceDefinition {
  resourceKey: string;
  displayName: string;
  fields: FieldProjectionFieldDefinition[];
}

export interface HostRoleFieldGrants {
  roleId: string;
  resourceKey: string;
  fieldKeys: string[];
  version: number;
}

export function isFieldProjectionCatalog(
  value: unknown
): value is FieldProjectionResourceDefinition[] {
  return Array.isArray(value) && value.every(resource => isRecord(resource)
    && isText(resource.resourceKey)
    && typeof resource.displayName === 'string'
    && Array.isArray(resource.fields)
    && resource.fields.every(field => isRecord(field)
      && isText(field.fieldKey)
      && typeof field.displayName === 'string'
      && typeof field.assignable === 'boolean'));
}

export function isHostRoleFieldGrants(value: unknown): value is HostRoleFieldGrants {
  return isRecord(value)
    && isText(value.roleId)
    && isText(value.resourceKey)
    && Array.isArray(value.fieldKeys)
    && value.fieldKeys.every(isText)
    && typeof value.version === 'number';
}

function isText(value: unknown): value is string {
  return typeof value === 'string' && value.length > 0;
}

function isRecord(value: unknown): value is Record<string, unknown> {
  return typeof value === 'object' && value !== null;
}
