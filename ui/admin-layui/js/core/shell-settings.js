import {
  ART_SHELL_MAIN_COLORS,
  exportShellSettingsJson,
  resetShellSettings
} from './shell-art-settings.js';

const layoutOptions = [
  { value: 'left', labelKey: 'shell.settingsMenuLayoutLeft' },
  { value: 'top', labelKey: 'shell.settingsMenuLayoutTop' },
  { value: 'top-left', labelKey: 'shell.settingsMenuLayoutTopLeft' },
  { value: 'dual-menu', labelKey: 'shell.settingsMenuLayoutDual' }
];

const themeOptions = [
  { value: 'light', labelKey: 'shell.settingsThemeLightCard' },
  { value: 'dark', labelKey: 'shell.settingsThemeDarkCard' }
];

const menuStyleOptions = [
  { value: 'design', labelKey: 'shell.settingsMenuStyleDesign' },
  { value: 'light', labelKey: 'shell.settingsThemeLightCard' },
  { value: 'dark', labelKey: 'shell.settingsThemeDarkCard' }
];

const tabStyleOptions = [
  { value: 'default', labelKey: 'shell.settingsTabDefault' },
  { value: 'card', labelKey: 'shell.settingsTabCard' },
  { value: 'google', labelKey: 'shell.settingsTabGoogle' }
];

const radiusOptions = ['0', '0.25', '0.5', '0.75', '1'];

const basicToggles = [
  { key: 'showPageTabs', labelKey: 'shell.settingsShowPageTabs' },
  { key: 'dualMenuShowText', labelKey: 'shell.settingsDualMenuShowText', layoutOnly: 'dual-menu' },
  { key: 'uniqueOpened', labelKey: 'shell.settingsUniqueOpened' },
  { key: 'showMenuButton', labelKey: 'shell.settingsShowMenuButton' },
  { key: 'showRefreshButton', labelKey: 'shell.settingsShowRefreshButton' },
  { key: 'showBreadcrumb', labelKey: 'shell.settingsShowBreadcrumb' },
  { key: 'showLanguage', labelKey: 'shell.settingsShowLanguage' },
  { key: 'showFullscreen', labelKey: 'shell.settingsShowFullscreen' }
];

function createSectionTitle(ownerDocument, text) {
  const title = ownerDocument.createElement('p');
  title.className = 'fn-settings-drawer__label';
  title.textContent = text;
  return title;
}

function createOptionGrid(ownerDocument) {
  const grid = ownerDocument.createElement('div');
  grid.className = 'fn-settings-layout';
  return grid;
}

function createOptionButton(ownerDocument, label, isActive, onClick) {
  const button = ownerDocument.createElement('button');
  button.type = 'button';
  button.className = 'fn-settings-layout__item';
  button.classList.toggle('is-active', isActive);
  button.textContent = label;
  button.addEventListener('click', onClick);
  return button;
}

function createSegment(ownerDocument, options, activeValue, onSelect) {
  const segment = ownerDocument.createElement('div');
  segment.className = 'fn-settings-segment';
  options.forEach(option => {
    const button = ownerDocument.createElement('button');
    button.type = 'button';
    button.className = 'fn-settings-segment__option';
    button.classList.toggle('is-active', option.value === activeValue);
    button.textContent = option.label;
    button.addEventListener('click', () => onSelect(option.value));
    segment.append(button);
  });
  return segment;
}

function createToggleRow(ownerDocument, label, checked, onChange) {
  const row = ownerDocument.createElement('label');
  row.className = 'fn-settings-drawer__toggle';
  const text = ownerDocument.createElement('span');
  text.textContent = label;
  const input = ownerDocument.createElement('input');
  input.type = 'checkbox';
  input.checked = checked;
  input.addEventListener('change', () => onChange(input.checked));
  row.append(text, input);
  return row;
}

/** 绑定 Layui 壳层设置抽屉；与 Vue ArtSettingsPanel 字段对齐。 */
export function bindShellSettings(root, options) {
  const drawer = root.querySelector('[data-shell-settings]');
  const openButton = root.querySelector('[data-shell-settings-open]');
  const closeButton = root.querySelector('[data-shell-settings-close]');
  const backdrop = root.querySelector('[data-shell-settings-backdrop]');
  const body = root.querySelector('[data-shell-settings-body]');
  const statusNode = root.querySelector('[data-shell-settings-status]');

  if (!drawer || !body) {
    return {
      render() {},
      dispose() {}
    };
  }

  let translate = key => key;

  function setStatus(message) {
    if (!statusNode) {
      return;
    }

    statusNode.textContent = message ?? '';
    statusNode.hidden = !message;
  }

  function patch(partial) {
    options.updateSettings(partial);
    render(translate);
  }

  function render(t) {
    if (t) {
      translate = t;
    }

    const settings = options.getSettings();
    const ownerDocument = body.ownerDocument;
    body.replaceChildren();

    body.append(createSectionTitle(ownerDocument, translate('shell.settingsThemeSection')));
    const themeGrid = createOptionGrid(ownerDocument);
    themeOptions.forEach(option => {
      themeGrid.append(createOptionButton(
        ownerDocument,
        translate(option.labelKey),
        settings.themeMode === option.value,
        () => patch({ themeMode: option.value })
      ));
    });
    body.append(themeGrid);

    body.append(createSectionTitle(ownerDocument, translate('shell.settingsMenuLayoutTitle')));
    const layoutGrid = createOptionGrid(ownerDocument);
    layoutOptions.forEach(option => {
      layoutGrid.append(createOptionButton(
        ownerDocument,
        translate(option.labelKey),
        settings.menuLayout === option.value,
        () => patch({ menuLayout: option.value })
      ));
    });
    body.append(layoutGrid);

    body.append(createSectionTitle(ownerDocument, translate('shell.settingsMenuStyleTitle')));
    const menuStyleGrid = createOptionGrid(ownerDocument);
    menuStyleOptions.forEach(option => {
      menuStyleGrid.append(createOptionButton(
        ownerDocument,
        translate(option.labelKey),
        settings.menuStyle === option.value,
        () => patch({ menuStyle: option.value })
      ));
    });
    body.append(menuStyleGrid);

    body.append(createSectionTitle(ownerDocument, translate('shell.settingsColorTitle')));
    const colorGrid = ownerDocument.createElement('div');
    colorGrid.className = 'fn-settings-color-grid';
    ART_SHELL_MAIN_COLORS.forEach(color => {
      const button = ownerDocument.createElement('button');
      button.type = 'button';
      button.className = 'fn-settings-color-dot';
      button.style.background = color;
      button.setAttribute('aria-label', color);
      button.classList.toggle('is-active', settings.primaryColor === color);
      button.addEventListener('click', () => patch({ primaryColor: color }));
      colorGrid.append(button);
    });
    body.append(colorGrid);

    body.append(createSectionTitle(ownerDocument, translate('shell.settingsBoxTitle')));
    body.append(createSegment(
      ownerDocument,
      [
        { value: 'border', label: translate('shell.settingsBoxBorder') },
        { value: 'shadow', label: translate('shell.settingsBoxShadow') }
      ],
      settings.boxStyle,
      value => patch({ boxStyle: value })
    ));

    body.append(createSectionTitle(ownerDocument, translate('shell.settingsContainerTitle')));
    body.append(createSegment(
      ownerDocument,
      [
        { value: 'full', label: translate('shell.settingsContainerFull') },
        { value: 'boxed', label: translate('shell.settingsContainerBoxed') }
      ],
      settings.containerWidth,
      value => patch({ containerWidth: value })
    ));

    body.append(createSectionTitle(ownerDocument, translate('shell.settingsBasicsTitle')));
    basicToggles.forEach(item => {
      if (item.layoutOnly && settings.menuLayout !== item.layoutOnly) {
        return;
      }

      body.append(createToggleRow(
        ownerDocument,
        translate(item.labelKey),
        settings[item.key] === true,
        value => patch({ [item.key]: value })
      ));
    });

    const widthRow = ownerDocument.createElement('label');
    widthRow.className = 'fn-settings-drawer__field';
    const widthLabel = ownerDocument.createElement('span');
    widthLabel.textContent = translate('shell.settingsMenuOpenWidth');
    const widthInput = ownerDocument.createElement('input');
    widthInput.type = 'number';
    widthInput.min = '180';
    widthInput.max = '320';
    widthInput.step = '10';
    widthInput.value = String(settings.menuOpenWidth);
    widthInput.addEventListener('change', () => {
      const value = Number(widthInput.value);
      if (!Number.isFinite(value)) {
        return;
      }

      patch({ menuOpenWidth: Math.min(320, Math.max(180, value)) });
    });
    widthRow.append(widthLabel, widthInput);
    body.append(widthRow);

    const tabRow = ownerDocument.createElement('label');
    tabRow.className = 'fn-settings-drawer__field';
    const tabLabel = ownerDocument.createElement('span');
    tabLabel.textContent = translate('shell.settingsTabStyle');
    const tabSelect = ownerDocument.createElement('select');
    tabStyleOptions.forEach(option => {
      const node = ownerDocument.createElement('option');
      node.value = option.value;
      node.textContent = translate(option.labelKey);
      node.selected = settings.tabStyle === option.value;
      tabSelect.append(node);
    });
    tabSelect.addEventListener('change', () => patch({ tabStyle: tabSelect.value }));
    tabRow.append(tabLabel, tabSelect);
    body.append(tabRow);

    const radiusRow = ownerDocument.createElement('label');
    radiusRow.className = 'fn-settings-drawer__field';
    const radiusLabel = ownerDocument.createElement('span');
    radiusLabel.textContent = translate('shell.settingsCustomRadius');
    const radiusSelect = ownerDocument.createElement('select');
    radiusOptions.forEach(value => {
      const node = ownerDocument.createElement('option');
      node.value = value;
      node.textContent = value;
      node.selected = settings.customRadius === value;
      radiusSelect.append(node);
    });
    radiusSelect.addEventListener('change', () => patch({ customRadius: radiusSelect.value }));
    radiusRow.append(radiusLabel, radiusSelect);
    body.append(radiusRow);

    const actions = ownerDocument.createElement('div');
    actions.className = 'fn-settings-actions';
    const copyButton = ownerDocument.createElement('button');
    copyButton.type = 'button';
    copyButton.className = 'layui-btn';
    copyButton.textContent = translate('shell.settingsCopyConfig');
    copyButton.addEventListener('click', async () => {
      try {
        await navigator.clipboard.writeText(exportShellSettingsJson());
        setStatus(translate('shell.settingsCopySuccess'));
      } catch {
        setStatus(translate('shell.settingsCopyFailed'));
      }
    });
    const resetButton = ownerDocument.createElement('button');
    resetButton.type = 'button';
    resetButton.className = 'layui-btn layui-btn-primary';
    resetButton.textContent = translate('shell.settingsResetConfig');
    resetButton.addEventListener('click', () => {
      options.updateSettings(resetShellSettings());
      setStatus(translate('shell.settingsResetSuccess'));
      render(translate);
    });
    actions.append(copyButton, resetButton);
    body.append(actions);
  }

  function open() {
    drawer.hidden = false;
    backdrop.hidden = false;
    setStatus('');
    render(translate);
  }

  function close() {
    drawer.hidden = true;
    backdrop.hidden = true;
    setStatus('');
  }

  const onOpen = () => open();
  const onClose = () => close();

  openButton?.addEventListener('click', onOpen);
  closeButton?.addEventListener('click', onClose);
  backdrop?.addEventListener('click', onClose);

  return {
    render,
    dispose() {
      openButton?.removeEventListener('click', onOpen);
      closeButton?.removeEventListener('click', onClose);
      backdrop?.removeEventListener('click', onClose);
    }
  };
}
