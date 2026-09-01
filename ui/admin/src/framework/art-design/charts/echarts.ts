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

/** 仅首次注册按需图表组件，避免重复 use 带来的额外初始化成本。 */
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

/** 图表创建选项，目前只承载主题和语言等壳层级上下文。 */
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

/** 在统一主题合并后再应用动效策略，保证减弱动画时仍保留最新视觉变量。 */
function buildChartOption(
  option: EChartsOption,
  options: CreateFullNetChartOptions
): EChartsOption {
  return {
    ...applyChartMotionPolicy(mergeChartTheme(option, options))
  };
}

/** 调整图表尺寸，供容器尺寸变化或抽屉展开后复用。 */
export function resizeFullNetChart(chart: ECharts | null | undefined): void {
  chart?.resize();
}

/** 销毁图表实例并释放关联 DOM/Canvas 资源。 */
export function disposeFullNetChart(chart: ECharts | null | undefined): void {
  chart?.dispose();
}

export type { ECharts };
