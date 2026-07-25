<script setup lang="ts">
import {
  computed,
  onBeforeUnmount,
  onMounted,
  ref,
  shallowRef,
  watch
} from 'vue';
import type { EChartsCoreOption } from 'echarts/core';
import type { ECharts } from 'echarts/core';
import {
  createFullNetChart,
  disposeFullNetChart,
  resizeFullNetChart,
  updateFullNetChart
} from './echarts';

defineOptions({ name: 'FullNetChart' });

const props = withDefaults(defineProps<{
  option: EChartsCoreOption | null;
  height?: number;
  ariaLabel: string;
  summary?: string;
  emptyLabel: string;
  errorLabel?: string;
  locale?: string;
  themeMode?: 'light' | 'dark';
  tableCaption?: string;
}>(), {
  height: 260,
  themeMode: 'light'
});

const containerRef = ref<HTMLElement | null>(null);
const chartRef = shallowRef<ECharts | null>(null);
const failed = ref(false);
let resizeObserver: ResizeObserver | undefined;

const hasRenderableSeries = computed(() => {
  if (!props.option?.series) {
    return false;
  }

  const seriesList = Array.isArray(props.option.series)
    ? props.option.series
    : [props.option.series];

  return seriesList.some(series => {
    const data = 'data' in series ? series.data : undefined;
    return Array.isArray(data) ? data.length > 0 : Boolean(data);
  });
});

const tableRows = computed(() => {
  if (!props.option?.series || !props.option.xAxis) {
    return [];
  }

  const xAxis = Array.isArray(props.option.xAxis)
    ? props.option.xAxis[0]
    : props.option.xAxis;
  const labels = Array.isArray(xAxis?.data) ? xAxis.data.map(String) : [];
  const series = Array.isArray(props.option.series)
    ? props.option.series[0]
    : props.option.series;
  const values = Array.isArray(series?.data) ? series.data : [];

  return labels.map((label: string, index: number) => ({
    label,
    value: values[index] ?? ''
  }));
});

function renderChart(): void {
  if (!containerRef.value || !props.option || !hasRenderableSeries.value) {
    disposeFullNetChart(chartRef.value);
    chartRef.value = null;
    return;
  }

  try {
    if (!chartRef.value) {
      chartRef.value = createFullNetChart(containerRef.value, props.option, {
        locale: props.locale,
        themeMode: props.themeMode
      });
    } else {
      updateFullNetChart(chartRef.value, props.option, {
        locale: props.locale,
        themeMode: props.themeMode
      });
    }

    failed.value = false;
  } catch {
    failed.value = true;
    disposeFullNetChart(chartRef.value);
    chartRef.value = null;
  }
}

onMounted(() => {
  renderChart();
  if (!containerRef.value || typeof ResizeObserver === 'undefined') {
    return;
  }

  resizeObserver = new ResizeObserver(() => {
    resizeFullNetChart(chartRef.value);
  });
  resizeObserver.observe(containerRef.value);
});

onBeforeUnmount(() => {
  resizeObserver?.disconnect();
  disposeFullNetChart(chartRef.value);
  chartRef.value = null;
});

watch(
  () => [props.option, props.locale, props.themeMode, hasRenderableSeries.value] as const,
  () => {
    renderChart();
  },
  { deep: true }
);
</script>

<template>
  <div class="fullnet-chart" :style="{ minHeight: `${height}px` }">
    <p v-if="summary" class="fullnet-chart__summary">{{ summary }}</p>

    <div
      v-if="hasRenderableSeries && !failed"
      ref="containerRef"
      class="fullnet-chart__canvas"
      :style="{ height: `${height}px` }"
      role="img"
      :aria-label="ariaLabel"
      data-testid="fullnet-chart-canvas"
    />

    <p
      v-else-if="failed"
      class="fullnet-chart__state fullnet-chart__state--error"
      role="alert"
    >
      {{ errorLabel ?? emptyLabel }}
    </p>
    <p v-else class="fullnet-chart__state" role="status">{{ emptyLabel }}</p>

    <table
      v-if="tableRows.length > 0"
      class="fullnet-chart__table"
    >
      <caption>{{ tableCaption ?? ariaLabel }}</caption>
      <thead>
        <tr>
          <th scope="col">Label</th>
          <th scope="col">Value</th>
        </tr>
      </thead>
      <tbody>
        <tr v-for="row in tableRows" :key="row.label">
          <th scope="row">{{ row.label }}</th>
          <td>{{ row.value }}</td>
        </tr>
      </tbody>
    </table>
  </div>
</template>

<style scoped>
.fullnet-chart {
  position: relative;
  max-width: 100%;
  overflow: hidden;
}

.fullnet-chart__canvas {
  width: 100%;
}

.fullnet-chart__summary {
  margin: 0 0 8px;
  color: var(--fullnet-color-ink-muted);
  font-size: 11px;
}

.fullnet-chart__state {
  display: grid;
  place-items: center;
  min-height: 180px;
  border: 1px dashed var(--fullnet-color-line);
  border-radius: var(--fullnet-radius-sm);
  color: var(--fullnet-color-ink-muted);
  font-size: 12px;
}

.fullnet-chart__state--error {
  border-color: var(--fullnet-color-danger);
  color: var(--fullnet-color-danger);
}

.fullnet-chart__table {
  position: absolute;
  width: 1px;
  height: 1px;
  margin: -1px;
  padding: 0;
  overflow: hidden;
  clip: rect(0, 0, 0, 0);
  white-space: nowrap;
  border: 0;
}
</style>
