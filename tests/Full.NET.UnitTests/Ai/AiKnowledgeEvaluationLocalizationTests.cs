using System.Globalization;
using Full.NET.Hosting.Api;
using Full.NET.Modules.Ai.Resources;

namespace Full.NET.UnitTests.Ai;

/// <summary>两个完整错误码前缀都必须读取编译资源，不能在中文请求中回退内部英文说明。</summary>
[TestClass]
public sealed class AiKnowledgeEvaluationLocalizationTests
{
    [TestMethod]
    [DataRow(false,"input_invalid")][DataRow(false,"not_found")][DataRow(false,"version_conflict")][DataRow(false,"source_unavailable")]
    [DataRow(true,"input_invalid")][DataRow(true,"not_found")][DataRow(true,"version_conflict")][DataRow(true,"source_unavailable")]
    public void Every_machine_code_has_distinct_compiled_chinese_and_english_resources(bool run,string suffix)
    {
        IErrorResourceSource source=run?new AiKnowledgeEvaluationRunErrorResourceSource():new AiKnowledgeEvaluationSetErrorResourceSource();
        var code=source.Prefix+suffix;
        Assert.IsTrue(source.TryGetTemplate(code,CultureInfo.GetCultureInfo("zh-CN"),out var chinese));
        Assert.IsTrue(source.TryGetTemplate(code,CultureInfo.GetCultureInfo("en-US"),out var english));
        Assert.IsTrue(chinese.Any(value=>value is >= '\u4e00' and <= '\u9fff'));Assert.AreNotEqual(chinese,english);
        Assert.IsFalse(english.Any(value=>value is >= '\u4e00' and <= '\u9fff'));
    }
}
