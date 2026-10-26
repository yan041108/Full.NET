export interface VForm3DesignerHostInstance {
  getFormJson: () => Promise<unknown>;
  setFormJson: (value: unknown) => Promise<void>;
  render: (value: { json: unknown; data: unknown }) => Promise<Record<string, unknown>>;
  getFormData: () => Promise<unknown>;
}
