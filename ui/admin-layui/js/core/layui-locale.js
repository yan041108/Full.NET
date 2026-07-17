const componentMessages = {
  'zh-CN': {
    table: { empty: '暂无数据' },
    laypage: { prev: '上一页', next: '下一页', total: '共 {total} 条' },
    laydate: { confirm: '确定', clear: '清空', now: '现在' },
    layer: { confirm: '确定', cancel: '取消' },
    form: { required: '必填项不能为空' },
    upload: { choose: '选择文件', retry: '重试' }
  },
  en: {
    table: {
      sort: { asc: 'Ascending', desc: 'Descending' },
      noData: 'No data',
      tools: {
        filter: { title: 'Filter columns' },
        export: {
          title: 'Export',
          noDataPrompt: 'There is no data to export',
          compatPrompt: 'Export is not supported by this browser',
          csvText: 'Export CSV'
        },
        print: { title: 'Print', noDataPrompt: 'There is no data to print' }
      },
      dataFormatError: 'The response does not match the table contract',
      xhrError: 'Request failed: {msg}'
    },
    laypage: {
      prev: 'Previous', next: 'Next', first: 'First', last: 'Last',
      total: '{total} items', pagesize: 'items/page', goto: 'Go to',
      page: 'page', confirm: 'Confirm'
    },
    laydate: {
      months: [
        'January', 'February', 'March', 'April', 'May', 'June',
        'July', 'August', 'September', 'October', 'November', 'December'
      ],
      weeks: ['Sun', 'Mon', 'Tue', 'Wed', 'Thu', 'Fri', 'Sat'],
      time: ['Hour', 'Minute', 'Second'],
      literal: { year: '' },
      selectDate: 'Select date', selectTime: 'Select time',
      startTime: 'Start time', endTime: 'End time',
      tools: { confirm: 'Confirm', clear: 'Clear', now: 'Now', reset: 'Reset' },
      rangeOrderPrompt: 'The end time must not precede the start time',
      invalidDatePrompt: 'The date or time is outside the valid range',
      formatErrorPrompt: 'The date must match {format}',
      autoResetPrompt: 'The value was reset',
      preview: 'Current selection'
    },
    layer: {
      confirm: 'Confirm', cancel: 'Cancel', defaultTitle: 'Information',
      prompt: { inputLengthPrompt: 'Enter no more than {length} characters' },
      photos: {
        noData: 'No images',
        tools: {
          rotate: 'Rotate', scaleX: 'Flip horizontally', zoomIn: 'Zoom in',
          zoomOut: 'Zoom out', reset: 'Reset', close: 'Close'
        },
        viewPicture: 'View original',
        urlError: {
          prompt: 'This image is unavailable. Continue to the next image?',
          confirm: 'Next', cancel: 'Close'
        }
      }
    },
    form: {
      select: {
        noData: 'No data', noMatch: 'No matching data',
        placeholder: 'Please select'
      },
      validateMessages: {
        required: 'This field is required', phone: 'Invalid phone number',
        email: 'Invalid email address', url: 'Invalid URL',
        number: 'Enter numbers only', date: 'Invalid date',
        identity: 'Invalid identity number'
      },
      verifyErrorPromptTitle: 'Validation'
    },
    upload: {
      fileType: { file: 'file', image: 'image', video: 'video', audio: 'audio' },
      validateMessages: {
        fileExtensionError: 'One or more {fileType} formats are not supported',
        filesOverLengthLimit: 'Select no more than {length} files',
        currentFilesLength: '{length} files are currently selected',
        fileOverSizeLimit: 'The file size must not exceed {size}'
      },
      chooseText: '{length} files'
    }
  }
};

/**
 * 只使用 Layui 2.13.8 公开 i18n.set；缺少全局对象时保持原生管理端可用。
 */
export function applyLayuiLocale(layui, locale) {
  if (typeof layui?.i18n?.set !== 'function') {
    return false;
  }

  layui.i18n.set({
    locale: locale === 'en-US' ? 'en' : 'zh-CN',
    messages: componentMessages
  });
  return true;
}
