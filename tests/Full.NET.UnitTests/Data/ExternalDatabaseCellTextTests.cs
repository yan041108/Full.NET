using System.Globalization;
using Full.NET.Data.Dapper;

namespace Full.NET.UnitTests.Data;

[TestClass]
public sealed class ExternalDatabaseCellTextTests
{
    [TestMethod]
    [DataRow(0, "")]
    [DataRow(1, "a")]
    [DataRow(2, "a")]
    [DataRow(3, "a😀")]
    [DataRow(4, "a😀b")]
    [DataRow(5, "a😀b")]
    [DataRow(6, "a😀b𠮷")]
    [DataRow(20, "a😀b𠮷")]
    public void Truncation_keeps_complete_surrogate_pairs(int maximum, string expected)
    {
        Assert.AreEqual(expected, ExternalDatabaseCellText.Format("a😀b𠮷", maximum));
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public void Database_null_and_null_stay_null(bool databaseNull)
    {
        Assert.IsNull(ExternalDatabaseCellText.Format(databaseNull ? DBNull.Value : null, 2));
    }

    [TestMethod]
    public void Existing_invariant_scalar_formats_are_preserved()
    {
        var old = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("fr-FR");
            Assert.AreEqual("12.5", ExternalDatabaseCellText.Format(12.5m, 100));
            var date = DateTimeOffset.Parse("2026-10-15T00:00:00Z", CultureInfo.InvariantCulture);
            Assert.AreEqual(date.ToString("O", CultureInfo.InvariantCulture), ExternalDatabaseCellText.Format(date, 100));
            Assert.AreEqual("0198fa17-3233-7f21-80a0-123456789abc", ExternalDatabaseCellText.Format(Guid.Parse("0198fa17-3233-7f21-80a0-123456789abc"), 100));
        }
        finally { CultureInfo.CurrentCulture = old; }
    }
}
