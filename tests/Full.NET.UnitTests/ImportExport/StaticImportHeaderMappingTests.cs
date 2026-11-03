using Full.NET.Modules.ImportExport.Contracts;
using Full.NET.Modules.ImportExport.Domain;

namespace Full.NET.UnitTests.ImportExport;

/// <summary>字面映射必须完整、有限且可冻结，不能靠文件位置回退改变字段含义。</summary>
[TestClass]
public sealed class StaticImportHeaderMappingTests
{
    private static readonly string[] Headers = ["code", "name"];
    private static readonly StaticImportSchemaDefinition Definition = new("source", "Source", "tenant", "source.import", [new("rows", "Rows", Headers)]);
    private static ImportColumnMappingRequest Mapping() => new(Headers, [new("name", " 名称 "), new("code", "编号")]);

    [TestMethod]
    public void Mapping_reorders_literal_headers_and_freezes_independent_input()
    {
        Assert.IsTrue(StaticImportHeaderMapping.TryNormalize(Headers, Mapping(), out var normalized));
        Assert.AreEqual("code", normalized.Columns[0].TargetColumn);
        CollectionAssert.AreEqual(new[] { 2, 0 }, StaticImportHeaderMapping.Bind(Headers, ["名称", "ignored", "编号"], normalized).ToArray());
        var captured = ImportColumnMappingSnapshots.Capture(Definition, "rows", normalized);
        Assert.IsTrue(captured.IsSuccess);
        var loaded = ImportColumnMappingSnapshots.Read(Definition, "rows", captured.Value!.Json);
        Assert.IsTrue(loaded.IsSuccess);
        Assert.AreEqual("编号", loaded.Value!.Configuration!.Columns[0].SourceHeader);
        Assert.IsNull(ImportColumnMappingSnapshots.Capture(Definition, "rows", null).Value!.Json);
    }

    [TestMethod]
    public void Missing_duplicate_changed_or_unbounded_mapping_is_rejected()
    {
        Assert.IsFalse(StaticImportHeaderMapping.TryNormalize(Headers, new(Headers, [new("code", "x"), new("name", " x ")]), out _));
        Assert.IsFalse(StaticImportHeaderMapping.TryNormalize(Headers, new(Headers, [new("code", "x"), new("code", "y")]), out _));
        Assert.IsFalse(StaticImportHeaderMapping.TryNormalize(Headers, new(Headers, [new("code", "x"), new("name", new('x', 129))]), out _));
        Assert.IsFalse(StaticImportHeaderMapping.TryNormalize(Headers, new(["name", "code"], Mapping().Columns), out _));
        Assert.ThrowsExactly<StaticImportHeaderMappingException>(() => StaticImportHeaderMapping.Bind(Headers, ["名称", "名称", "编号"], Mapping()));
        Assert.ThrowsExactly<StaticImportHeaderMappingException>(() => StaticImportHeaderMapping.Bind(Headers, ["名称", "编号", "ex\ntra"], Mapping()));
        Assert.ThrowsExactly<StaticImportHeaderMappingException>(() => StaticImportHeaderMapping.Bind(Headers, ["名称", "编号", new string('x', 129)], Mapping()));
        Assert.ThrowsExactly<StaticImportHeaderMappingException>(() => StaticImportHeaderMapping.Bind(Headers, ["名称", "missing"], Mapping()));
        Assert.ThrowsExactly<StaticImportHeaderMappingException>(() => StaticImportHeaderMapping.Bind(Headers, Enumerable.Range(0, 33).Select(i => i.ToString()).ToArray(), Mapping()));
    }

    [TestMethod]
    public void Corrupted_or_stale_snapshot_never_falls_back_to_canonical_template()
    {
        foreach (var json in new[] { "", "null", "{}", "{\"configuration\":null}", new string('x', 17000) })
            Assert.IsFalse(ImportColumnMappingSnapshots.Read(Definition, "rows", json).IsSuccess);
        var snapshot = ImportColumnMappingSnapshots.Capture(Definition, "rows", Mapping()).Value!.Json;
        var changed = Definition with { Worksheets = [new("rows", "Rows", ["name", "code"])] };
        Assert.AreEqual(ImportColumnMappingErrorCodes.SchemaChanged, ImportColumnMappingSnapshots.Read(changed, "rows", snapshot).Error!.Code);
        Assert.AreNotEqual(StaticImportHeaderMapping.Fingerprint(Headers), StaticImportHeaderMapping.Fingerprint(["name", "code"]));
    }
}
