<script setup lang="ts">
import { computed, ref, watch } from 'vue';
import { useRoute, useRouter } from 'vue-router';
import { ElButton } from 'element-plus';
import { useAdminI18n } from '../i18n/adminI18n';
import { personalShortcutKey, readPersonalShortcuts, savePersonalShortcuts, type PersonalShortcutScope } from '../navigation/personal-shortcuts';

const props = defineProps<{ scope: PersonalShortcutScope; switching: boolean; navigation: readonly { routeName: string; title: string }[] }>();
const emit = defineEmits<{ navigate: [] }>();
const route = useRoute(), router = useRouter(), { t } = useAdminI18n();
const names = ref<string[]>([]), problem = ref('');
const allowed = computed(() => new Map(props.navigation.map(item => [item.routeName, item])));
const visible = computed(() => props.switching ? [] : names.value.flatMap(name => { const item = allowed.value.get(name); return item ? [item] : []; }));
const current = computed(() => !props.switching && typeof route.name === 'string' ? allowed.value.get(route.name) : undefined);
const pinned = computed(() => !!current.value && names.value.includes(current.value.routeName));
watch(() => personalShortcutKey(props.scope), () => {
  problem.value = ''; names.value = [];
  try { names.value = readPersonalShortcuts(localStorage, props.scope); } catch { problem.value = t('shortcuts.unavailable'); }
}, { immediate: true, flush: 'sync' });
function toggle() {
  if (!current.value || props.switching) return;
  const next = names.value.filter(name => allowed.value.has(name) && name !== current.value!.routeName);
  if (!pinned.value) next.push(current.value.routeName);
  if (next.length > 12) { problem.value = t('shortcuts.limit'); return; }
  try { savePersonalShortcuts(localStorage, props.scope, next, new Set(allowed.value.keys())); names.value = next; problem.value = ''; }
  catch { problem.value = t('shortcuts.unavailable'); }
}
async function navigate(name: string) {
  if (props.switching || !allowed.value.has(name)) return;
  await router.push({ name }); emit('navigate');
}
</script>
<template>
  <section class="personal-shortcuts" :aria-label="t('shortcuts.title')" data-testid="personal-shortcuts">
    <div class="shortcut-heading"><strong>{{ t('shortcuts.title') }}</strong><ElButton :disabled="!current" data-testid="shortcut-toggle-current" @click="toggle">{{ t(pinned ? 'shortcuts.unpinCurrent' : 'shortcuts.pinCurrent') }}</ElButton></div>
    <p>{{ t('shortcuts.localHint') }}</p>
    <div class="shortcut-links"><ElButton v-for="item in visible" :key="item.routeName" link type="primary" :data-shortcut-route="item.routeName" @click="navigate(item.routeName)">{{ item.title }}</ElButton></div>
    <p v-if="!visible.length">{{ t('shortcuts.empty') }}</p><p v-if="problem" role="alert">{{ problem }}</p>
  </section>
</template>
<style scoped>
.personal-shortcuts { margin: 12px 0; padding: 12px; border: 1px solid var(--art-card-border); border-radius: var(--art-radius); }
.shortcut-heading, .shortcut-links { display: flex; flex-wrap: wrap; align-items: center; gap: 8px; }
.shortcut-heading { justify-content: space-between; }
.personal-shortcuts p { margin: 8px 0; font-size: 12px; }
.shortcut-links :deep(.el-button + .el-button) { margin-left: 0; }
</style>
