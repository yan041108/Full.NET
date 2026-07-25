import { beforeEach, describe, expect, it, vi } from 'vitest';
import { flushPromises, mount } from '@vue/test-utils';
import { readFileSync } from 'node:fs';
import path from 'node:path';
import { fileURLToPath } from 'node:url';
import FullNetChart from './FullNetChart.vue';
import { createTrafficLineOption } from './fullNetChartTheme';

const chartMocks = vi.hoisted(() => ({
  createFullNetChart: vi.fn(() => ({
    setOption: vi.fn(),
    resize: vi.fn(),
    dispose: vi.fn()
  })),
  updateFullNetChart: vi.fn(),
  resizeFullNetChart: vi.fn(),
  disposeFullNetChart: vi.fn()
}));

vi.mock('./echarts', () => chartMocks);

describe('FullNetChart', () => {
  beforeEach(() => {
    class ResizeObserverStub {
      observe(): void {}
      disconnect(): void {}
    }

    vi.stubGlobal('ResizeObserver', ResizeObserverStub);
  });

  it('空数据时展示空状态而不初始化图表', async () => {
    chartMocks.createFullNetChart.mockClear();
    const wrapper = mount(FullNetChart, {
      props: {
        option: createTrafficLineOption([]),
        ariaLabel: 'Traffic',
        emptyLabel: 'No data'
      }
    });

    await flushPromises();
    expect(wrapper.text()).toContain('No data');
    expect(wrapper.find('[data-testid="fullnet-chart-canvas"]').exists()).toBe(false);
    expect(chartMocks.createFullNetChart).not.toHaveBeenCalled();
  });

  it('有数据时渲染图表容器并提供表格摘要', async () => {
    const wrapper = mount(FullNetChart, {
      props: {
        option: createTrafficLineOption([
          { label: '00:00', value: 12 },
          { label: '03:00', value: 34 }
        ]),
        ariaLabel: 'Traffic trend',
        emptyLabel: 'No data',
        summary: 'Last 12 hours'
      }
    });

    await flushPromises();
    expect(wrapper.find('[data-testid="fullnet-chart-canvas"]').exists()).toBe(true);
    expect(wrapper.find('table').exists()).toBe(true);
    expect(wrapper.text()).toContain('Last 12 hours');
  });
});

describe('echarts 模块化入口', () => {
  it('禁止从完整 echarts 包副作用导入', () => {
    const filePath = path.resolve(
      path.dirname(fileURLToPath(import.meta.url)),
      'echarts.ts'
    );
    const source = readFileSync(filePath, 'utf8');
    expect(source).toContain("from 'echarts/core'");
    expect(source).not.toMatch(/from 'echarts'/);
  });
});
