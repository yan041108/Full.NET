import { describe, expect, it, vi } from 'vitest';
import { flushPromises, mount } from '@vue/test-utils';
import { nextTick } from 'vue';
import type { OrganizationUnitTreeSelectOption } from '../../organization/org-unit-tree';
import UserEditorDialog from './UserEditorDialog.vue';
import { messages } from '@fullnet/admin-i18n';
import type { MessageKey } from '@fullnet/admin-i18n';
import type { HostUserProfileWrite } from '@fullnet/client-contracts';

function translate(key: MessageKey): string {
  return messages['zh-CN'][key];
}

// 中文注释：HostUserProfileDictOption 与 UserEditorDialog.vue 中 defineProps 的字典项类型一致
interface HostUserProfileDictOption {
  value: string;
  label: string;
}

// 中文注释：createProps() 返回组件全部 required props，严格对齐 UserEditorDialog.defineProps 类型
// 禁止使用 Record<string, unknown> 绕过类型检查；若组件新增 required props 必须在此同步补充
function createProps(overrides: Partial<{
  open: boolean;
  mode: 'create' | 'edit';
  user: import('@fullnet/client-contracts').HostUser | null;
  username: string;
  displayName: string;
  accountType: string;
  password: string;
  profile: HostUserProfileWrite;
  activeTab: 'basic' | 'roles' | 'org-units' | 'org-positions' | 'profile' | 'binding';
  transferRoles: Array<{ key: string; label: string; disabled?: boolean }>;
  selectedRoleIds: string[];
  orgUnitTreeOptions: OrganizationUnitTreeSelectOption[];
  positionOptions: Array<{ value: string; label: string }>;
  primaryUnitId: string;
  subsidiaryUnitIds: string[];
  positionId: string;
  identityCommitted: boolean;
  saving: boolean;
  canAssignRoles: boolean;
  canCreate: boolean;
  canUpdate: boolean;
  canManageUserUnits: boolean;
  canManageUserPositions: boolean;
  canViewUserUnits: boolean;
  canViewUserPositions: boolean;
  accountTypeOptions: HostUserProfileDictOption[];
  idCardTypeOptions: HostUserProfileDictOption[];
  ethnicityOptions: HostUserProfileDictOption[];
  educationLevelOptions: HostUserProfileDictOption[];
  emergencyContactRelationOptions: HostUserProfileDictOption[];
  canSubmit: boolean;
  effectiveFieldKeys: string[];
  showProfileTab: boolean;
  translate: (key: MessageKey) => string;
}> = {}): {
  open: boolean;
  mode: 'create' | 'edit';
  user: import('@fullnet/client-contracts').HostUser | null;
  username: string;
  displayName: string;
  accountType: string;
  password: string;
  profile: HostUserProfileWrite;
  activeTab: 'basic' | 'roles' | 'org-units' | 'org-positions' | 'profile' | 'binding';
  transferRoles: Array<{ key: string; label: string; disabled?: boolean }>;
  selectedRoleIds: string[];
  orgUnitTreeOptions: OrganizationUnitTreeSelectOption[];
  positionOptions: Array<{ value: string; label: string }>;
  primaryUnitId: string;
  subsidiaryUnitIds: string[];
  positionId: string;
  identityCommitted: boolean;
  saving: boolean;
  canAssignRoles: boolean;
  canCreate: boolean;
  canUpdate: boolean;
  canManageUserUnits: boolean;
  canManageUserPositions: boolean;
  canViewUserUnits: boolean;
  canViewUserPositions: boolean;
  accountTypeOptions: HostUserProfileDictOption[];
  idCardTypeOptions: HostUserProfileDictOption[];
  ethnicityOptions: HostUserProfileDictOption[];
  educationLevelOptions: HostUserProfileDictOption[];
  emergencyContactRelationOptions: HostUserProfileDictOption[];
  canSubmit: boolean;
  effectiveFieldKeys: string[];
  showProfileTab: boolean;
  translate: (key: MessageKey) => string;
} {
  return {
    open: true,
    mode: 'create',
    user: null,
    username: '',
    displayName: '',
    // 中文注释：accountType 默认值取自 C# 枚举 AccountType.NormalUser，对应前端字典编码 'normal_user'
    accountType: 'normal_user',
    password: '',
    profile: {} as HostUserProfileWrite,
    activeTab: 'basic',
    transferRoles: [],
    selectedRoleIds: [],
    orgUnitTreeOptions: [] as OrganizationUnitTreeSelectOption[],
    positionOptions: [],
    primaryUnitId: '',
    subsidiaryUnitIds: [],
    positionId: '',
    identityCommitted: false,
    saving: false,
    canAssignRoles: false,
    canCreate: true,
    canUpdate: false,
    canManageUserUnits: false,
    canManageUserPositions: false,
    // 中文注释：canViewUserUnits / canViewUserPositions 对应组件 props 的 required 字段，控制机构/职位页签显示
    canViewUserUnits: true,
    canViewUserPositions: true,
    // 中文注释：以下字典选项数组与 profileDictOptions[HOST_USER_PROFILE_DICT_CODES.*] 来源一致，默认传空
    accountTypeOptions: [],
    idCardTypeOptions: [],
    ethnicityOptions: [],
    educationLevelOptions: [],
    emergencyContactRelationOptions: [],
    canSubmit: true,
    effectiveFieldKeys: [],
    showProfileTab: false,
    translate,
    ...overrides
  };
}

const baseProps = createProps();

describe('UserEditorDialog', () => {
  it('普通管理员看不到敏感用户档案入口和字段', async () => {
    const wrapper = mount(UserEditorDialog, {
      props: baseProps,
      attachTo: document.body
    });
    await flushPromises();

    expect(wrapper.text()).not.toContain(translate('users.tabProfile'));
    expect(wrapper.text()).not.toContain(translate('users.phone'));
    expect(wrapper.text()).not.toContain(translate('users.email'));
    wrapper.unmount();
  });

  it('创建模式下缺少必填项会阻止校验通过', async () => {
    const wrapper = mount(UserEditorDialog, {
      props: baseProps,
      attachTo: document.body
    });
    await flushPromises();
    await nextTick();
    await flushPromises();

    const vm = wrapper.vm as {
      validateBasicForm: () => boolean;
      onSubmitClick: () => Promise<void>;
    };
    const valid = vm.validateBasicForm();
    expect(valid).toBe(false);
    wrapper.unmount();
  });

  it('点击确定会在校验失败时阻止提交', async () => {
    const onSubmit = vi.fn();
    const wrapper = mount(UserEditorDialog, {
      props: {
        ...baseProps,
        onSubmit
      },
      attachTo: document.body
    });
    await flushPromises();
    await nextTick();
    await flushPromises();

    const vm = wrapper.vm as { onSubmitClick: () => Promise<void> };
    await vm.onSubmitClick();
    await flushPromises();

    expect(onSubmit).not.toHaveBeenCalled();
    wrapper.unmount();
  });
});
