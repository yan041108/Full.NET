<script setup lang="ts">
import { computed } from 'vue';
import { usePermission } from '../auth/permission';

/** 仅在当前会话具备指定权限码时渲染插槽内容。 */
const props = defineProps<{
  code: string;
}>();

const { can } = usePermission();
const allowed = computed(() => can(props.code));
</script>

<template>
  <slot v-if="allowed" />
</template>
