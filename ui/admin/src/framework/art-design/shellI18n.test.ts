import { describe, expect, it } from 'vitest';
import { messages, type MessageKey } from '@fullnet/admin-i18n';

const shellLabelKeys = [
  'shell.brandAria',
  'shell.brandCaption',
  'shell.currentTenant',
  'shell.mainNavigation',
  'shell.managementDomain',
  'shell.production',
  'shell.apiHealthy',
  'shell.searchPlaceholder',
  'shell.searchTitle',
  'shell.searchEmpty',
  'shell.searchHint',
  'shell.settingsTitle',
  'shell.settingsThemeSection',
  'shell.settingsThemeLightCard',
  'shell.settingsThemeDarkCard',
  'shell.settingsMenuStyleDesign',
  'shell.settingsClose',
  'shell.settingsMenuLayoutTitle',
  'shell.settingsMenuLayoutLeft',
  'shell.settingsMenuLayoutTop',
  'shell.settingsMenuLayoutTopLeft',
  'shell.settingsMenuLayoutDual',
  'shell.settingsMenuStyleTitle',
  'shell.settingsColorTitle',
  'shell.settingsBoxTitle',
  'shell.settingsBoxBorder',
  'shell.settingsBoxShadow',
  'shell.settingsContainerTitle',
  'shell.settingsContainerFull',
  'shell.settingsContainerBoxed',
  'shell.settingsBasicsTitle',
  'shell.settingsShowPageTabs',
  'shell.settingsUniqueOpened',
  'shell.settingsShowMenuButton',
  'shell.settingsShowRefreshButton',
  'shell.settingsShowBreadcrumb',
  'shell.settingsShowLanguage',
  'shell.settingsShowFullscreen',
  'shell.settingsMenuOpenWidth',
  'shell.settingsTabStyle',
  'shell.settingsTabDefault',
  'shell.settingsTabCard',
  'shell.settingsTabGoogle',
  'shell.settingsCustomRadius',
  'shell.settingsCopyConfig',
  'shell.settingsResetConfig',
  'shell.settingsCopySuccess',
  'shell.settingsCopyFailed',
  'shell.settingsResetSuccess',
  'shell.tenantSelector',
  'shell.notifications',
  'shell.chat',
  'shell.language',
  'shell.noticeTitle',
  'shell.noticeMarkRead',
  'shell.noticeViewAll',
  'shell.noticeEmpty',
  'shell.noticeTabNotice',
  'shell.noticeTabMessage',
  'shell.noticeTabPending',
  'shell.chatTitle',
  'shell.chatOnline',
  'shell.chatOffline',
  'shell.chatInputPlaceholder',
  'shell.chatSend',
  'shell.chatClose',
  'shell.logout',
  'shell.controlPlane',
  'shell.themeLight',
  'shell.themeDark',
  'shell.mobileMenu',
  'shell.pageTabs',
  'shell.closeTab',
  'shell.systemName',
  'shell.refresh',
  'shell.fullscreenEnter',
  'shell.fullscreenExit',
  'shell.collapseMenu',
  'shell.expandMenu'
] as const satisfies readonly MessageKey[];

describe('Art 壳层国际化资源', () => {
  it.each(['zh-CN', 'en-US'] as const)(
    '%s 包含壳层所需全部消息键且不为空',
    locale => {
      for (const key of shellLabelKeys) {
        const value = messages[locale][key];
        expect(value, key).toBeTruthy();
        expect(value.trim(), key).not.toHaveLength(0);
      }
    }
  );

  it('关闭标签页文案支持标题参数', () => {
    expect(messages['zh-CN']['shell.closeTab'].includes('{title}')).toBe(true);
    expect(messages['en-US']['shell.closeTab'].includes('{title}')).toBe(true);
  });
});
