using Full.NET.Host.Worker;

namespace Full.NET.UnitTests.Outbox;

[TestClass]
public sealed class OutboxVersionRetirementCommandLineTests
{
    [TestMethod]
    public void Valid_retirement_arguments_are_parsed_and_removed_from_host_arguments()
    {
        var options = OutboxVersionRetirementCommandLine.Parse(
            [
                "--environment",
                "Production",
                "--outbox-version-retirement-message-type",
                "fullnet.tenancy.tenant.provisioned",
                "--outbox-version-retirement-schema-version",
                "1"
            ]);

        Assert.AreEqual(
            new OutboxVersionRetirementRequest(
                "fullnet.tenancy.tenant.provisioned",
                1),
            options.VersionRetirement);
        CollectionAssert.AreEqual(
            new[] { "--environment", "Production" },
            options.HostArguments.ToArray());
    }

    [TestMethod]
    public void Incomplete_or_invalid_retirement_arguments_are_rejected()
    {
        string[][] invalidArguments =
        [
            ["--outbox-version-retirement-message-type", "fullnet.test"],
            ["--outbox-version-retirement-schema-version", "1"],
            [
                "--outbox-version-retirement-message-type",
                "",
                "--outbox-version-retirement-schema-version",
                "1"
            ],
            [
                "--outbox-version-retirement-message-type",
                "fullnet.test",
                "--outbox-version-retirement-schema-version",
                "0"
            ],
            [
                "--outbox-version-retirement-message-type",
                "fullnet.test",
                "--outbox-version-retirement-schema-version",
                "abc"
            ],
            [
                "--outbox-version-retirement-message-type",
                "fullnet.test",
                "--outbox-version-retirement-message-type",
                "fullnet.test",
                "--outbox-version-retirement-schema-version",
                "1"
            ]
        ];

        foreach (var arguments in invalidArguments)
        {
            var exception = Assert.ThrowsExactly<OutboxVersionRetirementException>(
                () => OutboxVersionRetirementCommandLine.Parse(arguments));

            Assert.AreEqual(
                OutboxVersionRetirementErrorCodes.CommandInvalid,
                exception.Code);
        }
    }
}
