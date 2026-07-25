import { describe, expect, it } from 'vitest';
import { createShellChatDrawer } from '../js/core/shell-chat-drawer.js';

describe('shell-chat-drawer', () => {
  it('打开聊天抽屉并发送消息', () => {
    document.body.innerHTML = `
      <div id="root">
        <button data-shell-chat-open></button>
        <div data-shell-chat hidden>
          <button data-shell-chat-backdrop></button>
          <aside data-shell-chat-panel>
            <strong data-shell-chat-title></strong>
            <small data-shell-chat-status></small>
            <button data-shell-chat-close></button>
            <div data-shell-chat-messages></div>
            <textarea data-shell-chat-input></textarea>
            <button data-shell-chat-send></button>
          </aside>
        </div>
      </div>
    `;
    const root = document.getElementById('root');
    const drawer = createShellChatDrawer(root);
    const t = key => {
      const labels = {
        'shell.chatTitle': 'Art Bot',
        'shell.chatOnline': '在线',
        'shell.chatInputPlaceholder': '输入消息',
        'shell.chatSend': '发送',
        'shell.chatClose': '关闭聊天',
        'shell.chat': '消息'
      };
      return labels[key] ?? key;
    };
    drawer.render(t);
    root.querySelector('[data-shell-chat-open]').click();
    expect(root.querySelector('[data-shell-chat]').hidden).toBe(false);
    expect(root.querySelectorAll('.fn-chat-drawer__message').length).toBe(5);
    const input = root.querySelector('[data-shell-chat-input]');
    input.value = '测试消息';
    root.querySelector('[data-shell-chat-send]').click();
    expect(root.querySelectorAll('.fn-chat-drawer__message').length).toBe(6);
    expect(root.querySelector('.fn-chat-drawer__message.is-me:last-child .fn-chat-drawer__bubble').textContent).toBe('测试消息');
  });
});
