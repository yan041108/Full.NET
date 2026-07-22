using System.Data;
using Full.NET.Data.Dapper;
using Microsoft.Data.SqlClient;

namespace Full.NET.UnitTests.Data;

[TestClass]
public sealed class AssignedGuidTypeHandlerTests
{
    [TestMethod]
    public void SetValue_declares_guid_database_type()
    {
        var parameter = new SqlParameter
        {
            DbType = DbType.Object,
        };
        var value = Guid.NewGuid();

        new AssignedGuidTypeHandler().SetValue(parameter, value);

        Assert.AreEqual(DbType.Guid, parameter.DbType);
        Assert.AreEqual(value, parameter.Value);
    }
}
