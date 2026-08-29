namespace Full.NET.Data.Dapper;

/// <summary>
/// 对固定 SQL 做轻量词法检查，确认 <c>@TenantId</c> 是完整参数令牌；查询和变更谓词必须与
/// <c>TenantId</c>（租户目录根记录允许 <c>Id</c>）做等值比较，插入则必须位于 VALUES 子句。
/// 该检查不是通用 SQL 解析器，只识别 Full.NET 双 Provider 已批准的固定形状。
/// </summary>
internal static class TenantSqlParameterUsage
{
    private const string TenantParameter = "@TenantId";

    public static bool IsUsedInSafeClause(string sql)
    {
        ArgumentNullException.ThrowIfNull(sql);

        var clause = TenantParameterClause.None;
        var index = 0;
        while (index < sql.Length)
        {
            if (char.IsWhiteSpace(sql[index]))
            {
                index++;
                continue;
            }

            if (StartsWith(sql, index, "--"))
            {
                SkipLineComment(sql, ref index);
                continue;
            }

            if (sql[index] == '#')
            {
                SkipLineComment(sql, ref index);
                continue;
            }

            if (StartsWith(sql, index, "/*"))
            {
                SkipBlockComment(sql, ref index);
                continue;
            }

            if (sql[index] == '\'')
            {
                SkipQuoted(sql, ref index, '\'', '\'');
                continue;
            }

            if (sql[index] == '"')
            {
                SkipQuoted(sql, ref index, '"', '"');
                continue;
            }

            if (sql[index] == '`')
            {
                SkipQuoted(sql, ref index, '`', '`');
                continue;
            }

            if (sql[index] == '[')
            {
                SkipQuoted(sql, ref index, '[', ']');
                continue;
            }

            if (sql[index] == '@')
            {
                if (IsTenantParameter(sql, index)
                    && (clause == TenantParameterClause.Values
                        || (clause is TenantParameterClause.Where
                                or TenantParameterClause.JoinOn
                            && IsTenantEqualityPredicate(sql, index))))
                {
                    return true;
                }

                SkipParameter(sql, ref index);
                continue;
            }

            if (IsIdentifierStart(sql[index]))
            {
                var start = index++;
                while (index < sql.Length && IsIdentifierPart(sql[index]))
                {
                    index++;
                }

                clause = UpdateClause(sql.AsSpan(start, index - start), clause);
                continue;
            }

            if (sql[index] == ';')
            {
                clause = TenantParameterClause.None;
            }

            index++;
        }

        return false;
    }

    private static TenantParameterClause UpdateClause(
        ReadOnlySpan<char> token,
        TenantParameterClause current) =>
        token.Equals("WHERE", StringComparison.OrdinalIgnoreCase)
            ? TenantParameterClause.Where
            : token.Equals("ON", StringComparison.OrdinalIgnoreCase)
                ? TenantParameterClause.JoinOn
                : token.Equals("VALUES", StringComparison.OrdinalIgnoreCase)
                    ? TenantParameterClause.Values
                    : IsClauseBoundary(token)
                        ? TenantParameterClause.None
                        : current;

    private static bool IsClauseBoundary(ReadOnlySpan<char> token) =>
        token.Equals("SELECT", StringComparison.OrdinalIgnoreCase)
        || token.Equals("FROM", StringComparison.OrdinalIgnoreCase)
        || token.Equals("SET", StringComparison.OrdinalIgnoreCase)
        || token.Equals("INSERT", StringComparison.OrdinalIgnoreCase)
        || token.Equals("UPDATE", StringComparison.OrdinalIgnoreCase)
        || token.Equals("DELETE", StringComparison.OrdinalIgnoreCase)
        || token.Equals("MERGE", StringComparison.OrdinalIgnoreCase)
        || token.Equals("JOIN", StringComparison.OrdinalIgnoreCase)
        || token.Equals("GROUP", StringComparison.OrdinalIgnoreCase)
        || token.Equals("ORDER", StringComparison.OrdinalIgnoreCase)
        || token.Equals("HAVING", StringComparison.OrdinalIgnoreCase)
        || token.Equals("LIMIT", StringComparison.OrdinalIgnoreCase)
        || token.Equals("OFFSET", StringComparison.OrdinalIgnoreCase)
        || token.Equals("RETURNING", StringComparison.OrdinalIgnoreCase)
        || token.Equals("UNION", StringComparison.OrdinalIgnoreCase);

    private static bool IsTenantParameter(string sql, int index)
    {
        if (!sql.AsSpan(index).StartsWith(TenantParameter, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var end = index + TenantParameter.Length;
        return end == sql.Length || !IsIdentifierPart(sql[end]);
    }

    private static bool IsTenantEqualityPredicate(string sql, int parameterIndex) =>
        IsTenantColumnOnLeft(sql, parameterIndex)
        || IsTenantColumnOnRight(sql, parameterIndex + TenantParameter.Length);

    private static bool IsTenantColumnOnLeft(string sql, int parameterIndex)
    {
        var index = parameterIndex - 1;
        SkipWhitespaceBackward(sql, ref index);
        if (index < 0 || sql[index] != '=')
        {
            return false;
        }

        index--;
        SkipWhitespaceBackward(sql, ref index);
        return TryReadIdentifierBackward(sql, ref index, out var identifier)
            && IsTenantIdentityColumn(identifier);
    }

    private static bool IsTenantColumnOnRight(string sql, int parameterEnd)
    {
        var index = parameterEnd;
        SkipWhitespaceForward(sql, ref index);
        if (index >= sql.Length || sql[index] != '=')
        {
            return false;
        }

        index++;
        SkipWhitespaceForward(sql, ref index);
        if (!TryReadIdentifierForward(sql, ref index, out var identifier))
        {
            return false;
        }

        SkipWhitespaceForward(sql, ref index);
        while (index < sql.Length && sql[index] == '.')
        {
            index++;
            SkipWhitespaceForward(sql, ref index);
            if (!TryReadIdentifierForward(sql, ref index, out identifier))
            {
                return false;
            }

            SkipWhitespaceForward(sql, ref index);
        }

        return IsTenantIdentityColumn(identifier);
    }

    private static bool IsTenantIdentityColumn(ReadOnlySpan<char> identifier) =>
        identifier.Equals("TenantId", StringComparison.OrdinalIgnoreCase)
        || identifier.Equals("Id", StringComparison.OrdinalIgnoreCase);

    private static bool TryReadIdentifierBackward(
        string sql,
        ref int index,
        out ReadOnlySpan<char> identifier)
    {
        if (index >= 0 && sql[index] is ']' or '`' or '"')
        {
            var closing = sql[index];
            var opening = closing == ']' ? '[' : closing;
            var end = index;
            index = end == 0 ? -1 : sql.LastIndexOf(opening, end - 1);
            if (index >= 0)
            {
                identifier = sql.AsSpan(index + 1, end - index - 1);
                index--;
                return true;
            }
        }

        var identifierEnd = index + 1;
        while (index >= 0 && IsIdentifierPart(sql[index]))
        {
            index--;
        }

        if (identifierEnd == index + 1)
        {
            identifier = default;
            return false;
        }

        identifier = sql.AsSpan(index + 1, identifierEnd - index - 1);
        return true;
    }

    private static bool TryReadIdentifierForward(
        string sql,
        ref int index,
        out ReadOnlySpan<char> identifier)
    {
        if (index < sql.Length && sql[index] is '[' or '`' or '"')
        {
            var opening = sql[index];
            var closing = opening == '[' ? ']' : opening;
            var start = ++index;
            var end = sql.IndexOf(closing, start);
            if (end >= 0)
            {
                identifier = sql.AsSpan(start, end - start);
                index = end + 1;
                return true;
            }
        }

        var identifierStart = index;
        while (index < sql.Length && IsIdentifierPart(sql[index]))
        {
            index++;
        }

        if (identifierStart == index)
        {
            identifier = default;
            return false;
        }

        identifier = sql.AsSpan(identifierStart, index - identifierStart);
        return true;
    }

    private static void SkipWhitespaceBackward(string sql, ref int index)
    {
        while (index >= 0 && char.IsWhiteSpace(sql[index]))
        {
            index--;
        }
    }

    private static void SkipWhitespaceForward(string sql, ref int index)
    {
        while (index < sql.Length && char.IsWhiteSpace(sql[index]))
        {
            index++;
        }
    }

    private static void SkipParameter(string sql, ref int index)
    {
        index++;
        while (index < sql.Length && IsIdentifierPart(sql[index]))
        {
            index++;
        }
    }

    private static void SkipLineComment(string sql, ref int index)
    {
        while (index < sql.Length && sql[index] is not '\r' and not '\n')
        {
            index++;
        }
    }

    private static void SkipBlockComment(string sql, ref int index)
    {
        var depth = 1;
        index += 2;
        while (index < sql.Length && depth > 0)
        {
            if (StartsWith(sql, index, "/*"))
            {
                depth++;
                index += 2;
            }
            else if (StartsWith(sql, index, "*/"))
            {
                depth--;
                index += 2;
            }
            else
            {
                index++;
            }
        }
    }

    private static void SkipQuoted(
        string sql,
        ref int index,
        char opening,
        char closing)
    {
        index++;
        while (index < sql.Length)
        {
            if (sql[index] == '\\' && opening is '\'' or '"' or '`')
            {
                index = Math.Min(index + 2, sql.Length);
                continue;
            }

            if (sql[index] != closing)
            {
                index++;
                continue;
            }

            if (index + 1 < sql.Length && sql[index + 1] == closing)
            {
                index += 2;
                continue;
            }

            index++;
            return;
        }
    }

    private static bool StartsWith(string sql, int index, string value) =>
        sql.AsSpan(index).StartsWith(value, StringComparison.Ordinal);

    private static bool IsIdentifierStart(char value) =>
        char.IsLetter(value) || value == '_';

    private static bool IsIdentifierPart(char value) =>
        char.IsLetterOrDigit(value) || value == '_';

    private enum TenantParameterClause
    {
        None,
        Where,
        JoinOn,
        Values,
    }
}
