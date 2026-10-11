import { DOMWrapper, flushPromises, mount } from '@vue/test-utils';
import { expect, it } from 'vitest';
import ArtFormDialog from './ArtFormDialog.vue';

it('自定义标题也必须提供真实对话框的可访问名称，并随标题更新', async () => {
  const wrapper = mount(ArtFormDialog, { props: { open: true, title: '租户版本授权：申请' } });
  try {
    await flushPromises();
    const dialog = new DOMWrapper(document.body).get('[role="dialog"]');
    expect(dialog.attributes('aria-label')).toBe('租户版本授权：申请');
    await wrapper.setProps({ title: '租户版本授权：报表' });
    expect(dialog.attributes('aria-label')).toBe('租户版本授权：报表');
  } finally {
    wrapper.unmount();
  }
});
