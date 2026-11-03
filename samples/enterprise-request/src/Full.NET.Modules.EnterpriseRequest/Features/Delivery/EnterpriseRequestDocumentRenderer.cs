using Full.NET.Abstractions.Results;
using Full.NET.Modules.Reporting.Contracts;
using Full.NET.Modules.Printing.Contracts;
using System.Text;

namespace Full.NET.Modules.EnterpriseRequest.Features.Delivery;

/// <summary>应用拥有列和静态布局；官方模块只渲染已授权投影，不能反向读取样例表。</summary>
internal sealed class EnterpriseRequestDocumentRenderer(IReportingWorkbookRenderer workbooks, IPrintingFormRenderer forms)
{
    internal Result<byte[]> Workbook(EnterpriseDeliverySnapshot document, string locale, CancellationToken token)
    {
        var en = locale == "en-US";
        var columns = new List<ReportingExecutionColumnDefinition>
        {
            new("requestNumber", en ? "Request number" : "申请单号"), new("title", en ? "Title" : "标题"),
            new("status", en ? "Status" : "状态"), new("lineNumber", en ? "Line" : "行号"),
            new("description", en ? "Description" : "项目说明"), new("quantity", en ? "Quantity" : "数量")
        };
        if (document.IncludesAmounts) columns.AddRange([new("totalAmount", en ? "Total amount" : "总金额"),
            new("unitPrice", en ? "Unit price" : "单价"), new("lineAmount", en ? "Line amount" : "行金额")]);
        var rows = document.Lines.Count == 0 ? new[] { Row(null) } : document.Lines.Select(line => Row(line)).ToArray();
        return workbooks.Render(columns, rows, token);

        ReportingExecutionRow Row(EnterpriseDeliveryLine? line)
        {
            var values = new Dictionary<string, string?>
            {
                ["requestNumber"] = document.RequestNumber, ["title"] = document.Title, ["status"] = document.Status,
                ["lineNumber"] = line?.LineNumber.ToString(System.Globalization.CultureInfo.InvariantCulture),
                ["description"] = line?.Description, ["quantity"] = line?.Quantity
            };
            if (document.IncludesAmounts)
            { values["totalAmount"] = document.TotalAmount; values["unitPrice"] = line?.UnitPrice; values["lineAmount"] = line?.LineAmount; }
            return new(values);
        }
    }

    internal Result<string> Print(EnterpriseDeliverySnapshot document, string locale, CancellationToken token)
    {
        if (document.Lines.Count > 20) return Result<string>.Failure(new Error("enterprise_request.print_limit_exceeded",
            "Print supports at most 20 lines. Use the workbook for larger documents.", ErrorType.Validation));
        var en = locale == "en-US";
        var fields = new Dictionary<string, string?> { ["number"] = document.RequestNumber, ["title"] = document.Title, ["status"] = document.Status };
        var layout = new StringBuilder("<h1>{{title}}</h1><p>{{number}}</p><p>{{status}}</p>");
        if (document.IncludesAmounts) { fields["total"] = document.TotalAmount; layout.Append(en ? "<p>Total: {{total}}</p>" : "<p>总金额：{{total}}</p>"); }
        layout.Append(en ? "<table><thead><tr><th>Line</th><th>Description</th><th>Quantity</th>" : "<table><thead><tr><th>行号</th><th>项目说明</th><th>数量</th>");
        if (document.IncludesAmounts) layout.Append(en ? "<th>Unit price</th><th>Amount</th>" : "<th>单价</th><th>行金额</th>");
        layout.Append("</tr></thead><tbody>");
        for (var i = 0; i < document.Lines.Count; i++)
        {
            token.ThrowIfCancellationRequested();
            var row = document.Lines[i];
            fields[$"n{i}"] = row.LineNumber.ToString(System.Globalization.CultureInfo.InvariantCulture);
            fields[$"d{i}"] = row.Description; fields[$"q{i}"] = row.Quantity;
            layout.Append($"<tr><td>{{{{n{i}}}}}</td><td>{{{{d{i}}}}}</td><td>{{{{q{i}}}}}</td>");
            if (document.IncludesAmounts)
            { fields[$"u{i}"] = row.UnitPrice; fields[$"a{i}"] = row.LineAmount; layout.Append($"<td>{{{{u{i}}}}}</td><td>{{{{a{i}}}}}</td>"); }
            layout.Append("</tr>");
        }
        layout.Append("</tbody></table>");
        return forms.Render(layout.ToString(), fields, token);
    }
}
