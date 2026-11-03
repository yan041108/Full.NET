/// API始终返回固定表单投影；原有办理边界用最小真实字段代替缺失的响应属性。
const workflowTestFormSchema = {
  'schemaVersion': 1,
  'adapterVersion': 1,
  'sections': [
    {
      'sectionKey': 'main',
      'fields': [
        {
          'fieldKey': 'document',
          'fieldTypeKey': 'text',
          'required': false,
          'constraints': {'maxLength': 128}
        }
      ]
    }
  ]
};
