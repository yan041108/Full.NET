using Full.NET.Modules.Ocr.Domain;

namespace Full.NET.UnitTests.Ocr;

[TestClass]
public sealed class OcrIdCardResponseParserTests
{
    [TestMethod]
    public void TryParse_reads_snake_case_payload()
    {
        const string json = """
            {
              "data": {
                "name": "张三",
                "id_number": "110101199001011234",
                "gender": "男",
                "nation": "汉",
                "address": "北京市东城区",
                "birth_date": "1990-01-01"
              }
            }
            """;

        var parsed = OcrIdCardResponseParser.TryParse(json, out var result, out var message);

        Assert.IsTrue(parsed);
        Assert.AreEqual("Recognition parsed successfully.", message);
        Assert.AreEqual("张三", result.Name);
        Assert.AreEqual("110101199001011234", result.IdNumber);
        Assert.AreEqual("男", result.Gender);
        Assert.AreEqual("汉", result.Nation);
        Assert.AreEqual("北京市东城区", result.Address);
        Assert.AreEqual("1990-01-01", result.BirthDate);
    }

    [TestMethod]
    public void TryParse_fails_when_required_fields_missing()
    {
        const string json = """{ "data": { "name": "张三" } }""";

        var parsed = OcrIdCardResponseParser.TryParse(json, out _, out var message);

        Assert.IsFalse(parsed);
        Assert.AreEqual("OCR response is missing name or id number.", message);
    }
}
