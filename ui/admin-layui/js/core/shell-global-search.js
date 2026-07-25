import { buildFlatNavigationItems } from './shell-navigation-groups.js';
import { localNavigationFor } from './navigation.js';
import { filterShellNavigation } from './shell-navigation-search.js';

function buildSearchItems(navigation, t) {
  return buildFlatNavigationItems(navigation, t).map(item => {
    const local = localNavigationFor(item.componentKey);
    return {
      ...item,
      caption: local ? t(local.captionKey) : ''
    };
  });
}

/**
 * Layui 全局搜索弹层；支持 Ctrl/Cmd+K 与方向键导航。
 */
export function createShellGlobalSearch(root, options = {}) {
  const dialog = root.querySelector('[data-shell-search]');
  const backdrop = root.querySelector('[data-shell-search-backdrop]');
  const openButton = root.querySelector('[data-shell-search-open]');
  const input = root.querySelector('[data-shell-search-input]');
  const resultsNode = root.querySelector('[data-shell-search-results]');
  const emptyNode = root.querySelector('[data-shell-search-empty]');
  const titleNode = root.querySelector('[data-shell-search-title]');
  const hintNode = root.querySelector('[data-shell-search-hint]');
  let translate = key => key;
  let highlightedIndex = 0;
  let results = [];

  function navigateTo(path) {
    close();
    if (typeof options.onNavigate === 'function') {
      options.onNavigate(path);
      return;
    }

    window.location.hash = path;
  }

  function renderResults() {
    if (!resultsNode || !emptyNode || !input) {
      return;
    }

    const navigation = buildSearchItems(options.getNavigation?.() ?? [], translate);
    results = filterShellNavigation(navigation, input.value);
    highlightedIndex = Math.min(highlightedIndex, Math.max(0, results.length - 1));
    resultsNode.replaceChildren();

    if (results.length === 0) {
      emptyNode.hidden = false;
      emptyNode.textContent = translate('shell.searchEmpty');
      return;
    }

    emptyNode.hidden = true;
    const ownerDocument = resultsNode.ownerDocument;
    const fragment = ownerDocument.createDocumentFragment();
    results.forEach((item, index) => {
      const button = ownerDocument.createElement('button');
      button.type = 'button';
      button.className = 'fn-search-modal__item';
      button.classList.toggle('is-active', index === highlightedIndex);
      button.setAttribute('role', 'option');
      button.setAttribute('aria-selected', index === highlightedIndex ? 'true' : 'false');
      button.addEventListener('click', () => navigateTo(item.path));

      const icon = ownerDocument.createElement('i');
      icon.className = `layui-icon ${item.iconClass}`;
      icon.setAttribute('aria-hidden', 'true');
      const body = ownerDocument.createElement('span');
      const title = ownerDocument.createElement('strong');
      title.textContent = item.title;
      const caption = ownerDocument.createElement('small');
      caption.textContent = item.caption || item.path;
      body.append(title, caption);
      button.append(icon, body);
      fragment.append(button);
    });
    resultsNode.append(fragment);
  }

  function open() {
    if (!dialog) {
      return;
    }

    dialog.hidden = false;
    highlightedIndex = 0;
    if (input) {
      input.value = '';
    }
    renderResults();
    input?.focus();
  }

  function close() {
    if (!dialog) {
      return;
    }

    dialog.hidden = true;
    highlightedIndex = 0;
    if (input) {
      input.value = '';
    }
  }

  function onDocumentKeydown(event) {
    if ((event.metaKey || event.ctrlKey) && event.key.toLowerCase() === 'k') {
      event.preventDefault();
      if (dialog?.hidden) {
        open();
      } else {
        close();
      }
      return;
    }

    if (dialog?.hidden || results.length === 0) {
      return;
    }

    if (event.key === 'ArrowDown') {
      event.preventDefault();
      highlightedIndex = (highlightedIndex + 1) % results.length;
      renderResults();
    } else if (event.key === 'ArrowUp') {
      event.preventDefault();
      highlightedIndex = (highlightedIndex - 1 + results.length) % results.length;
      renderResults();
    } else if (event.key === 'Enter') {
      event.preventDefault();
      const target = results[highlightedIndex];
      if (target) {
        navigateTo(target.path);
      }
    } else if (event.key === 'Escape') {
      event.preventDefault();
      close();
    }
  }

  const onOpenClick = () => open();
  const onBackdropClick = () => close();
  const onInput = () => {
    highlightedIndex = 0;
    renderResults();
  };

  openButton?.addEventListener('click', onOpenClick);
  backdrop?.addEventListener('click', onBackdropClick);
  input?.addEventListener('input', onInput);
  document.addEventListener('keydown', onDocumentKeydown);

  return {
    open,
    close,
    render(t) {
      if (t) {
        translate = t;
      }

      if (titleNode) {
        titleNode.textContent = translate('shell.searchTitle');
      }
      if (hintNode) {
        hintNode.textContent = translate('shell.searchHint');
      }
      if (input) {
        input.placeholder = translate('shell.searchPlaceholder');
        input.setAttribute('aria-label', translate('shell.searchPlaceholder'));
      }
      if (!dialog?.hidden) {
        renderResults();
      }
    },
    dispose() {
      openButton?.removeEventListener('click', onOpenClick);
      backdrop?.removeEventListener('click', onBackdropClick);
      input?.removeEventListener('input', onInput);
      document.removeEventListener('keydown', onDocumentKeydown);
    }
  };
}
