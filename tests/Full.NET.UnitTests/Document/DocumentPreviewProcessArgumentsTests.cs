using Full.NET.Modules.Document.Configuration;
using Full.NET.Modules.Document.Contracts;
using Full.NET.Modules.Document.Providers.OfficePreview;

namespace Full.NET.UnitTests.Document;

/// <summary>路径替换不能产生额外参数；配置及 MIME 映射与操作系统文件名规则无关。</summary>
[TestClass]
public sealed class DocumentPreviewProcessArgumentsTests
{
    [TestMethod]
    public void Paths_with_spaces_quotes_and_flags_remain_single_argument_values()
    {
        const string input = "/tmp/space path/source\" --unsafe.docx";
        const string output = "/tmp/space path/output.pdf";
        CollectionAssert.AreEqual(new[] { "--input", input, "--output", output },
            DocumentPreviewProcessArguments.Build("--input \"{inputPath}\" --output '{outputPath}'", input, output).ToArray());
    }

    [TestMethod]
    public void Inline_placeholders_and_empty_quoted_arguments_are_preserved()
    {
        CollectionAssert.AreEqual(new[] { "--input=in path", "", "--output=out path" },
            DocumentPreviewProcessArguments.Build("--input={inputPath} \"\" --output={outputPath}", "in path", "out path").ToArray());
    }

    [TestMethod]
    public void Escaped_quotes_and_word_apostrophes_preserve_existing_literal_arguments()
    {
        CollectionAssert.AreEqual(new[] { "--option", "a\"b", "operator's", "input", "output" },
            DocumentPreviewProcessArguments.Build("--option \"a\\\"b\" operator's \"{inputPath}\" \"{outputPath}\"", "input", "output").ToArray());
    }

    [TestMethod]
    public void Windows_path_backslashes_and_escaped_trailing_backslash_are_preserved()
    {
        CollectionAssert.AreEqual(new[] { @"C:\tools\converter", @"C:\working\", "input", "output" },
            DocumentPreviewProcessArguments.Build("C:\\tools\\converter \"C:\\working\\\\\" {inputPath} {outputPath}", "input", "output").ToArray());
    }

    [TestMethod]
    public void Unbalanced_template_is_rejected_by_configuration_before_task_claim()
    {
        var value = new DocumentOfficePreviewConversionOptions
        {
            Enabled = true, ProviderKey = DocumentOfficePreviewConversionProviderKeys.ExternalProcess,
            ExternalProcess = new() { ExecutablePath = "converter", ArgumentTemplate = "\"{inputPath} {outputPath}" },
        };
        Assert.IsTrue(new DocumentOfficePreviewConversionOptionsValidator().Validate(null, value).Failed);
    }

    [TestMethod]
    [DataRow("{inputPath}")]
    [DataRow("{outputPath}")]
    [DataRow("\"{inputPath} {outputPath}")]
    public void Invalid_template_is_rejected_before_process_launch(string template) =>
        Assert.ThrowsExactly<InvalidDataException>(() => DocumentPreviewProcessArguments.Build(template, "input", "output"));

    [TestMethod]
    [DataRow("application/msword", "source.doc")]
    [DataRow("application/vnd.ms-excel", "source.xls")]
    [DataRow("application/vnd.ms-powerpoint", "source.ppt")]
    [DataRow("application/vnd.openxmlformats-officedocument.wordprocessingml.document", "source.docx")]
    [DataRow("application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", "source.xlsx")]
    [DataRow("application/vnd.openxmlformats-officedocument.presentationml.presentation", "source.pptx")]
    public void Input_name_comes_only_from_known_mime(string mime, string expected) =>
        Assert.AreEqual(expected, DocumentPreviewProcessArguments.InputFileName(mime));

    [TestMethod]
    [DataRow(0L)]
    [DataRow(-1L)]
    [DataRow(2147483648L)]
    public void Output_budget_is_validated_in_options(long maximum)
    {
        var value = new DocumentOfficePreviewConversionOptions { MaxOutputBytes = maximum };
        Assert.IsTrue(new DocumentOfficePreviewConversionOptionsValidator().Validate(null, value).Failed);
    }

    [TestMethod]
    public void Default_output_budget_matches_existing_file_upload_default() =>
        Assert.AreEqual(10_485_760L, new DocumentOfficePreviewConversionOptions().MaxOutputBytes);
}
