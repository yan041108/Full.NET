#if FULLNET_AOT_COMPILE
#pragma warning disable CS8764, CS8765
using System.Collections;
using System.Data;
using System.Data.Common;
using System.Diagnostics.CodeAnalysis;

namespace Full.NET.Data.Dapper;

/// <summary>
/// MySQL DATETIME 在 Native AOT Dapper.AOT 路径下返回 <see cref="DateTime"/>；
/// 将读取面对齐为 UTC <see cref="DateTimeOffset"/>，避免 CommandUtils.As 转换失败。
/// </summary>
internal sealed class MySqlAotUtcDateTimeOffsetConnection(DbConnection inner)
    : DbConnection, IDapperDbConnectionWrapper
{
    private readonly DbConnection _inner = inner;

    public DbConnection InnerConnection => _inner;

    protected override DbTransaction BeginDbTransaction(IsolationLevel isolationLevel) =>
        _inner.BeginTransaction(isolationLevel);

    protected override DbCommand CreateDbCommand() =>
        new MySqlAotUtcDateTimeOffsetCommand(_inner.CreateCommand());

    public override void ChangeDatabase(string databaseName) =>
        _inner.ChangeDatabase(databaseName);

    public override void Close() => _inner.Close();

    public override void Open() => _inner.Open();

    public override Task OpenAsync(CancellationToken cancellationToken) =>
        _inner.OpenAsync(cancellationToken);

    public override string ConnectionString
    {
        get => _inner.ConnectionString;
        set => _inner.ConnectionString = value;
    }

    public override string Database => _inner.Database;

    public override ConnectionState State => _inner.State;

    public override string DataSource => _inner.DataSource;

    public override string ServerVersion => _inner.ServerVersion;

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _inner.Dispose();
        }

        base.Dispose(disposing);
    }

    public override ValueTask DisposeAsync() => _inner.DisposeAsync();
}

internal sealed class MySqlAotUtcDateTimeOffsetCommand(DbCommand inner) : DbCommand
{
    private readonly DbCommand _inner = inner;

    protected override DbDataReader ExecuteDbDataReader(CommandBehavior behavior) =>
        new MySqlAotUtcDateTimeOffsetDataReader(
            _inner.ExecuteReader(behavior));

    protected override async Task<DbDataReader> ExecuteDbDataReaderAsync(
        CommandBehavior behavior,
        CancellationToken cancellationToken) =>
        new MySqlAotUtcDateTimeOffsetDataReader(
            await _inner.ExecuteReaderAsync(behavior, cancellationToken)
                .ConfigureAwait(false));

    public override string CommandText
    {
        get => _inner.CommandText;
        set => _inner.CommandText = value;
    }

    public override int CommandTimeout
    {
        get => _inner.CommandTimeout;
        set => _inner.CommandTimeout = value;
    }

    public override CommandType CommandType
    {
        get => _inner.CommandType;
        set => _inner.CommandType = value;
    }

    public override bool DesignTimeVisible
    {
        get => _inner.DesignTimeVisible;
        set => _inner.DesignTimeVisible = value;
    }

    public override UpdateRowSource UpdatedRowSource
    {
        get => _inner.UpdatedRowSource;
        set => _inner.UpdatedRowSource = value;
    }

    protected override DbConnection? DbConnection
    {
        get => _inner.Connection;
        set => _inner.Connection = value is IDapperDbConnectionWrapper wrapper
            ? wrapper.InnerConnection
            : value;
    }

    protected override DbParameterCollection DbParameterCollection => _inner.Parameters;

    protected override DbTransaction? DbTransaction
    {
        get => _inner.Transaction;
        set => _inner.Transaction = value;
    }

    public override void Cancel() => _inner.Cancel();

    public override int ExecuteNonQuery() => _inner.ExecuteNonQuery();

    public override Task<int> ExecuteNonQueryAsync(CancellationToken cancellationToken) =>
        _inner.ExecuteNonQueryAsync(cancellationToken);

    public override object? ExecuteScalar() => _inner.ExecuteScalar();

    public override Task<object?> ExecuteScalarAsync(CancellationToken cancellationToken) =>
        _inner.ExecuteScalarAsync(cancellationToken);

    protected override DbParameter CreateDbParameter() => _inner.CreateParameter();

    public override void Prepare() => _inner.Prepare();

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _inner.Dispose();
        }

        base.Dispose(disposing);
    }
}

internal sealed class MySqlAotUtcDateTimeOffsetDataReader(DbDataReader inner) : DbDataReader
{
    private readonly DbDataReader _inner = inner;

    public override object this[int ordinal] => NormalizeValue(_inner[ordinal]);

    public override object this[string name] => NormalizeValue(_inner[name]);

    public override int Depth => _inner.Depth;

    public override int FieldCount => _inner.FieldCount;

    public override bool HasRows => _inner.HasRows;

    public override bool IsClosed => _inner.IsClosed;

    public override int RecordsAffected => _inner.RecordsAffected;

    public override bool GetBoolean(int ordinal) => _inner.GetBoolean(ordinal);

    public override byte GetByte(int ordinal) => _inner.GetByte(ordinal);

    public override long GetBytes(
        int ordinal,
        long dataOffset,
        byte[]? buffer,
        int bufferOffset,
        int length) =>
        _inner.GetBytes(ordinal, dataOffset, buffer, bufferOffset, length);

    public override char GetChar(int ordinal) => _inner.GetChar(ordinal);

    public override long GetChars(
        int ordinal,
        long dataOffset,
        char[]? buffer,
        int bufferOffset,
        int length) =>
        _inner.GetChars(ordinal, dataOffset, buffer, bufferOffset, length);

    public override string GetDataTypeName(int ordinal) => _inner.GetDataTypeName(ordinal);

    public override DateTime GetDateTime(int ordinal) => _inner.GetDateTime(ordinal);

    public override decimal GetDecimal(int ordinal) => _inner.GetDecimal(ordinal);

    public override double GetDouble(int ordinal) => _inner.GetDouble(ordinal);

    [return: DynamicallyAccessedMembers(
        DynamicallyAccessedMemberTypes.PublicFields
        | DynamicallyAccessedMemberTypes.PublicProperties)]
    public override Type GetFieldType(int ordinal) =>
        _inner.GetFieldType(ordinal) == typeof(DateTime)
            ? typeof(DateTimeOffset)
            : _inner.GetFieldType(ordinal);

    public override float GetFloat(int ordinal) => _inner.GetFloat(ordinal);

    public override Guid GetGuid(int ordinal) => _inner.GetGuid(ordinal);

    public override short GetInt16(int ordinal) => _inner.GetInt16(ordinal);

    public override int GetInt32(int ordinal) => _inner.GetInt32(ordinal);

    public override long GetInt64(int ordinal) => _inner.GetInt64(ordinal);

    public override string GetName(int ordinal) => _inner.GetName(ordinal);

    public override int GetOrdinal(string name) => _inner.GetOrdinal(name);

    public override string GetString(int ordinal) => _inner.GetString(ordinal);

    public override object GetValue(int ordinal) =>
        NormalizeValue(_inner.GetValue(ordinal));

    public override int GetValues(object[] values) => _inner.GetValues(values);

    public override bool IsDBNull(int ordinal) => _inner.IsDBNull(ordinal);

    public override bool NextResult() => _inner.NextResult();

    public override bool Read() => _inner.Read();

    public override Task<bool> ReadAsync(CancellationToken cancellationToken) =>
        _inner.ReadAsync(cancellationToken);

    public override Task<bool> NextResultAsync(CancellationToken cancellationToken) =>
        _inner.NextResultAsync(cancellationToken);

    public override IEnumerator GetEnumerator() => _inner.GetEnumerator();

    public override T GetFieldValue<T>(int ordinal)
    {
        if (typeof(T) == typeof(DateTimeOffset)
            && _inner.GetFieldType(ordinal) == typeof(DateTime))
        {
            return (T)(object)new DateTimeOffset(
                DateTime.SpecifyKind(_inner.GetDateTime(ordinal), DateTimeKind.Utc));
        }

        return _inner.GetFieldValue<T>(ordinal);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _inner.Dispose();
        }

        base.Dispose(disposing);
    }

    private static object NormalizeValue(object value) =>
        value is DateTime dateTime
            ? new DateTimeOffset(DateTime.SpecifyKind(dateTime, DateTimeKind.Utc))
            : value;
}
#endif
