import { describe, expect, it } from 'vitest';
import {
  isFieldProjectionCatalog,
  isHostRoleFieldGrants
} from '../src/field-projections';

describe('字段投影契约', () => {
  it('只接受稳定资源、字段目录和版本化角色授权', () => {
    expect(isFieldProjectionCatalog([{
      resourceKey: 'identity.host_users',
      displayName: 'Host 用户',
      fields: [{
        fieldKey: 'preferred_locale',
        displayName: '区域偏好',
        sensitivity: 1,
        defaultVisibility: 1,
        assignable: true
      }]
    }])).toBe(true);
    expect(isHostRoleFieldGrants({
      roleId: 'role-id',
      resourceKey: 'identity.host_users',
      fieldKeys: ['preferred_locale'],
      version: 2
    })).toBe(true);
    expect(isHostRoleFieldGrants({
      roleId: 'role-id',
      resourceKey: 'identity.host_users',
      fieldKeys: ['preferred_locale']
    })).toBe(false);
  });
});
