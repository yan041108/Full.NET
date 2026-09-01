import { shallowRef, type ShallowRef } from 'vue';
import dayjs from 'dayjs';
import 'dayjs/locale/en';
import 'dayjs/locale/zh-cn';
import type { Language } from 'element-plus/es/locale';
import type { SupportedLocale } from '@fullnet/admin-i18n';

type LocaleModule = Readonly<{ default: Language }>;
type LocaleLoaders = Readonly<Record<SupportedLocale, () => Promise<LocaleModule>>>;

/** Element Plus 语言包加载器目录，与 SupportedLocale 一一对应。 */
export const elementLocaleLoaders: LocaleLoaders = {
  'zh-CN': () => import('element-plus/es/locale/lang/zh-cn'),
  'en-US': () => import('element-plus/es/locale/lang/en')
};

/** Element Plus 语言控制器依赖项，便于测试替换异步加载与 Day.js 侧效。 */
export interface ElementLocaleControllerOptions {
  loaders?: LocaleLoaders;
  setDayjsLocale?: (locale: 'zh-cn' | 'en') => void;
  onFallback?: (locale: 'zh-CN') => void;
}

/** Element Plus 组件语言状态控制器。 */
export interface ElementLocaleController {
  locale: ShallowRef<Language | undefined>;
  setLocale: (locale: SupportedLocale) => Promise<void>;
}

/**
 * 协调异步组件语言加载；请求序号阻止较慢的旧 chunk 覆盖用户最新选择。
 */
export function createElementLocaleController(
  options: ElementLocaleControllerOptions = {}
): ElementLocaleController {
  const locale = shallowRef<Language>();
  const loaders = options.loaders ?? elementLocaleLoaders;
  const setDayjsLocale = options.setDayjsLocale ?? (value => dayjs.locale(value));
  let generation = 0;

  /** 切换组件库语言，并确保较早发起的异步加载不会覆盖最后一次选择。 */
  async function setLocale(value: SupportedLocale): Promise<void> {
    const operation = ++generation;
    try {
      const loaded = await loaders[value]();
      if (operation !== generation) {
        return;
      }

      locale.value = loaded.default;
      setDayjsLocale(value === 'zh-CN' ? 'zh-cn' : 'en');
    } catch {
      if (operation !== generation) {
        return;
      }

      let fallback: LocaleModule | undefined;
      try {
        fallback = await loaders['zh-CN']();
      } catch {
        // 中文 chunk 也不可用时保留已加载的组件语言，避免异步异常中断整个管理端。
      }
      if (operation !== generation) {
        return;
      }

      if (fallback) {
        locale.value = fallback.default;
      }
      setDayjsLocale('zh-cn');
      options.onFallback?.('zh-CN');
    }
  }

  return { locale, setLocale };
}
