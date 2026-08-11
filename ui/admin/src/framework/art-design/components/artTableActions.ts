/** 操作列默认可直接展示的图标按钮数量。 */
export const ART_TABLE_ACTION_MAX_VISIBLE = 4;

/** 单个图标操作按钮尺寸（px），与 `ArtTableActionButton` 样式一致。 */
export const ART_TABLE_ACTION_BUTTON_SIZE = 32;

/** 操作按钮间距（px）。 */
export const ART_TABLE_ACTION_BUTTON_GAP = 4;

/**
 * 标准图标操作列默认宽度（px）。
 * 计算：4 个按钮 + 「更多」按钮 + 内边距余量。
 */
export const ART_TABLE_ACTION_COLUMN_WIDTH =
  ART_TABLE_ACTION_MAX_VISIBLE * ART_TABLE_ACTION_BUTTON_SIZE
  + (ART_TABLE_ACTION_MAX_VISIBLE - 1) * ART_TABLE_ACTION_BUTTON_GAP
  + ART_TABLE_ACTION_BUTTON_SIZE
  + ART_TABLE_ACTION_BUTTON_GAP
  + 16;
