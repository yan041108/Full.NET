import { describe, expect, it } from 'vitest';
import { createHostFilesController } from '../js/core/host-files.js';

describe('Layui Host 文件控制器', () => {
  it('上传后渲染目录并支持删除', async () => {
    document.body.innerHTML = `
      <div data-host-files-problem hidden><strong></strong><span></span></div>
      <form data-host-files-upload-form>
        <input type="file" />
        <button type="submit">上传</button>
      </form>
      <div data-host-files-directory></div>`;

    const requests = [];
    const controller = createHostFilesController(document.body, {
      request: async (url, init) => {
        requests.push({ url, init });
        if (url.includes('/delete')) {
          return { id: '01912345-6789-7abc-8def-0123456789ab' };
        }
        return {
          items: [{
            id: '01912345-6789-7abc-8def-0123456789ab',
            originalFileName: 'parity.txt',
            contentType: 'text/plain',
            sizeBytes: 12,
            contentHash: null,
            createdAtUtc: '2026-07-26T00:00:00Z',
            createdByUserId: '01912345-6789-7abc-8def-0123456789ac'
          }],
          page: 1,
          pageSize: 20,
          total: 1
        };
      },
      translation: () => ({
        t: (key, params) => {
          if (key === 'hostFiles.confirmDelete') return `删除 ${params?.name ?? ''}`;
          if (key === 'hostFiles.deleteSuccess') return '已删除';
          if (key === 'hostFiles.uploadSuccess') return '已上传';
          if (key === 'hostFiles.sizeBytes') return '大小';
          if (key === 'hostFiles.createdAt') return '创建时间';
          return key;
        }
      })
    });

    globalThis.layui = {
      layer: {
        confirm: (_message, _options, callback) => callback(1),
        close: () => undefined,
        msg: () => undefined
      }
    };

    const fileInput = document.querySelector('input[type="file"]');
    const file = new File(['hello'], 'parity.txt', { type: 'text/plain' });
    Object.defineProperty(fileInput, 'files', {
      configurable: true,
      value: [file]
    });
    document.querySelector('form')?.dispatchEvent(new Event('submit', { cancelable: true }));
    await new Promise(resolve => setTimeout(resolve, 0));
    await controller.load();

    expect(document.querySelector('[data-host-files-directory] code')?.textContent)
      .toBe('text/plain');

    document.querySelector('[data-host-files-delete]')?.dispatchEvent(new Event('click', { bubbles: true }));
    await new Promise(resolve => setTimeout(resolve, 0));

    expect(requests.some(entry => entry.url.includes('/delete'))).toBe(true);
    controller.dispose();
    delete globalThis.layui;
  });
});
