import { describe, expect, it } from 'vitest';
import {
  moveHorizontalTabScrollToTarget,
  revealElementInScrollContainer,
  SHELL_TAB_TAG_SPACING_PX
} from './shellScrollIntoView';

function mockRect(top: number, left: number, width: number, height: number): DOMRect {
  return {
    top,
    left,
    right: left + width,
    bottom: top + height,
    width,
    height,
    x: left,
    y: top,
    toJSON: () => ({})
  } as DOMRect;
}

describe('moveHorizontalTabScrollToTarget', () => {
  it('首项滚到最左', () => {
    const scroll = document.createElement('div');
    Object.defineProperty(scroll, 'clientWidth', { value: 100 });
    Object.defineProperty(scroll, 'scrollWidth', { value: 300 });
    scroll.scrollLeft = 50;

    const tabs = [
      { offsetLeft: 0, offsetWidth: 40 } as HTMLElement,
      { offsetLeft: 50, offsetWidth: 40 } as HTMLElement
    ];

    moveHorizontalTabScrollToTarget(scroll, tabs, 0);
    expect(scroll.scrollLeft).toBe(0);
  });

  it('末项滚到最右', () => {
    const scroll = document.createElement('div');
    Object.defineProperty(scroll, 'clientWidth', { value: 100 });
    Object.defineProperty(scroll, 'scrollWidth', { value: 300 });
    scroll.scrollLeft = 0;

    const tabs = [
      { offsetLeft: 0, offsetWidth: 40 } as HTMLElement,
      { offsetLeft: 200, offsetWidth: 80 } as HTMLElement
    ];

    moveHorizontalTabScrollToTarget(scroll, tabs, 1);
    expect(scroll.scrollLeft).toBe(200);
  });

  it('中间项在右侧不可见时向左滚', () => {
    const scroll = document.createElement('div');
    Object.defineProperty(scroll, 'clientWidth', { value: 100 });
    Object.defineProperty(scroll, 'scrollWidth', { value: 400 });
    scroll.scrollLeft = 0;

    const tabs = [
      { offsetLeft: 0, offsetWidth: 30 } as HTMLElement,
      { offsetLeft: 40, offsetWidth: 30 } as HTMLElement,
      { offsetLeft: 180, offsetWidth: 30 } as HTMLElement
    ];

    moveHorizontalTabScrollToTarget(scroll, tabs, 1);
    const next = tabs[2];
    const expected =
      next.offsetLeft + next.offsetWidth + SHELL_TAB_TAG_SPACING_PX - scroll.clientWidth;
    expect(scroll.scrollLeft).toBe(expected);
  });
});

describe('revealElementInScrollContainer', () => {
  it('纵向将低于视口的目标滚入', () => {
    const scroll = document.createElement('div');
    scroll.scrollTop = 0;
    scroll.getBoundingClientRect = () => mockRect(100, 0, 200, 400);
    const target = document.createElement('div');
    target.getBoundingClientRect = () => mockRect(520, 0, 200, 32);

    revealElementInScrollContainer(scroll, target, 'vertical');
    expect(scroll.scrollTop).toBe(52);
  });
});
