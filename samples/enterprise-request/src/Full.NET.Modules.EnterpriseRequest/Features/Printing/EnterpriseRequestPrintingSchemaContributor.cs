using Full.NET.Modules.Printing.Contracts;

namespace Full.NET.Modules.EnterpriseRequest.Features.Printing;

/// <summary>样例拥有的申请摘要表单；字段固定，不接受客户端自定义查询。</summary>
internal sealed class EnterpriseRequestPrintingSchemaContributor : IPrintingFormSchemaContributor
{
    internal const string SchemaKey = "enterprise_request.request_summary";
    /// <summary>获取申请摘要固定字段；申请行与审批历史不在本表单范围内。</summary>
    public IReadOnlyList<PrintingFormSchemaDefinition> Schemas { get; } =
    [new(SchemaKey, "企业申请摘要", "打印当前租户中有权读取的企业申请摘要。",
        [new("requestNumber", "申请编号"), new("title", "标题"), new("status", "状态"), new("totalAmount", "金额"),
         new("printedByDisplayName", "打印人"), new("printedAtUtc", "打印时间")], true)];
}
