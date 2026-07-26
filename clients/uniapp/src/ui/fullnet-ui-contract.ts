export const fullNetUiContract = {
  colors: {
    primary: '#08736d',
    primaryBright: '#42b9a6',
    success: '#20764f',
    warning: '#936109',
    error: '#b83e3e',
    text: '#17212b',
    textMuted: '#596670',
    canvas: '#f3f4ef',
    panel: '#fffefa',
    border: '#dfe4df'
  },
  controlRadiusPx: 12
} as const;

export type FullNetUiContract = typeof fullNetUiContract;
