import type { LineSeriesOption } from 'echarts/charts';
import type { EChartsCoreOption } from 'echarts/core';
import type { CreateFullNetChartOptions } from './echarts';

type EChartsOption = EChartsCoreOption;

/** 兼容单轴与多轴配置时的最小可着色结构。 */
interface AxisLike {
  axisLine?: Record<string, unknown>;
  axisLabel?: Record<string, unknown>;
  splitLine?: Record<string, unknown>;
  [key: string]: unknown;
}

const lightTheme = {
  axis: '#8a969c',
  grid: '#e7ebe7',
  line: '#0b8f87',
  areaTop: 'rgba(11, 143, 135, 0.34)',
  areaBottom: 'rgba(11, 143, 135, 0.01)',
  tooltipBg: '#172027',
  tooltipText: '#ffffff'
};

const darkTheme = {
  axis: '#9aa7aa',
  grid: 'rgba(255, 255, 255, 0.08)',
  line: '#42b9a6',
  areaTop: 'rgba(66, 185, 166, 0.28)',
  areaBottom: 'rgba(66, 185, 166, 0.02)',
  tooltipBg: '#10161a',
  tooltipText: '#f4f7f8'
};

/** 根据亮暗主题选择一组稳定的图表语义色。 */
function resolvePalette(themeMode: 'light' | 'dark' = 'light') {
  return themeMode === 'dark' ? darkTheme : lightTheme;
}

/** 将 Full.NET 语义色注入 ECharts Option，不执行服务端字符串。 */
export function mergeChartTheme(
  option: EChartsOption,
  options: CreateFullNetChartOptions = {}
): EChartsOption {
  const palette = resolvePalette(options.themeMode);
  const nextOption: EChartsOption = {
    ...option,
    textStyle: {
      fontFamily: 'var(--fullnet-font-sans)',
      color: palette.axis,
      ...(option.textStyle ?? {})
    },
    tooltip: {
      trigger: 'axis',
      backgroundColor: palette.tooltipBg,
      borderWidth: 0,
      textStyle: { color: palette.tooltipText },
      ...(option.tooltip ?? {})
    },
    xAxis: Array.isArray(option.xAxis)
      ? option.xAxis.map((axis: AxisLike) => ({
          ...axis,
          axisLine: { lineStyle: { color: palette.grid }, ...(axis.axisLine ?? {}) },
          axisLabel: { color: palette.axis, ...(axis.axisLabel ?? {}) },
          splitLine: { lineStyle: { color: palette.grid }, ...(axis.splitLine ?? {}) }
        }))
      : option.xAxis
        ? (() => {
            const axis = option.xAxis as AxisLike;
            return {
              ...axis,
              axisLine: {
                lineStyle: { color: palette.grid },
                ...axis.axisLine
              },
              axisLabel: {
                color: palette.axis,
                ...axis.axisLabel
              },
              splitLine: {
                lineStyle: { color: palette.grid },
                ...axis.splitLine
              }
            };
          })()
        : undefined,
    yAxis: Array.isArray(option.yAxis)
      ? option.yAxis.map((axis: AxisLike) => ({
          ...axis,
          axisLine: { lineStyle: { color: palette.grid }, ...(axis.axisLine ?? {}) },
          axisLabel: { color: palette.axis, ...(axis.axisLabel ?? {}) },
          splitLine: { lineStyle: { color: palette.grid }, ...(axis.splitLine ?? {}) }
        }))
      : option.yAxis
        ? (() => {
            const axis = option.yAxis as AxisLike;
            return {
              ...axis,
              axisLine: {
                lineStyle: { color: palette.grid },
                ...axis.axisLine
              },
              axisLabel: {
                color: palette.axis,
                ...axis.axisLabel
              },
              splitLine: {
                lineStyle: { color: palette.grid },
                ...axis.splitLine
              }
            };
          })()
        : undefined
  };

  if (Array.isArray(option.series)) {
    nextOption.series = option.series.map((series: LineSeriesOption) => {
      if (series.type !== 'line') {
        return series;
      }

      return {
        ...series,
        smooth: series.smooth ?? true,
        symbolSize: series.symbolSize ?? 6,
        lineStyle: {
          color: palette.line,
          width: 3,
          ...series.lineStyle
        },
        areaStyle: series.areaStyle ?? {
          color: {
            type: 'linear',
            x: 0,
            y: 0,
            x2: 0,
            y2: 1,
            colorStops: [
              { offset: 0, color: palette.areaTop },
              { offset: 1, color: palette.areaBottom }
            ]
          }
        }
      };
    });
  }

  return nextOption;
}

/** 根据用户减弱动画偏好关闭图表动画。 */
export function applyChartMotionPolicy(option: EChartsOption): EChartsOption {
  const reducedMotion = typeof window !== 'undefined'
    && window.matchMedia('(prefers-reduced-motion: reduce)').matches;

  if (!reducedMotion) {
    return option;
  }

  return {
    ...option,
    animation: false,
    series: Array.isArray(option.series)
      ? option.series.map((series: LineSeriesOption) => ({ ...series, animation: false }))
      : option.series
  };
}

export interface TrafficPoint {
  label: string;
  value: number;
}

/** 工作台流量折线图 Option 工厂。 */
export function createTrafficLineOption(
  points: TrafficPoint[],
  yAxisName?: string
): EChartsOption {
  return {
    grid: { left: 12, right: 12, top: 18, bottom: 8, containLabel: true },
    xAxis: {
      type: 'category',
      boundaryGap: false,
      data: points.map(point => point.label)
    },
    yAxis: {
      type: 'value',
      name: yAxisName
    },
    series: [{
      type: 'line',
      data: points.map(point => point.value),
      showSymbol: true
    }]
  };
}
