<script setup lang="ts">
import { computed, onBeforeUnmount, ref } from 'vue';
import { onShow } from '@dcloudio/uni-app';
import { useI18n } from 'vue-i18n';

import {
  initializeLocale,
  localeController,
  setActiveLocale,
  synchronizeNavigationTitle
} from '../../i18n';
import type { CanonicalLocale } from '../../i18n/locale-adapter';
import { createLocaleSettingsModel } from './locale-settings-model';

interface LocaleOption {
  readonly value: CanonicalLocale;
  readonly code: string;
  readonly labelKey: string;
  readonly detailKey: string;
}

const localeOptions: readonly LocaleOption[] = [
  {
    value: 'zh-CN',
    code: 'ZH-CN',
    labelKey: 'settings.locale.options.zh-CN.label',
    detailKey: 'settings.locale.options.zh-CN.detail'
  },
  {
    value: 'en-US',
    code: 'EN-US',
    labelKey: 'settings.locale.options.en-US.label',
    detailKey: 'settings.locale.options.en-US.detail'
  }
];

const { t, te } = useI18n();
const model = createLocaleSettingsModel({
  initialize: initializeLocale,
  subscribe: listener => localeController.subscribe(listener),
  setActiveLocale,
  translate: (key, arguments_) => t(key, arguments_ ?? {}),
  hasTranslation: key => te(key)
});
const state = ref(model.state);
const stopModelSubscription = model.subscribe(nextState => {
  state.value = nextState;
});
const statusText = computed(() => state.value.hasPendingChange
  ? t('settings.locale.pending')
  : t('settings.locale.unchanged')
);
const actionText = computed(() => state.value.snapshot.authenticated
  ? t('settings.locale.actions.save')
  : t('settings.locale.actions.apply')
);

onBeforeUnmount(() => {
  stopModelSubscription();
  model.dispose();
});
onShow(synchronizeNavigationTitle);

function selectLocale(event: { readonly detail: { readonly value: string } }): void {
  model.selectLocale(event.detail.value);
}

function localeName(locale: CanonicalLocale): string {
  return t(`settings.locale.options.${locale}.label`);
}

async function saveSelection(): Promise<void> {
  await model.saveSelection();
}
</script>

<template>
  <view class="locale-page">
    <view class="ambient ambient--top" aria-hidden="true" />
    <view class="ambient ambient--bottom" aria-hidden="true" />

    <main class="control-panel">
      <header class="hero">
        <view class="brand-mark" aria-hidden="true">
          <text>F</text>
        </view>
        <view class="hero-copy">
          <text class="eyebrow">{{ t('settings.locale.eyebrow') }}</text>
          <text class="title">{{ t('settings.locale.heading') }}</text>
          <text class="description">{{ t('settings.locale.description') }}</text>
        </view>
      </header>

      <section class="status-strip" aria-live="polite">
        <view class="status-item">
          <text class="status-label">{{ t('settings.locale.current') }}</text>
          <text class="status-value">{{ localeName(state.snapshot.preferredLocale) }}</text>
        </view>
        <view class="status-rule" aria-hidden="true" />
        <view class="status-item status-item--end">
          <text class="status-label">{{ statusText }}</text>
          <text class="status-value status-value--accent">
            {{ localeName(state.selectedLocale) }}
          </text>
        </view>
      </section>

      <section class="mode-card">
        <view class="mode-indicator" aria-hidden="true" />
        <view class="mode-copy">
          <text class="mode-title">
            {{ state.snapshot.authenticated
              ? t('settings.locale.authenticated')
              : t('settings.locale.anonymous') }}
          </text>
          <text v-if="state.snapshot.authenticated" class="mode-version">
            {{ t('settings.locale.profileVersion', { version: state.snapshot.profileVersion }) }}
          </text>
        </view>
      </section>

      <form class="locale-form" @submit.prevent="saveSelection">
        <radio-group class="language-grid" @change="selectLocale">
          <label
            v-for="option in localeOptions"
            :key="option.value"
            class="language-card"
            :class="{ 'language-card--selected': state.selectedLocale === option.value }"
          >
            <radio
              class="language-radio"
              :value="option.value"
              :checked="state.selectedLocale === option.value"
              :disabled="state.isBusy"
              color="#38d4b2"
            />
            <view class="option-copy">
              <text class="option-code">{{ option.code }}</text>
              <text class="option-name">{{ t(option.labelKey) }}</text>
              <text class="option-detail">{{ t(option.detailKey) }}</text>
            </view>
            <view class="selection-dot" aria-hidden="true" />
          </label>
        </radio-group>

        <button
          class="save-button"
          form-type="submit"
          :disabled="state.isSubmitDisabled"
          :loading="state.isBusy"
        >
          {{ state.isBusy ? t('settings.save.saving') : actionText }}
        </button>
      </form>

      <view v-if="state.feedback === 'success'" class="feedback feedback--success" role="status">
        <text class="feedback-mark" aria-hidden="true">✓</text>
        <text>{{ t('settings.save.success') }}</text>
      </view>

      <view v-else-if="state.feedback === 'error' && state.errorFeedback" class="feedback feedback--error" role="alert">
        <text class="feedback-mark" aria-hidden="true">!</text>
        <view class="feedback-copy">
          <text>{{ state.errorFeedback.message }}</text>
          <text v-if="state.errorFeedback.traceId" class="trace-id">
            {{ t('traceId.label') }}: {{ state.errorFeedback.traceId }}
          </text>
        </view>
      </view>

      <footer class="panel-footer">
        <view class="footer-line" aria-hidden="true" />
        <text>{{ t('settings.locale.hint') }}</text>
      </footer>
    </main>
  </view>
</template>

<style scoped>
.locale-page {
  --ink: #e7f3f2;
  --muted: #91a7ad;
  --panel: rgba(12, 31, 48, 0.94);
  --line: rgba(139, 179, 184, 0.18);
  --accent: #38d4b2;
  position: relative;
  box-sizing: border-box;
  min-height: 100vh;
  overflow: hidden;
  padding: calc(42rpx + env(safe-area-inset-top)) calc(28rpx + env(safe-area-inset-right)) calc(40rpx + env(safe-area-inset-bottom)) calc(28rpx + env(safe-area-inset-left));
  background:
    linear-gradient(145deg, rgba(15, 43, 62, 0.82) 0%, rgba(7, 20, 33, 0.98) 54%, #06111d 100%);
  color: var(--ink);
}

.ambient {
  position: absolute;
  width: 420rpx;
  height: 420rpx;
  border: 1px solid rgba(56, 212, 178, 0.13);
  border-radius: 50%;
  pointer-events: none;
}

.ambient--top {
  top: -238rpx;
  right: -150rpx;
  box-shadow: 0 0 120rpx rgba(56, 212, 178, 0.08) inset;
}

.ambient--bottom {
  bottom: -310rpx;
  left: -245rpx;
  width: 540rpx;
  height: 540rpx;
}

.control-panel {
  position: relative;
  z-index: 1;
  box-sizing: border-box;
  width: 100%;
  max-width: 760px;
  margin: 0 auto;
  padding: 38rpx;
  border: 1px solid var(--line);
  border-radius: 28rpx;
  background: var(--panel);
  box-shadow: 0 26rpx 80rpx rgba(0, 7, 15, 0.34);
  backdrop-filter: blur(20px);
}

.hero {
  display: flex;
  align-items: flex-start;
  gap: 24rpx;
}

.brand-mark {
  display: flex;
  flex: 0 0 68rpx;
  align-items: center;
  justify-content: center;
  width: 68rpx;
  height: 68rpx;
  border: 1px solid rgba(56, 212, 178, 0.56);
  border-radius: 18rpx;
  background: rgba(56, 212, 178, 0.08);
  color: var(--accent);
  font-size: 32rpx;
  font-weight: 700;
  letter-spacing: -0.04em;
}

.hero-copy,
.mode-copy,
.option-copy,
.feedback-copy {
  display: flex;
  flex-direction: column;
}

.hero-copy {
  min-width: 0;
}

.eyebrow {
  margin-bottom: 10rpx;
  color: var(--accent);
  font-size: 20rpx;
  font-weight: 700;
  letter-spacing: 0.16em;
}

.title {
  color: #f6fbfa;
  font-size: 44rpx;
  font-weight: 650;
  line-height: 1.18;
  letter-spacing: -0.03em;
}

.description {
  margin-top: 14rpx;
  color: var(--muted);
  font-size: 25rpx;
  line-height: 1.7;
}

.status-strip {
  display: grid;
  grid-template-columns: minmax(0, 1fr) 1px minmax(0, 1fr);
  gap: 24rpx;
  margin: 34rpx 0 24rpx;
  padding: 24rpx 26rpx;
  border: 1px solid var(--line);
  border-radius: 20rpx;
  background: rgba(4, 17, 29, 0.5);
}

.status-item {
  display: flex;
  min-width: 0;
  flex-direction: column;
  gap: 8rpx;
}

.status-item--end {
  text-align: right;
}

.status-rule {
  width: 1px;
  background: var(--line);
}

.status-label {
  color: #718b94;
  font-size: 20rpx;
  letter-spacing: 0.08em;
  text-transform: uppercase;
}

.status-value {
  overflow: hidden;
  color: #dcebea;
  font-size: 27rpx;
  font-weight: 600;
  text-overflow: ellipsis;
  white-space: nowrap;
}

.status-value--accent {
  color: var(--accent);
}

.mode-card {
  display: flex;
  align-items: flex-start;
  gap: 16rpx;
  margin-bottom: 28rpx;
  padding: 18rpx 20rpx;
  border-left: 4rpx solid rgba(56, 212, 178, 0.55);
  background: rgba(56, 212, 178, 0.055);
}

.mode-indicator {
  flex: 0 0 12rpx;
  width: 12rpx;
  height: 12rpx;
  margin-top: 10rpx;
  border-radius: 50%;
  background: var(--accent);
  box-shadow: 0 0 18rpx rgba(56, 212, 178, 0.52);
}

.mode-title {
  color: #b9cecf;
  font-size: 23rpx;
  line-height: 1.55;
}

.mode-version {
  margin-top: 5rpx;
  color: #718b94;
  font-size: 20rpx;
}

.language-grid {
  display: grid;
  grid-template-columns: repeat(2, minmax(0, 1fr));
  gap: 18rpx;
}

.language-card {
  position: relative;
  display: flex;
  box-sizing: border-box;
  min-height: 206rpx;
  align-items: flex-start;
  gap: 16rpx;
  padding: 24rpx;
  border: 1px solid rgba(139, 179, 184, 0.2);
  border-radius: 22rpx;
  background: rgba(8, 24, 38, 0.82);
  transition: border-color 160ms ease, background-color 160ms ease, transform 160ms ease;
}

.language-card--selected {
  border-color: rgba(56, 212, 178, 0.7);
  background: rgba(21, 67, 72, 0.42);
  box-shadow: 0 0 0 1px rgba(56, 212, 178, 0.08) inset;
}

.language-card:focus-within {
  outline: 3px solid #9df7e1;
  outline-offset: 4px;
}

.language-radio {
  margin-top: 2rpx;
  transform: scale(0.82);
  transform-origin: top left;
}

.option-copy {
  min-width: 0;
  flex: 1;
}

.option-code {
  color: #64818b;
  font-size: 18rpx;
  font-weight: 700;
  letter-spacing: 0.13em;
}

.option-name {
  margin-top: 14rpx;
  color: #f1f8f7;
  font-size: 29rpx;
  font-weight: 650;
  line-height: 1.3;
}

.option-detail {
  margin-top: 9rpx;
  color: var(--muted);
  font-size: 21rpx;
  line-height: 1.5;
}

.selection-dot {
  position: absolute;
  right: 19rpx;
  bottom: 19rpx;
  width: 9rpx;
  height: 9rpx;
  border-radius: 50%;
  background: rgba(139, 179, 184, 0.22);
}

.language-card--selected .selection-dot {
  background: var(--accent);
  box-shadow: 0 0 16rpx rgba(56, 212, 178, 0.65);
}

.save-button {
  display: flex;
  width: 100%;
  min-height: 92rpx;
  align-items: center;
  justify-content: center;
  margin-top: 26rpx;
  border: 1px solid #66e6c9;
  border-radius: 18rpx;
  background: #38d4b2;
  color: #05231f;
  font-size: 26rpx;
  font-weight: 750;
  letter-spacing: 0.02em;
  line-height: 1.2;
  box-shadow: 0 14rpx 34rpx rgba(19, 128, 109, 0.22);
}

.save-button::after {
  border: 0;
}

.save-button:focus-visible {
  outline: 3px solid #e8fff9;
  outline-offset: 5px;
}

.save-button[disabled] {
  border-color: rgba(123, 153, 158, 0.22);
  background: #18303e;
  color: #6f8990;
  box-shadow: none;
  opacity: 1;
}

.feedback {
  display: flex;
  align-items: flex-start;
  gap: 14rpx;
  margin-top: 20rpx;
  padding: 18rpx 20rpx;
  border-radius: 16rpx;
  font-size: 23rpx;
  line-height: 1.5;
}

.feedback--success {
  border: 1px solid rgba(56, 212, 178, 0.25);
  background: rgba(56, 212, 178, 0.08);
  color: #a8f4df;
}

.feedback--error {
  border: 1px solid rgba(241, 144, 121, 0.32);
  background: rgba(107, 42, 39, 0.22);
  color: #ffd0c5;
}

.feedback-mark {
  flex: 0 0 auto;
  font-weight: 800;
}

.trace-id {
  margin-top: 6rpx;
  color: #dca398;
  font-family: Consolas, "SFMono-Regular", monospace;
  font-size: 19rpx;
  word-break: break-all;
}

.panel-footer {
  display: flex;
  align-items: flex-start;
  gap: 16rpx;
  margin-top: 30rpx;
  color: #6f8990;
  font-size: 20rpx;
  line-height: 1.55;
}

.footer-line {
  flex: 0 0 30rpx;
  width: 30rpx;
  height: 1px;
  margin-top: 14rpx;
  background: rgba(56, 212, 178, 0.42);
}

@media (max-width: 480px) {
  .locale-page {
    padding-right: calc(22rpx + env(safe-area-inset-right));
    padding-left: calc(22rpx + env(safe-area-inset-left));
  }

  .control-panel {
    padding: 30rpx 24rpx;
    border-radius: 24rpx;
  }

  .language-grid {
    grid-template-columns: 1fr;
  }

  .language-card {
    min-height: 176rpx;
  }
}

@media (prefers-reduced-motion: reduce) {
  .language-card,
  .save-button {
    transition: none;
  }
}
</style>
