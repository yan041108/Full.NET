import { describe, expect, it, vi } from 'vitest';

import { HttpProblem } from '../src/api/problem-details';
import { createLocaleSettingsModel } from '../src/pages/settings/locale-settings-model';
import type { CanonicalLocale } from '../src/i18n/locale-adapter';
import type { LocaleSnapshot } from '../src/i18n/locale-controller';

const anonymousSnapshot: LocaleSnapshot = {
  preferredLocale: 'zh-CN',
  profileVersion: 0,
  authenticated: false,
  saving: false
};

const authenticatedSnapshot: LocaleSnapshot = {
  preferredLocale: 'zh-CN',
  profileVersion: 5,
  authenticated: true,
  saving: false
};

function createHarness(initialSnapshot: LocaleSnapshot) {
  let localeListener: (snapshot: LocaleSnapshot) => void = () => undefined;
  const unsubscribeLocale = vi.fn();
  const initialize = vi.fn(() => ({ ...initialSnapshot }));
  const setActiveLocale = vi.fn<(locale: CanonicalLocale) => Promise<LocaleSnapshot>>();
  const messages: Readonly<Record<string, string>> = {
    'settings.save.failure': 'Safe generic failure.',
    'errors.identity.profile_version_conflict': 'Profile changed locally.'
  };
  const model = createLocaleSettingsModel({
    initialize,
    subscribe: listener => {
      localeListener = listener;
      return unsubscribeLocale;
    },
    setActiveLocale,
    translate: key => messages[key] ?? key,
    hasTranslation: key => Object.hasOwn(messages, key)
  });

  return {
    model,
    initialize,
    setActiveLocale,
    unsubscribeLocale,
    publish: (snapshot: LocaleSnapshot) => localeListener({ ...snapshot })
  };
}

describe('locale settings model', () => {
  it('commits an anonymous selection and exposes a successful synchronized state', async () => {
    const harness = createHarness(anonymousSnapshot);
    const committed: LocaleSnapshot = { ...anonymousSnapshot, preferredLocale: 'en-US' };
    harness.setActiveLocale.mockImplementation(async locale => {
      expect(locale).toBe('en-US');
      harness.publish(committed);
      return committed;
    });

    harness.model.selectLocale('en-US');
    expect(harness.initialize).toHaveBeenCalledTimes(1);
    expect(harness.model.hasPendingChange).toBe(true);
    expect(harness.model.isSubmitDisabled).toBe(false);

    await harness.model.saveSelection();

    expect(harness.model.state).toMatchObject({
      snapshot: committed,
      selectedLocale: 'en-US',
      feedback: 'success'
    });
    expect(harness.model.hasPendingChange).toBe(false);
    expect(harness.model.isSubmitDisabled).toBe(true);
  });

  it('exposes busy and disabled state while an authenticated save is pending', async () => {
    const harness = createHarness(authenticatedSnapshot);
    const saving = { ...authenticatedSnapshot, saving: true };
    const committed: LocaleSnapshot = {
      ...authenticatedSnapshot,
      preferredLocale: 'en-US',
      profileVersion: 6
    };
    let resolveSave: ((snapshot: LocaleSnapshot) => void) | undefined;
    harness.setActiveLocale.mockImplementation(() => {
      harness.publish(saving);
      return new Promise(resolve => {
        resolveSave = resolve;
      });
    });
    harness.model.selectLocale('en-US');

    const save = harness.model.saveSelection();

    expect(harness.model.isBusy).toBe(true);
    expect(harness.model.isSubmitDisabled).toBe(true);
    expect(harness.model.state.selectedLocale).toBe('en-US');
    harness.publish(committed);
    resolveSave?.(committed);
    await save;
    expect(harness.model.state.feedback).toBe('success');
  });

  it('coalesces concurrent save attempts before the controller publishes its busy snapshot', async () => {
    const harness = createHarness(authenticatedSnapshot);
    const committed: LocaleSnapshot = {
      ...authenticatedSnapshot,
      preferredLocale: 'en-US',
      profileVersion: 6
    };
    let resolveSave: ((snapshot: LocaleSnapshot) => void) | undefined;
    harness.setActiveLocale.mockImplementation(() =>
      new Promise(resolve => {
        resolveSave = resolve;
      })
    );
    harness.model.selectLocale('en-US');

    const firstSave = harness.model.saveSelection();
    const concurrentSave = harness.model.saveSelection();

    expect(harness.setActiveLocale).toHaveBeenCalledTimes(1);
    expect(harness.model.isBusy).toBe(true);
    expect(harness.model.isSubmitDisabled).toBe(true);

    harness.publish(committed);
    resolveSave?.(committed);
    await Promise.all([firstSave, concurrentSave]);
    expect(harness.model.state.feedback).toBe('success');
  });

  it('rolls selected and current locale back to the committed snapshot after a save failure', async () => {
    const harness = createHarness(authenticatedSnapshot);
    harness.setActiveLocale.mockImplementation(async () => {
      harness.publish({ ...authenticatedSnapshot, saving: true });
      harness.publish(authenticatedSnapshot);
      throw new Error('network detail must not be displayed');
    });
    harness.model.selectLocale('en-US');

    await harness.model.saveSelection();

    expect(harness.model.state).toMatchObject({
      snapshot: authenticatedSnapshot,
      selectedLocale: 'zh-CN',
      feedback: 'error',
      errorFeedback: { message: 'Safe generic failure.' }
    });
    expect(harness.model.isBusy).toBe(false);
    expect(harness.model.hasPendingChange).toBe(false);
  });

  it('localizes a stable ProblemDetails code and preserves its trace id', async () => {
    const harness = createHarness(authenticatedSnapshot);
    harness.setActiveLocale.mockImplementation(async () => {
      harness.publish(authenticatedSnapshot);
      throw new HttpProblem({
        status: 409,
        code: 'identity.profile_version_conflict',
        title: 'Profile changed.',
        traceId: 'trace-stable-code'
      });
    });
    harness.model.selectLocale('en-US');

    await harness.model.saveSelection();

    expect(harness.model.state.errorFeedback).toEqual({
      message: 'Profile changed locally.',
      traceId: 'trace-stable-code'
    });
  });

  it('uses the guarded server title and trace id for an unknown ProblemDetails code', async () => {
    const harness = createHarness(authenticatedSnapshot);
    harness.setActiveLocale.mockImplementation(async () => {
      harness.publish(authenticatedSnapshot);
      throw new HttpProblem({
        status: 503,
        code: 'service.future_code',
        title: 'Request could not be completed.',
        traceId: 'trace-unknown-code'
      });
    });
    harness.model.selectLocale('en-US');

    await harness.model.saveSelection();

    expect(harness.model.state.errorFeedback).toEqual({
      message: 'Request could not be completed.',
      traceId: 'trace-unknown-code'
    });
  });

  it('disposes the controller subscription once and ignores later snapshots', () => {
    const harness = createHarness(anonymousSnapshot);
    const beforeDispose = harness.model.state;

    harness.model.dispose();
    harness.model.dispose();
    harness.publish({ ...anonymousSnapshot, preferredLocale: 'en-US' });

    expect(harness.unsubscribeLocale).toHaveBeenCalledTimes(1);
    expect(harness.model.state).toEqual(beforeDispose);
  });
});
