# Office 文档预览转换配置

Host 文档预览任务由 Worker 调用可插拔 Provider 转为 PDF；API 负责入队与读取，不在请求线程运行转换器。默认 `disabled`，启用真实转换器前需单独核对部署方式、字体与许可证。

配置节为 `Document:OfficePreviewConversion`：

```json
{
  "Document": {
    "OfficePreviewConversion": {
      "Enabled": true,
      "ProviderKey": "external_http",
      "MaxInputBytes": 52428800,
      "MaxOutputBytes": 10485760,
      "TimeoutSeconds": 120,
      "PollSeconds": 15,
      "BatchSize": 5,
      "ExternalHttp": {
        "BaseUrl": "https://converter.example/office-to-pdf"
      }
    }
  }
}
```

`MaxInputBytes` 按声明长度预检并按实际读取字节限制输入。`MaxOutputBytes` 同样限制声明及实际 PDF 大小，默认 10 MiB，与 Files 默认上传预算一致；范围为 1 到 `Int32.MaxValue`。Files 自身的类型、大小、扫描与就绪要求仍然执行，调整转换预算时需一起核对 Files 配置。Worker 包装自定义 Provider 的输入和上传输出，不能仅通过低报长度绕过预算。

`external_http` 接受 `multipart/form-data` 的 `file` 字段，成功返回 PDF 字节流。`ExternalHttp:ApiKey` 可由秘密配置提供，作为 `X-Api-Key` 发送，不应写入仓库。官方 Provider 的 `TimeoutSeconds` 覆盖上传、响应头及完整正文读取；返回独立拥有的有界 PDF 流，响应释放后仍能读取。空正文和已知转换失败使用 `document.office_preview.conversion_failed`，输入、输出超限分别使用 `document.office_preview.input_too_large` 与 `document.office_preview.output_too_large`。

`external_process` 使用运维配置的 `ExternalProcess:ExecutablePath` 和 `ExternalProcess:ArgumentTemplate`。模板必须含 `{inputPath}`、`{outputPath}`，缺失或引号未闭合时配置校验失败。先对可信模板分词，再替换路径并写入 `ProcessStartInfo.ArgumentList`，禁止把上传文件名拼成命令。输入文件名只由已知 Office MIME 映射为 `source.doc/docx/xls/xlsx/ppt/pptx`；输出固定为本任务目录的 `output.pdf`。

模板语法统一为：空白分词、双引号分组、双引号前反斜杠的奇偶转义规则；词首单引号可分组，单词内部撇号保留字面值。Windows 路径的普通反斜杠保持不变。示例：`--input "{inputPath}" --output "{outputPath}"`；参数中包含字面双引号时使用 `\"`。模板不经过 shell，不支持管道、重定向或环境变量展开。升级前核对既有模板；不要依赖平台原始命令行的未列出语法。

进程必须以前台方式完成转换，退出前生成最终输出并回收自身后代；禁止父进程提前退出后交给分离的后台进程。Provider 同时排空 stdout/stderr，不保存供应商输出；业务取消或超时尝试回收仍在途的自有进程树，并独立关闭管道及观察读取任务。操作系统拒绝回收或不遵守前台契约的转换器不属于受保证的生命周期范围。

输入流为借用流，Provider 不释放；成功输出由 Worker 释放。调用方取消向上传播，正常 Worker 停机不改写为普通转换失败。当前任务领取和排队取消的生产代码变化、Provider 生命周期与预算变化仍待本地集中编译、测试、双库及独立应用验收；本页描述实现契约，不作为验收通过证据。Worker 崩溃后的 processing 任务自动恢复不属于这些变化的已交付能力。
