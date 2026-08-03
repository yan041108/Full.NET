<script setup lang="ts">
import { computed } from 'vue';
import { Check, Close } from '@element-plus/icons-vue';
import {
  ElButton,
  ElDrawer,
  ElIcon,
  ElInputNumber,
  ElMessage,
  ElOption,
  ElSelect,
  ElSwitch
} from 'element-plus';
import type { MessageKey } from '@fullnet/admin-i18n';
import {
  ART_SHELL_MAIN_COLORS,
  type ArtBoxStyle,
  type ArtContainerWidth,
  type ArtCustomRadius,
  type ArtMenuLayout,
  type ArtMenuStyle,
  type ArtTabStyle,
  type ArtThemeMode
} from '../composables/artShellSettingsDefaults';
import { useArtShellPreferences } from '../composables/useArtShellPreferences';

defineOptions({ name: 'ArtSettingsPanel' });

const props = defineProps<{
  closeLabel: string;
  translate: (key: MessageKey) => string;
}>();

const visible = defineModel<boolean>('open', { default: false });
const { settings, patchSettings, resetSettings, exportSettingsJson } = useArtShellPreferences();

const themeOptions = computed(() => [
  { value: 'light' as const, label: props.translate('shell.settingsThemeLightCard') },
  { value: 'dark' as const, label: props.translate('shell.settingsThemeDarkCard') }
]);

const menuLayoutOptions = computed(() => [
  { value: 'left' as const, label: props.translate('shell.settingsMenuLayoutLeft') },
  { value: 'top' as const, label: props.translate('shell.settingsMenuLayoutTop') },
  { value: 'top-left' as const, label: props.translate('shell.settingsMenuLayoutTopLeft') },
  { value: 'dual-menu' as const, label: props.translate('shell.settingsMenuLayoutDual') }
]);

const menuStyleOptions = computed(() => [
  { value: 'design' as const, label: props.translate('shell.settingsMenuStyleDesign') },
  { value: 'light' as const, label: props.translate('shell.settingsThemeLightCard') },
  { value: 'dark' as const, label: props.translate('shell.settingsThemeDarkCard') }
]);

const tabStyleOptions = computed(() => [
  { value: 'default' as const, label: props.translate('shell.settingsTabDefault') },
  { value: 'card' as const, label: props.translate('shell.settingsTabCard') },
  { value: 'google' as const, label: props.translate('shell.settingsTabGoogle') }
]);

const radiusOptions: Array<{ value: ArtCustomRadius; label: string }> = [
  { value: '0', label: '0' },
  { value: '0.25', label: '0.25' },
  { value: '0.5', label: '0.5' },
  { value: '0.75', label: '0.75' },
  { value: '1', label: '1' }
];

const basicToggles = computed(() => [
  { key: 'showPageTabs' as const, label: props.translate('shell.settingsShowPageTabs') },
  { key: 'dualMenuShowText' as const, label: props.translate('shell.settingsDualMenuShowText'), layoutOnly: 'dual-menu' as const },
  { key: 'uniqueOpened' as const, label: props.translate('shell.settingsUniqueOpened') },
  { key: 'showMenuButton' as const, label: props.translate('shell.settingsShowMenuButton') },
  { key: 'showRefreshButton' as const, label: props.translate('shell.settingsShowRefreshButton') },
  { key: 'showBreadcrumb' as const, label: props.translate('shell.settingsShowBreadcrumb'), mobileHide: true },
  { key: 'showLanguage' as const, label: props.translate('shell.settingsShowLanguage') },
  { key: 'showFullscreen' as const, label: props.translate('shell.settingsShowFullscreen') }
]);

function selectTheme(mode: ArtThemeMode): void {
  patchSettings({ themeMode: mode });
}

function selectMenuLayout(layout: ArtMenuLayout): void {
  patchSettings({ menuLayout: layout });
}

function selectMenuStyle(style: ArtMenuStyle): void {
  patchSettings({ menuStyle: style });
}

function selectPrimaryColor(color: string): void {
  patchSettings({ primaryColor: color });
}

function selectBoxStyle(style: ArtBoxStyle): void {
  patchSettings({ boxStyle: style });
}

function selectContainerWidth(width: ArtContainerWidth): void {
  patchSettings({ containerWidth: width });
}

function updateToggle(
  key: 'showPageTabs' | 'dualMenuShowText' | 'uniqueOpened' | 'showMenuButton' | 'showRefreshButton' | 'showBreadcrumb' | 'showLanguage' | 'showFullscreen',
  value: boolean
): void {
  patchSettings({ [key]: value });
}

async function copyConfig(): Promise<void> {
  try {
    await navigator.clipboard.writeText(exportSettingsJson());
    ElMessage.success(props.translate('shell.settingsCopySuccess'));
  } catch {
    ElMessage.error(props.translate('shell.settingsCopyFailed'));
  }
}

function handleReset(): void {
  resetSettings();
  ElMessage.success(props.translate('shell.settingsResetSuccess'));
}
</script>

<template>
  <el-drawer
    v-model="visible"
    direction="rtl"
    size="300px"
    :with-header="false"
    :lock-scroll="true"
    :destroy-on-close="false"
    :close-on-click-modal="true"
    :close-on-press-escape="true"
    append-to-body
    modal-class="art-settings-modal"
  >
    <div class="art-settings-drawer">
      <div class="art-settings-drawer__header">
        <button
          type="button"
          class="art-settings-drawer__close"
          :aria-label="closeLabel"
          @click="visible = false"
        >
          <el-icon :size="20"><Close /></el-icon>
        </button>
      </div>

      <p class="art-settings-section-title">{{ translate('shell.settingsThemeSection') }}</p>
      <div class="art-settings-box-wrap">
        <button
          v-for="option in themeOptions"
          :key="option.value"
          type="button"
          class="art-settings-item is-wide"
          :aria-pressed="settings.themeMode === option.value ? 'true' : 'false'"
          @click="selectTheme(option.value)"
        >
          <div
            class="art-settings-item__box"
            :class="{ 'is-active': settings.themeMode === option.value }"
          >
            <div
              class="art-settings-theme-preview"
              :class="`art-settings-theme-preview--${option.value}`"
            >
              <span class="art-settings-theme-preview__sidebar" />
              <span class="art-settings-theme-preview__main">
                <span class="art-settings-theme-preview__bar" />
                <span class="art-settings-theme-preview__block" />
                <span class="art-settings-theme-preview__block is-short" />
              </span>
            </div>
          </div>
          <span class="art-settings-item__name">{{ option.label }}</span>
        </button>
      </div>

      <section class="art-settings-menu-layout-section">
        <p class="art-settings-section-title">{{ translate('shell.settingsMenuLayoutTitle') }}</p>
        <div class="art-settings-box-wrap">
          <button
            v-for="(option, index) in menuLayoutOptions"
            :key="option.value"
            type="button"
            class="art-settings-item"
            :class="{ 'is-span-2': index > 2 }"
            @click="selectMenuLayout(option.value)"
          >
            <div
              class="art-settings-item__box"
              :class="{ 'is-active': settings.menuLayout === option.value }"
            >
              <div
                class="art-settings-menu-layout-preview"
                :class="`art-settings-menu-layout-preview--${option.value === 'dual-menu' ? 'dual' : option.value}`"
              />
            </div>
            <span class="art-settings-item__name">{{ option.label }}</span>
          </button>
        </div>
      </section>

      <p class="art-settings-section-title">{{ translate('shell.settingsMenuStyleTitle') }}</p>
      <div class="art-settings-box-wrap">
        <button
          v-for="option in menuStyleOptions"
          :key="option.value"
          type="button"
          class="art-settings-item"
          @click="selectMenuStyle(option.value)"
        >
          <div
            class="art-settings-item__box"
            :class="{ 'is-active': settings.menuStyle === option.value }"
          >
            <div
              class="art-settings-menu-style-preview"
              :class="`art-settings-menu-style-preview--${option.value}`"
            />
          </div>
          <span class="art-settings-item__name">{{ option.label }}</span>
        </button>
      </div>

      <p class="art-settings-section-title">{{ translate('shell.settingsColorTitle') }}</p>
      <div class="art-settings-color-grid">
        <button
          v-for="color in ART_SHELL_MAIN_COLORS"
          :key="color"
          type="button"
          class="art-settings-color-dot"
          :style="{ background: color }"
          :aria-label="color"
          @click="selectPrimaryColor(color)"
        >
          <Check v-if="settings.primaryColor === color" aria-hidden="true" />
        </button>
      </div>

      <p class="art-settings-section-title">{{ translate('shell.settingsBoxTitle') }}</p>
      <div class="art-settings-segment">
        <button
          type="button"
          class="art-settings-segment__option"
          :class="{ 'is-active': settings.boxStyle === 'border' }"
          @click="selectBoxStyle('border')"
        >
          {{ translate('shell.settingsBoxBorder') }}
        </button>
        <button
          type="button"
          class="art-settings-segment__option"
          :class="{ 'is-active': settings.boxStyle === 'shadow' }"
          @click="selectBoxStyle('shadow')"
        >
          {{ translate('shell.settingsBoxShadow') }}
        </button>
      </div>

      <p class="art-settings-section-title">{{ translate('shell.settingsContainerTitle') }}</p>
      <div class="art-settings-container-grid">
        <button
          type="button"
          class="art-settings-container-option"
          :class="{ 'is-active': settings.containerWidth === 'full' }"
          @click="selectContainerWidth('full')"
        >
          {{ translate('shell.settingsContainerFull') }}
        </button>
        <button
          type="button"
          class="art-settings-container-option"
          :class="{ 'is-active': settings.containerWidth === 'boxed' }"
          @click="selectContainerWidth('boxed')"
        >
          {{ translate('shell.settingsContainerBoxed') }}
        </button>
      </div>

      <p class="art-settings-section-title">{{ translate('shell.settingsBasicsTitle') }}</p>
      <div
        v-for="item in basicToggles"
        v-show="!item.layoutOnly || settings.menuLayout === item.layoutOnly"
        :key="item.key"
        class="art-settings-basic-item"
        :class="{ 'is-mobile-hide': item.mobileHide }"
      >
        <span>{{ item.label }}</span>
        <el-switch
          :model-value="settings[item.key]"
          @change="value => updateToggle(item.key, Boolean(value))"
        />
      </div>

      <div class="art-settings-basic-item">
        <span>{{ translate('shell.settingsMenuOpenWidth') }}</span>
        <el-input-number
          :model-value="settings.menuOpenWidth"
          :min="180"
          :max="320"
          :step="10"
          controls-position="right"
          style="width: 120px"
          @change="value => patchSettings({ menuOpenWidth: Number(value ?? settings.menuOpenWidth) })"
        />
      </div>

      <div class="art-settings-basic-item">
        <span>{{ translate('shell.settingsTabStyle') }}</span>
        <el-select
          :model-value="settings.tabStyle"
          style="width: 120px"
          @change="value => patchSettings({ tabStyle: value as ArtTabStyle })"
        >
          <el-option
            v-for="option in tabStyleOptions"
            :key="option.value"
            :label="option.label"
            :value="option.value"
          />
        </el-select>
      </div>

      <div class="art-settings-basic-item">
        <span>{{ translate('shell.settingsCustomRadius') }}</span>
        <el-select
          :model-value="settings.customRadius"
          style="width: 120px"
          @change="value => patchSettings({ customRadius: value as ArtCustomRadius })"
        >
          <el-option
            v-for="option in radiusOptions"
            :key="option.value"
            :label="option.label"
            :value="option.value"
          />
        </el-select>
      </div>

      <div class="art-settings-actions">
        <el-button type="primary" @click="copyConfig">
          {{ translate('shell.settingsCopyConfig') }}
        </el-button>
        <el-button type="danger" plain @click="handleReset">
          {{ translate('shell.settingsResetConfig') }}
        </el-button>
      </div>
    </div>
  </el-drawer>
</template>
