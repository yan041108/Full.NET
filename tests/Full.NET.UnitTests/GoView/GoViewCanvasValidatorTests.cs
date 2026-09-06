using Full.NET.Modules.GoView.Domain;

namespace Full.NET.UnitTests.GoView;

[TestClass]
public sealed class GoViewCanvasValidatorTests
{
    [TestMethod]
    public void Validate_accepts_default_canvas_json()
    {
        var result = GoViewCanvasValidator.Validate(GoViewCanvasPolicy.DefaultCanvasJson);
        Assert.IsTrue(result.IsSuccess);
    }

    [TestMethod]
    public void Validate_rejects_invalid_json()
    {
        var result = GoViewCanvasValidator.Validate("{not-json");
        Assert.IsFalse(result.IsSuccess);
    }

    [TestMethod]
    public void Validate_rejects_missing_components_array()
    {
        var result = GoViewCanvasValidator.Validate("""{"width":1920,"height":1080}""");
        Assert.IsFalse(result.IsSuccess);
    }

    [TestMethod]
    public void NormalizeOptional_returns_default_when_blank()
    {
        Assert.AreEqual(
            GoViewCanvasPolicy.DefaultCanvasJson,
            GoViewCanvasValidator.NormalizeOptional(null));
    }
}
