import { describe, expect, it, beforeEach } from 'vitest';
import { applyShellChrome } from '../js/core/shell-chrome.js';
import { createDefaultShellSettings } from '../js/core/shell-art-settings.js';

describe('shell-chrome', () => {
  beforeEach(() => {
    document.body.innerHTML = `
      <div id="root">
        <div data-session-shell>
          <label data-shell-chrome="language">语言</label>
          <button data-shell-chrome="search">搜索</button>
        </div>
      </div>
    `;
  });

  it('根据 showLanguage 切换顶栏语言控件', () => {
    const root = document.getElementById('root');
    applyShellChrome(root, { ...createDefaultShellSettings(), showLanguage: false });
    expect(root.querySelector('[data-shell-chrome="language"]').hidden).toBe(true);

    applyShellChrome(root, { ...createDefaultShellSettings(), showLanguage: true });
    expect(root.querySelector('[data-shell-chrome="language"]').hidden).toBe(false);
  });
});
