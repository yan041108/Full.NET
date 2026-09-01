import type { SettingsDictItem } from '@fullnet/client-contracts';
import { listSettingsDictItems, listSettingsDictTypes } from '../api/dict-types';

/** Host 用户档案表单依赖的字典类型编码，需与服务端种子和权限目录保持一致。 */
export const HOST_USER_PROFILE_DICT_CODES = {
  accountType: 'identity.account_type',
  idCardType: 'identity.id_card_type',
  ethnicity: 'identity.ethnicity',
  educationLevel: 'identity.education_level',
  emergencyContactRelation: 'identity.emergency_contact_relation'
} as const;

export type HostUserProfileDictCode =
  (typeof HOST_USER_PROFILE_DICT_CODES)[keyof typeof HOST_USER_PROFILE_DICT_CODES];

/** 档案表单下拉项的最小展示模型，只暴露界面真正需要的值和值标签。 */
export interface HostUserProfileDictOption {
  value: string;
  label: string;
}

/** 分页拉全某个字典类型的启用项，避免档案表单因后台分页而漏掉候选值。 */
async function fetchAllDictItems(dictTypeId: string): Promise<SettingsDictItem[]> {
  const pageSize = 100;
  const firstPage = await listSettingsDictItems(dictTypeId, 1, pageSize);
  const items = [...firstPage.items];
  const totalPages = Math.ceil(firstPage.total / pageSize);

  for (let page = 2; page <= totalPages; page += 1) {
    const nextPage = await listSettingsDictItems(dictTypeId, page, pageSize);
    items.push(...nextPage.items);
  }

  return items.filter(item => item.isActive);
}

/** 加载 Host 用户档案依赖的全部字典下拉项；缺失的字典类型保持空数组而不是抛错。 */
export async function loadHostUserProfileDictOptions(): Promise<
  Record<HostUserProfileDictCode, HostUserProfileDictOption[]>
> {
  const empty: Record<HostUserProfileDictCode, HostUserProfileDictOption[]> = {
    [HOST_USER_PROFILE_DICT_CODES.accountType]: [],
    [HOST_USER_PROFILE_DICT_CODES.idCardType]: [],
    [HOST_USER_PROFILE_DICT_CODES.ethnicity]: [],
    [HOST_USER_PROFILE_DICT_CODES.educationLevel]: [],
    [HOST_USER_PROFILE_DICT_CODES.emergencyContactRelation]: []
  };

  const pageSize = 100;
  const firstPage = await listSettingsDictTypes(1, pageSize);
  const dictTypes = [...firstPage.items];
  const totalPages = Math.ceil(firstPage.total / pageSize);
  for (let page = 2; page <= totalPages; page += 1) {
    const nextPage = await listSettingsDictTypes(page, pageSize);
    dictTypes.push(...nextPage.items);
  }

  const codeToId = new Map(
    dictTypes
      .filter(type => type.isActive)
      .map(type => [type.code, type.id] as const)
  );

  await Promise.all(
    (Object.values(HOST_USER_PROFILE_DICT_CODES) as HostUserProfileDictCode[]).map(
      async (code) => {
        const dictTypeId = codeToId.get(code);
        if (!dictTypeId) {
          return;
        }

        const items = await fetchAllDictItems(dictTypeId);
        empty[code] = items
          .sort((left, right) => left.displayOrder - right.displayOrder)
          .map(item => ({
            value: item.value,
            label: item.label
          }));
      }
    )
  );

  return empty;
}

/** 按本地日期计算年龄；无效日期或未来生日一律返回 null，避免展示误导性年龄。 */
export function computeAgeFromBirthDate(birthDate: string | null | undefined): number | null {
  if (!birthDate) {
    return null;
  }

  const parsed = new Date(`${birthDate}T00:00:00`);
  if (Number.isNaN(parsed.getTime())) {
    return null;
  }

  const today = new Date();
  let age = today.getFullYear() - parsed.getFullYear();
  const monthDelta = today.getMonth() - parsed.getMonth();
  if (monthDelta < 0 || (monthDelta === 0 && today.getDate() < parsed.getDate())) {
    age -= 1;
  }

  return age >= 0 ? age : null;
}
