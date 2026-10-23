import { describe, expect, it, vi } from 'vitest';
import { mount } from '@vue/test-utils';
import NotificationPreferenceEditor from './NotificationPreferenceEditor.vue';
import { showWarning } from '../feedback/fullNetMessage';

vi.mock('../feedback/fullNetMessage', () => ({ showWarning: vi.fn() }));

function editor(overrides = {}) {
  return mount(NotificationPreferenceEditor, {
    props: {
      channelKey: 'email',
      optedOut: false,
      marketingConsent: false,
      quietHoursJson: null,
      canUpdate: true,
      saving: false,
      ...overrides
    }
  });
}

describe('渠道发送偏好编辑器', () => {
  it('只读用户不渲染保存入口', () => {
    const wrapper = editor({ canUpdate: false });
    expect(wrapper.find('[data-testid="preference-save"]').exists()).toBe(false);
    expect(wrapper.emitted('save')).toBeUndefined();
  });

  it('提交渠道关闭和撤回营销同意，不混入端点或身份', async () => {
    const wrapper = editor({ optedOut: true, marketingConsent: false });
    await wrapper.get('[data-testid="preference-save"]').trigger('click');
    expect(wrapper.emitted('save')).toEqual([[
      { optedOut: true, marketingConsent: false, quietHoursJson: null }
    ]]);
  });

  it('保持服务端静默时区与跨午夜时段', async () => {
    const quiet = { timeZone: 'Asia/Shanghai', start: '22:00', end: '08:00' };
    const wrapper = editor({ quietHoursJson: JSON.stringify(quiet) });
    await wrapper.get('[data-testid="preference-save"]').trigger('click');
    expect(wrapper.emitted('save')).toEqual([[
      { optedOut: false, marketingConsent: false, quietHoursJson: JSON.stringify(quiet) }
    ]]);
  });

  it('无效时区或全天相同时间不提交', async () => {
    const wrapper = editor({
      quietHoursJson: '{"timeZone":"Asia/Shanghai","start":"22:00","end":"08:00"}'
    });
    await wrapper.get('[data-testid="preference-timezone"]').setValue('Invalid/Zone');
    await wrapper.get('[data-testid="preference-save"]').trigger('click');
    expect(wrapper.emitted('save')).toBeUndefined();
    expect(showWarning).toHaveBeenCalled();
    await wrapper.get('[data-testid="preference-timezone"]').setValue('Asia/Shanghai');
    await wrapper.get('[data-testid="preference-start"]').setValue('08:00');
    await wrapper.get('[data-testid="preference-save"]').trigger('click');
    expect(wrapper.emitted('save')).toBeUndefined();
  });

  it('损坏规则不被默认时间静默覆盖', async () => {
    const wrapper = editor({ quietHoursJson: '{"unexpected":true}' });
    expect(wrapper.find('[role="alert"]').exists()).toBe(true);
    expect(wrapper.get<HTMLButtonElement>('[data-testid="preference-save"]').element.disabled).toBe(true);
    expect(wrapper.emitted('save')).toBeUndefined();
  });

  it('保存中不允许二次提交', async () => {
    const wrapper = editor({ saving: true });
    await wrapper.get('[data-testid="preference-save"]').trigger('click');
    expect(wrapper.emitted('save')).toBeUndefined();
  });
});
