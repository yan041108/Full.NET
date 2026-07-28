/**
 * ECharts 按需注册入口。
 * 来源参考：art-design-pro @ f3aaf58 — src/plugins/echarts.ts（裁剪）
 * 禁止从完整 `echarts` 包副作用导入。
 */
import { init, use, type ECharts, type EChartsCoreOption } from 'echarts/core';
import { LineChart } from 'echarts/charts';
import {
  GridComponent,
  TooltipComponent
} from 'echarts/components';
import { CanvasRenderer } from 'echarts/renderers';
import { applyChartMotionPolicy, mergeChartTheme } from './fullNetChartTheme';

let registered = false;

function ensureRegistered(): void {
  if (registered) {
    return;
  }

  use([
    LineChart,
    TooltipComponent,
    GridComponent,
    CanvasRenderer
  ]);
  registered = true;
}

export interface CreateFullNetChartOptions {
  locale?: string;
  themeMode?: 'light' | 'dark';
}

export type EChartsOption = EChartsCoreOption;

/** 在容器上创建并渲染图表实例。 */
export function createFullNetChart(
  element: HTMLElement,
  option: EChartsOption,
  options: CreateFullNetChartOptions = {}
): ECharts {
  ensureRegistered();
  const chart = init(element);
  chart.setOption(buildChartOption(option, options));
  return chart;
}

/** 更新已有实例配置。 */
export function updateFullNetChart(
  chart: ECharts | null | undefined,
  option: EChartsOption,
  options: CreateFullNetChartOptions = {}
): void {
  if (!chart) {
    return;
  }

  chart.setOption(buildChartOption(option, options), true);
}

function buildChartOption(
  option: EChartsOption,
  options: CreateFullNetChartOptions
): EChartsOption {
  return {
    ...applyChartMotionPolicy(mergeChartTheme(option, options))
  };
}

export function resizeFullNetChart(chart: ECharts | null | undefined): void {
  chart?.resize();
}

export function disposeFullNetChart(chart: ECharts | null | undefined): void {
  chart?.dispose();
}

export type { ECharts };
