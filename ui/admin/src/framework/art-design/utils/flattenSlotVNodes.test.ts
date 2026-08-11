import { describe, expect, it } from 'vitest';
import { Comment, Fragment, h } from 'vue';
import { flattenSlotVNodes } from './flattenSlotVNodes';

describe('flattenSlotVNodes', () => {
  it('忽略注释与空节点', () => {
    expect(flattenSlotVNodes([
      h('span', 'a'),
      h(Comment, 'ignored'),
      null,
      false,
      '',
      h(Fragment, [h('span', 'b')])
    ])).toHaveLength(2);
  });
});
