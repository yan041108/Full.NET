<script setup lang="ts">
import { ref, watch } from 'vue';
import { ElCascader, type CascaderProps, type CascaderValue } from 'element-plus';
import type { AdministrativeRegionChild } from '@fullnet/client-contracts';
import { listAdministrativeRegionChildren } from '../api/administrative-regions';

type CascaderOption = {
  value: string;
  label: string;
  leaf: boolean;
  children?: CascaderOption[];
}

const props = withDefaults(defineProps<{
  modelValue?: string[];
  placeholder?: string;
  disabled?: boolean;
  clearable?: boolean;
}>(), {
  modelValue: () => [],
  placeholder: '',
  disabled: false,
  clearable: true
});

const emit = defineEmits<{
  'update:modelValue': [value: string[]];
}>();

const selected = ref<string[]>([...props.modelValue]);

watch(
  () => props.modelValue,
  value => {
    selected.value = [...value];
  }
);

const loadChildren: NonNullable<CascaderProps['lazyLoad']> = async (node, resolve) => {
  try {
    const parentId = node.level === 0 ? undefined : String(node.value);
    const children = await listAdministrativeRegionChildren(parentId);
    resolve(children.map(mapChild));
  } catch {
    resolve([]);
  }
}

function mapChild(child: AdministrativeRegionChild): CascaderOption {
  return {
    value: child.id,
    label: child.name,
    leaf: !child.hasChildren
  };
}

function handleChange(value: CascaderValue | null | undefined): void {
  const normalized = (Array.isArray(value) ? value : value == null ? [] : [value])
    .filter((item): item is string => typeof item === 'string');
  selected.value = normalized;
  emit('update:modelValue', normalized);
}
</script>

<template>
  <el-cascader
    v-model="selected"
    data-testid="administrative-region-cascader"
    :props="{
      lazy: true,
      lazyLoad: loadChildren,
      value: 'value',
      label: 'label',
      leaf: 'leaf'
    }"
    :placeholder="placeholder"
    :disabled="disabled"
    :clearable="clearable"
    filterable
    @change="handleChange"
  />
</template>
