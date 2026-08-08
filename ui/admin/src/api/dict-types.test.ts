import { beforeEach, describe, expect, it, vi } from 'vitest';
import { request } from './http';
import {
  createSettingsDictItem,
  createSettingsDictType,
  disableSettingsDictItem,
  disableSettingsDictType,
  listSettingsDictItems,
  listSettingsDictTypes,
  updateSettingsDictItem,
  updateSettingsDictType
} from './dict-types';

vi.mock('./http', () => ({ request: vi.fn() }));
const requestMock = vi.mocked(request);

const sampleDictType = {
  id: '019bc2b1-2a40-7cc3-8992-a80de51bf296',
  code: 'gender',
  name: '性别',
  description: '通用性别枚举',
  displayOrder: 10,
  isActive: true,
  createdAtUtc: '2026-01-01T00:00:00+00:00',
  updatedAtUtc: null,
  version: 1
};

const sampleDictItem = {
  id: '019bc2b1-2a40-7cc3-8992-a80de51bf297',
  dictTypeId: sampleDictType.id,
  label: '男',
  value: 'male',
  color: '#409eff',
  displayOrder: 1,
  isActive: true,
  createdAtUtc: '2026-01-01T00:00:00+00:00',
  updatedAtUtc: null,
  version: 1
};

describe('Vue Settings 数据字典 API', () => {
  beforeEach(() => requestMock.mockReset());

  it('校验分页列表响应', async () => {
    requestMock.mockResolvedValueOnce({
      items: [sampleDictType],
      page: 1,
      pageSize: 20,
      total: 1
    });

    await expect(listSettingsDictTypes()).resolves.toMatchObject({ total: 1 });
    expect(requestMock).toHaveBeenCalledWith(
      '/api/v1/settings/dict-types?page=1&pageSize=20'
    );
  });

  it('通过 JSON 正文创建并禁用字典类型', async () => {
    requestMock
      .mockResolvedValueOnce(sampleDictType)
      .mockResolvedValueOnce({ ...sampleDictType, isActive: false, version: 2 });

    await expect(
      createSettingsDictType('gender', '性别', '通用性别枚举', 10)
    ).resolves.toMatchObject({ code: 'gender' });
    expect(requestMock).toHaveBeenNthCalledWith(
      1,
      '/api/v1/settings/dict-types',
      expect.objectContaining({
        method: 'POST',
        body: JSON.stringify({
          code: 'gender',
          name: '性别',
          description: '通用性别枚举',
          displayOrder: 10
        })
      })
    );

    await expect(disableSettingsDictType(sampleDictType.id))
      .resolves.toMatchObject({ isActive: false });
  });

  it('通过 JSON 正文更新字典类型', async () => {
    requestMock.mockResolvedValueOnce({ ...sampleDictType, name: '性别枚举', version: 2 });

    await expect(
      updateSettingsDictType(sampleDictType.id, '性别枚举', '说明', 10, 1)
    ).resolves.toMatchObject({ name: '性别枚举' });
    expect(requestMock).toHaveBeenCalledWith(
      `/api/v1/settings/dict-types/${sampleDictType.id}`,
      expect.objectContaining({
        method: 'PUT',
        body: JSON.stringify({
          name: '性别枚举',
          description: '说明',
          displayOrder: 10,
          version: 1
        })
      })
    );
  });

  it('按类型列出并创建、更新、禁用字典项', async () => {
    requestMock
      .mockResolvedValueOnce({
        items: [sampleDictItem],
        page: 1,
        pageSize: 20,
        total: 1
      })
      .mockResolvedValueOnce(sampleDictItem)
      .mockResolvedValueOnce({ ...sampleDictItem, label: '男性', version: 2 })
      .mockResolvedValueOnce({ ...sampleDictItem, isActive: false, version: 3 });

    await expect(listSettingsDictItems(sampleDictType.id))
      .resolves.toMatchObject({ total: 1 });
    expect(requestMock).toHaveBeenNthCalledWith(
      1,
      `/api/v1/settings/dict-types/${sampleDictType.id}/items?page=1&pageSize=20`
    );

    await expect(
      createSettingsDictItem(sampleDictType.id, '男', 'male', '#409eff', 1)
    ).resolves.toMatchObject({ value: 'male' });
    expect(requestMock).toHaveBeenNthCalledWith(
      2,
      `/api/v1/settings/dict-types/${sampleDictType.id}/items`,
      expect.objectContaining({
        method: 'POST',
        body: JSON.stringify({
          label: '男',
          value: 'male',
          color: '#409eff',
          displayOrder: 1
        })
      })
    );

    await expect(
      updateSettingsDictItem(sampleDictItem.id, '男性', '#409eff', 1, 1)
    ).resolves.toMatchObject({ label: '男性' });
    await expect(disableSettingsDictItem(sampleDictItem.id))
      .resolves.toMatchObject({ isActive: false });
  });
});
