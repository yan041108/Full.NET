<script setup lang="ts">
import { Icon } from '@iconify/vue';
import { ElIcon } from 'element-plus';
import { computed } from 'vue';
import {
  isHostMenuIcon,
  isIconifyMenuIcon,
  resolveMenuIconComponent
} from './host-menu-icons';

const props = withDefaults(defineProps<{
  icon: string;
  size?: number;
}>(), {
  size: 22
});

const legacyComponent = computed(() =>
  isHostMenuIcon(props.icon) ? resolveMenuIconComponent(props.icon) : null
);

const useIconify = computed(() => isIconifyMenuIcon(props.icon));
</script>

<template>
  <Icon
    v-if="useIconify"
    :icon="icon"
    :width="size"
    :height="size"
    aria-hidden="true"
  />
  <el-icon v-else :size="size" aria-hidden="true">
    <component :is="legacyComponent ?? resolveMenuIconComponent(icon)" />
  </el-icon>
</template>
