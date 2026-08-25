using System.Data.Common;

namespace Full.NET.Data.Dapper;

internal interface IDbConnectionFactory
{
    DbConnection Create();
}
