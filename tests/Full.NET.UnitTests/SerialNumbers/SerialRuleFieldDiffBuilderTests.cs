using Full.NET.Modules.SerialNumbers.Contracts;
using Full.NET.Modules.SerialNumbers.Features.DataApprovalBridge;

namespace Full.NET.UnitTests.SerialNumbers;

[TestClass]
public sealed class SerialRuleFieldDiffBuilderTests
{
    [TestMethod]
    public void Build_marks_changed_fields_only()
    {
        var before = """
            {
              "displayName": "Old",
              "description": null,
              "scope": 1,
              "resetInterval": 1,
              "pattern": "INV-{sequence:5}",
              "minimumValue": 1,
              "maximumValue": 100,
              "displayOrder": 10,
              "isEnabled": true,
              "version": 1
            }
            """;
        var proposed = new UpdateSerialNumberRuleRequest(
            "New",
            null,
            SerialNumberRuleScope.Tenant,
            SerialNumberResetInterval.Day,
            "INV-{sequence:5}",
            1,
            100,
            10,
            true,
            1);

        var changes = SerialRuleFieldDiffBuilder.Build(before, proposed);
        Assert.IsTrue(SerialRuleFieldDiffBuilder.HasChanges(changes));
        Assert.IsTrue(changes.Single(change => change.FieldKey == "displayName").Changed);
        Assert.IsFalse(changes.Single(change => change.FieldKey == "pattern").Changed);
    }

    [TestMethod]
    public void HasChanges_returns_false_when_snapshot_matches()
    {
        var json = """
            {
              "displayName": "Same",
              "description": null,
              "scope": 0,
              "resetInterval": 0,
              "pattern": "A",
              "minimumValue": 1,
              "maximumValue": 2,
              "displayOrder": 1,
              "isEnabled": false,
              "version": 2
            }
            """;
        var proposed = new UpdateSerialNumberRuleRequest(
            "Same",
            null,
            SerialNumberRuleScope.Host,
            SerialNumberResetInterval.Never,
            "A",
            1,
            2,
            1,
            false,
            2);
        var changes = SerialRuleFieldDiffBuilder.Build(json, proposed);
        Assert.IsFalse(SerialRuleFieldDiffBuilder.HasChanges(changes));
    }
}
