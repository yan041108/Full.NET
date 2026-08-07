import { describe, expect, it, vi } from 'vitest';
import { flushPromises, mount } from '@vue/test-utils';
import { nextTick } from 'vue';
import type { OrganizationUnitTreeSelectOption } from '../../organization/org-unit-tree';
import UserEditorDialog from './UserEditorDialog.vue';
import { messages } from '@fullnet/admin-i18n';
import type { MessageKey } from '@fullnet/admin-i18n';

function translate(key: MessageKey): string {
  return messages['zh-CN'][key];
}

const baseProps = {
  open: true,
  mode: 'create' as const,
  user: null,
  username: '',
  displayName: '',
  password: '',
  profile: {},
  activeTab: 'basic' as const,
  transferRoles: [],
  selectedRoleIds: [] as string[],
  orgUnitTreeOptions: [] as OrganizationUnitTreeSelectOption[],
  positionOptions: [],
  primaryUnitId: '',
  subsidiaryUnitIds: [] as string[],
  positionId: '',
  identityCommitted: false,
  saving: false,
  canAssignRoles: false,
  canCreate: true,
  canUpdate: false,
  canManageUserUnits: false,
  canManageUserPositions: false,
  canSubmit: true,
  effectiveFieldKeys: [],
  showProfileTab: false,
  translate
};

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
