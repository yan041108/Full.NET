export interface GoViewDisplayComponent { key: number; text: string; x: number; y: number; width: number; height: number; color: string; fontSize: number }
export interface GoViewDisplayCanvas { width: number; height: number; backgroundColor: string; components: GoViewDisplayComponent[] }
const record = (value: unknown): value is Record<string, unknown> => !!value && typeof value === 'object' && !Array.isArray(value);
const number = (value: unknown, fallback: number, min: number, max: number) => typeof value === 'number' && Number.isFinite(value) ? Math.max(min, Math.min(max, value)) : fallback;
const color = (value: unknown, fallback: string) => typeof value === 'string' && /^(?:#[a-f\d]{3,8}|[a-z]{1,32}|(?:rgba?|hsla?)\([\d.,%+\-\s]+\))$/iu.test(value) ? value : fallback;
/** 只生成静态文本和受限样式；不解释数据源、组件脚本、HTML 或 URL。 */
export function displayGoViewCanvas(text: string): GoViewDisplayCanvas | undefined {
  if (text.length > 2 * 1024 * 1024) return;
  try {
    const value: unknown = JSON.parse(text); if (!record(value) || !Array.isArray(value.components) || value.components.length > 5000) return;
    return { width: number(value.width, 1920, 1, 16384), height: number(value.height, 1080, 1, 16384), backgroundColor: color(value.backgroundColor, '#0a1628'), components: value.components.map((item: unknown, key: number) => {
      const row = record(item) ? item : {}, props = record(row.props) ? row.props : {};
      return { key, text: typeof props.text === 'string' && props.text ? props.text : typeof row.type === 'string' ? row.type : 'component', x: number(row.x, 0, -16384, 16384), y: number(row.y, 0, -16384, 16384), width: number(row.width, 120, 0, 16384), height: number(row.height, 40, 0, 16384), fontSize: number(props.fontSize, 16, 1, 512), color: color(props.color, '#ffffff') };
    }) };
  } catch { return; }
}
