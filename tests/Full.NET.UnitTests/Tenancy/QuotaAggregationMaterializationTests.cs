using System.Data;
using Dapper;
using Full.NET.Modules.Identity.HostUsers;
using Full.NET.Modules.Files.Features.TenantResourceFiles;
using Full.NET.Modules.Tenancy.Features.ReserveTenantQuota.Persistence;

namespace Full.NET.UnitTests.Tenancy;

/// <summary>真实 Dapper 映射覆盖双库聚合/UNION 返回的整数与 decimal 类型，避免替身掩盖构造器限制。</summary>
[TestClass]
public sealed class QuotaAggregationMaterializationTests
{
    [TestMethod]
    [DataRow(typeof(int))]
    [DataRow(typeof(long))]
    public void Member_count_reads_provider_integer_types(Type valueType)
    {
        using var table = Aggregate(valueType);
        using var reader = table.CreateDataReader();
        var parse = reader.GetRowParser<TenantActiveMemberCountRow>();
        Assert.IsTrue(reader.Read());
        Assert.AreEqual(7L, parse(reader).UsageValue);
    }

    [TestMethod]
    [DataRow(typeof(decimal))]
    [DataRow(typeof(long))]
    public void Storage_sum_reads_provider_numeric_types(Type valueType)
    {
        using var table = Aggregate(valueType);
        using var reader = table.CreateDataReader();
        var parse = reader.GetRowParser<TenantStorageUsageRow>();
        Assert.IsTrue(reader.Read());
        Assert.AreEqual(7L, parse(reader).UsageValue);
    }

    [TestMethod]
    [DataRow(typeof(int))]
    [DataRow(typeof(long))]
    public void Baseline_union_reads_missing_id_and_provider_version_types(Type versionType)
    {
        using var table = new DataTable();
        table.Columns.Add("Id", typeof(Guid)); table.Columns.Add("TenantId", typeof(Guid));
        table.Columns.Add("MetricCode", typeof(string)); table.Columns.Add("PeriodKey", typeof(string));
        table.Columns.Add("LimitValue", typeof(long)); table.Columns.Add("UsedValue", typeof(long));
        table.Columns.Add("ReservedValue", typeof(long)); table.Columns.Add("Version", versionType);
        var tenant = Guid.CreateVersion7();
        table.Rows.Add(DBNull.Value, tenant, "identity.seats", "lifetime", 100L, 0L, 0L, Convert.ChangeType(0, versionType));
        using var reader = table.CreateDataReader();
        var parse = reader.GetRowParser<TenantQuotaUsageBaselineRecord>();
        Assert.IsTrue(reader.Read());
        var row = parse(reader);
        Assert.IsNull(row.Id);
        Assert.AreEqual(tenant, row.TenantId);
        Assert.AreEqual(0, row.Version);
    }

    private static DataTable Aggregate(Type valueType)
    {
        var table = new DataTable();
        table.Columns.Add("TenantId", typeof(Guid)); table.Columns.Add("UsageValue", valueType);
        table.Rows.Add(Guid.CreateVersion7(), Convert.ChangeType(7, valueType));
        return table;
    }
}
