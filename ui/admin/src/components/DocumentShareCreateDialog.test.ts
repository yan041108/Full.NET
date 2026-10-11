import { describe, expect, it } from 'vitest';
import { flushPromises, mount } from '@vue/test-utils';
import { createShareSession } from '../test/document-share-fixtures';
import DocumentShareCreateDialog from './DocumentShareCreateDialog.vue';

describe('DocumentShareCreateDialog', () => {
  it('shows confirm button when open', async () => {
    const { pinia } = createShareSession(['document.host_shares.read', 'document.host_shares.create']);
    const wrapper = mount(DocumentShareCreateDialog, {
      props: {
        open: true,
        presetDocument: {
          id: '0198f36e-f7a7-7c52-9cbb-774e67411205',
          title: 'Demo',
          documentNo: 'DOC-1'
        }
      },
      global: { plugins: [pinia] },
      attachTo: document.body
    });
    await flushPromises();
    expect(document.body.querySelector('[data-testid="document-share-editor-submit"]')).not.toBeNull();
    wrapper.unmount();
  });
});
