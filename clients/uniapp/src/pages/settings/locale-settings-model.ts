import { HttpProblem, toProblemPresentation } from '../../api/problem-details';
import { isCanonicalLocale, type CanonicalLocale } from '../../i18n/locale-adapter';
import type { LocaleSnapshot } from '../../i18n/locale-controller';

/** 设置页可安全展示的失败信息。 */
export interface LocaleSettingsErrorFeedback {
  readonly message: string;
  readonly traceId?: string;
}

/** 设置页渲染所需的完整不可变状态。 */
export interface LocaleSettingsState {
  readonly snapshot: LocaleSnapshot;
  readonly selectedLocale: CanonicalLocale;
  readonly feedback?: 'success' | 'error';
  readonly errorFeedback?: LocaleSettingsErrorFeedback;
  readonly hasPendingChange: boolean;
  readonly isBusy: boolean;
  readonly isSubmitDisabled: boolean;
}

/** 设置页模型依赖的语言控制器与翻译边界。 */
export interface LocaleSettingsModelDependencies {
  readonly initialize: () => LocaleSnapshot;
  readonly subscribe: (listener: (snapshot: LocaleSnapshot) => void) => () => void;
  readonly setActiveLocale: (locale: CanonicalLocale) => Promise<LocaleSnapshot>;
  readonly translate: (
    key: string,
    arguments_?: Readonly<Record<string, unknown>>
  ) => string;
  readonly hasTranslation: (key: string) => boolean;
}

/** 设置页的纯状态与提交行为，不承担认证发现或网络客户端创建。 */
export interface LocaleSettingsModel {
  readonly state: LocaleSettingsState;
  readonly hasPendingChange: boolean;
  readonly isBusy: boolean;
  readonly isSubmitDisabled: boolean;
  selectLocale(locale: unknown): void;
  saveSelection(): Promise<void>;
  subscribe(listener: (state: LocaleSettingsState) => void): () => void;
  dispose(): void;
}

interface InternalState {
  readonly snapshot: LocaleSnapshot;
  readonly selectedLocale: CanonicalLocale;
  readonly feedback?: 'success' | 'error';
  readonly errorFeedback?: LocaleSettingsErrorFeedback;
}

/** 创建可独立测试的语言设置页状态模型。 */
export function createLocaleSettingsModel(
  dependencies: LocaleSettingsModelDependencies
): LocaleSettingsModel {
  const listeners = new Set<(state: LocaleSettingsState) => void>();
  let disposed = false;
  const initialSnapshot = dependencies.initialize();
  let state: InternalState = {
    snapshot: initialSnapshot,
    selectedLocale: initialSnapshot.preferredLocale
  };

  const toPublicState = (): LocaleSettingsState => {
    const snapshot = { ...state.snapshot };
    const hasPendingChange = state.selectedLocale !== snapshot.preferredLocale;
    return {
      snapshot,
      selectedLocale: state.selectedLocale,
      feedback: state.feedback,
      errorFeedback: state.errorFeedback ? { ...state.errorFeedback } : undefined,
      hasPendingChange,
      isBusy: snapshot.saving,
      isSubmitDisabled: !hasPendingChange || snapshot.saving
    };
  };

  const notify = (): void => {
    const current = toPublicState();
    for (const listener of listeners) {
      listener(current);
    }
  };

  const stopLocaleSubscription = dependencies.subscribe(snapshot => {
    if (disposed) {
      return;
    }

    state = {
      ...state,
      snapshot: { ...snapshot },
      selectedLocale: snapshot.saving ? state.selectedLocale : snapshot.preferredLocale
    };
    notify();
  });

  const model: LocaleSettingsModel = {
    get state() {
      return toPublicState();
    },
    get hasPendingChange() {
      return toPublicState().hasPendingChange;
    },
    get isBusy() {
      return toPublicState().isBusy;
    },
    get isSubmitDisabled() {
      return toPublicState().isSubmitDisabled;
    },
    selectLocale(locale) {
      if (disposed || state.snapshot.saving || !isCanonicalLocale(locale)) {
        return;
      }

      state = {
        ...state,
        selectedLocale: locale,
        feedback: undefined,
        errorFeedback: undefined
      };
      notify();
    },
    async saveSelection() {
      if (disposed || toPublicState().isSubmitDisabled) {
        return;
      }

      state = { ...state, feedback: undefined, errorFeedback: undefined };
      notify();
      try {
        await dependencies.setActiveLocale(state.selectedLocale);
        if (!disposed) {
          state = { ...state, feedback: 'success' };
          notify();
        }
      } catch (error) {
        if (!disposed) {
          state = {
            ...state,
            feedback: 'error',
            errorFeedback: presentError(error, dependencies)
          };
          notify();
        }
      }
    },
    subscribe(listener) {
      if (disposed) {
        return () => undefined;
      }

      listeners.add(listener);
      return () => listeners.delete(listener);
    },
    dispose() {
      if (disposed) {
        return;
      }

      disposed = true;
      stopLocaleSubscription();
      listeners.clear();
    }
  };

  return model;
}

function presentError(
  error: unknown,
  dependencies: LocaleSettingsModelDependencies
): LocaleSettingsErrorFeedback {
  if (!(error instanceof HttpProblem)) {
    return { message: dependencies.translate('settings.save.failure') };
  }

  const presentation = toProblemPresentation(error, (code, arguments_) => {
    const key = `errors.${code}`;
    return dependencies.hasTranslation(key)
      ? dependencies.translate(key, arguments_)
      : undefined;
  });
  return {
    message: presentation.fieldMessages.locale?.[0]
      ?? presentation.message
      ?? dependencies.translate('settings.save.failure'),
    traceId: presentation.traceId
  };
}
