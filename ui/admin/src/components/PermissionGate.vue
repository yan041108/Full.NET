<script setup lang="ts">
import { computed } from 'vue';
import { usePermission } from '../auth/permission';

const props = defineProps<{
  code: string;
}>();

const { can } = usePermission();
const allowed = computed(() => can(props.code));
</script>

<template>
  <slot v-if="allowed" />
</template>