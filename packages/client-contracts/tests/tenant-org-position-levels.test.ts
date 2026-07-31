import { describe, expect, it } from 'vitest';
import {
  isOrganizationPositionLevel,
  isOrganizationPositionLevelPage
} from '../src/tenant-org-position-levels';

const sample = {
  id: '01912345-6789-7abc-8def-0123456789ab',
  code: 'senior',
  name: '高级',
  displayOrder: 10,
  isActive: true,
  createdAtUtc: '2026-07-29T08:00:00.000Z',
  updatedAtUtc: null,
  version: 1
};

describe('Organization 职级契约', () => {
  it('接受合法职级与分页', () => {
    expect(isOrganizationPositionLevel(sample)).toBe(true);
    expect(isOrganizationPositionLevelPage({
      items: [sample],
      page: 1,
      pageSize: 20,
      total: 1
    })).toBe(true);
  });

  it('拒绝畸形职级', () => {
    expect(isOrganizationPositionLevel({
      ...sample,
      version: '1'
    })).toBe(false);
  });
});
