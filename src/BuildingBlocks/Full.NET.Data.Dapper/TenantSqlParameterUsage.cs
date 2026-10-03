namespace Full.NET.Data.Dapper;

/// <summary>
/// 对固定 SQL 做轻量词法检查，确认 <c>@TenantId</c> 是完整参数令牌；查询和变更谓词必须与
/// <c>TenantId</c> 做等值比较；仅租户目录根表的受限单表形状允许无限定符的 <c>Id</c>，
/// 插入则必须位于 VALUES 子句。根表例外不能由别名、JOIN、子查询或复合语句借用。
/// 该检查不是通用 SQL 解析器，只识别 Full.NET 双 Provider 已批准的固定形状。
/// </summary>
internal static class TenantSqlParameterUsage
{
    private const string TenantParameter = "@TenantId";

    public static bool IsUsedInSafeClause(string sql)
    {
        ArgumentNullException.ThrowIfNull(sql);

        var allowsTenantRootId = IsTenantRootStatement(sql);
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
                            && IsTenantEqualityPredicate(sql, index, allowsTenantRootId))))
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

    private static bool IsTenantEqualityPredicate(
        string sql,
        int parameterIndex,
        bool allowsTenantRootId) =>
        IsTenantColumnOnLeft(sql, parameterIndex, allowsTenantRootId)
        || IsTenantColumnOnRight(sql, parameterIndex, allowsTenantRootId);

    private static bool IsTenantColumnOnLeft(
        string sql,
        int parameterIndex,
        bool allowsTenantRootId)
    {
        var index = parameterIndex - 1;
        SkipWhitespaceBackward(sql, ref index);
        if (index < 0 || sql[index] != '=')
        {
            return false;
        }

        index--;
        SkipWhitespaceBackward(sql, ref index);
        if (!TryReadIdentifierBackward(sql, ref index, out var identifier))
        {
            return false;
        }

        return IsTenantIdentityColumn(identifier)
            || (allowsTenantRootId
                && identifier.Equals("Id", StringComparison.OrdinalIgnoreCase));
    }

    private static bool IsTenantColumnOnRight(
        string sql,
        int parameterIndex,
        bool allowsTenantRootId)
    {
        var index = parameterIndex + TenantParameter.Length;
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

        return IsTenantIdentityColumn(identifier)
            || (allowsTenantRootId
                && identifier.Equals("Id", StringComparison.OrdinalIgnoreCase));
    }

    private static bool IsTenantIdentityColumn(ReadOnlySpan<char> identifier) =>
        identifier.Equals("TenantId", StringComparison.OrdinalIgnoreCase);

    private static bool IsTenantRootStatement(string sql)
    {
        // Id 并非通用租户列：只有官方根表的单表 SELECT/UPDATE 才能使用此例外。
        // 保持形状封闭，避免合法根表出现在投影、注释、别名或另一条语句中时放行业务表。
        var tokens = ReadRootShapeTokens(sql);
        if (tokens.Count == 0)
        {
            return false;
        }

        var isSelect = TokenEquals(tokens[0], "SELECT");
        if (!isSelect && !TokenEquals(tokens[0], "UPDATE"))
        {
            return false;
        }

        var fromIndex = -1;
        var whereIndex = -1;
        for (var index = 1; index < tokens.Count; index++)
        {
            var token = tokens[index];
            if (TokenEquals(token, "FROM"))
            {
                if (!isSelect || fromIndex >= 0)
                {
                    return false;
                }

                fromIndex = index;
            }

            if (TokenEquals(token, "WHERE"))
            {
                if (whereIndex >= 0)
                {
                    return false;
                }

                whereIndex = index;
            }

            if (token == ";" && index != tokens.Count - 1
                || TokenEquals(token, "SELECT")
                || TokenEquals(token, "UPDATE")
                || TokenEquals(token, "DELETE")
                || TokenEquals(token, "INSERT")
                || TokenEquals(token, "MERGE")
                || TokenEquals(token, "INTO")
                || TokenEquals(token, "JOIN")
                || TokenEquals(token, "APPLY")
                || TokenEquals(token, "UNION")
                || TokenEquals(token, "INTERSECT")
                || TokenEquals(token, "EXCEPT")
                || TokenEquals(token, "OR")
                || (TokenEquals(token, "NOT") && !TokenEquals(tokens[index - 1], "IS")))
            {
                return false;
            }
        }

        if (whereIndex < 0 || (isSelect && fromIndex < 0))
        {
            return false;
        }

        var tableIndex = isSelect ? fromIndex + 1 : 1;
        if (tableIndex + 2 < tokens.Count
            && TokenEquals(tokens[tableIndex], "dbo")
            && tokens[tableIndex + 1] == ".")
        {
            tableIndex += 2;
        }

        // 根表的第一个 WHERE 条件必须是完整等式，后续只允许 AND 连接的简单列条件。
        // 不从表达式内部寻找 Id，避免 MySQL 将 (Id = @TenantId) = 0 解释为反向筛选。
        var predicateEnd = whereIndex + 4;
        var hasRootEquality = whereIndex + 3 < tokens.Count
            && tokens[whereIndex + 2] == "="
            && ((TokenEquals(tokens[whereIndex + 1], "Id")
                    && TokenEquals(tokens[whereIndex + 3], TenantParameter))
                || (TokenEquals(tokens[whereIndex + 1], TenantParameter)
                    && TokenEquals(tokens[whereIndex + 3], "Id")))
            && HasSupportedRootTail(tokens, predicateEnd);

        return hasRootEquality
            && tableIndex + 1 < tokens.Count
            && TokenEquals(tokens[tableIndex], "fn_tenancy_tenant")
            && (isSelect
                ? tableIndex + 1 == whereIndex
                : TokenEquals(tokens[tableIndex + 1], "SET"));
    }

    private static bool HasSupportedRootTail(List<string> tokens, int index)
    {
        // 使用封闭语法，避免把 XOR、方言运算符或无分号的下一条命令当成普通条件。
        while (index < tokens.Count)
        {
            if (tokens[index] == ";")
            {
                return index == tokens.Count - 1;
            }

            if (!TokenEquals(tokens[index++], "AND")
                || index >= tokens.Count
                || !IsIdentifierStart(tokens[index][0]))
            {
                return false;
            }

            index++;
            if (index < tokens.Count && tokens[index] == "=")
            {
                index++;
                if (index >= tokens.Count
                    || !(tokens[index][0] == '@'
                        || tokens[index] is "'" or "0" or "1"
                        || TokenEquals(tokens[index], "NULL")))
                {
                    return false;
                }

                index++;
            }
            else if (index < tokens.Count && TokenEquals(tokens[index], "IS"))
            {
                index++;
                if (index < tokens.Count && TokenEquals(tokens[index], "NOT"))
                {
                    index++;
                }

                if (index >= tokens.Count || !TokenEquals(tokens[index++], "NULL"))
                {
                    return false;
                }
            }
            else
            {
                return false;
            }
        }

        return true;
    }

    private static List<string> ReadRootShapeTokens(string sql)
    {
        var tokens = new List<string>();
        var index = 0;
        while (index < sql.Length)
        {
            if (char.IsWhiteSpace(sql[index]))
            {
                index++;
            }
            else if (StartsWith(sql, index, "--"))
            {
                // MySQL 要求双连字符后有空白；不接受两种提供程序解释不同的注释。
                if (index + 2 < sql.Length && !char.IsWhiteSpace(sql[index + 2]))
                {
                    return [];
                }

                SkipLineComment(sql, ref index);
            }
            else if (sql[index] == '#')
            {
                SkipLineComment(sql, ref index);
            }
            else if (StartsWith(sql, index, "/*"))
            {
                // MySQL 可执行注释包含真正的 SQL，不能按普通注释忽略。
                if (StartsWith(sql, index, "/*!"))
                {
                    return [];
                }

                // SQL Server 支持嵌套块注释，MySQL 在第一个结束符后恢复执行；例外拒绝歧义。
                var commentEnd = sql.IndexOf("*/", index + 2, StringComparison.Ordinal);
                var nestedComment = sql.IndexOf("/*", index + 2, StringComparison.Ordinal);
                if (commentEnd < 0 || (nestedComment >= 0 && nestedComment < commentEnd))
                {
                    return [];
                }

                SkipBlockComment(sql, ref index);
            }
            else if (sql[index] == '\'')
            {
                var start = index;
                SkipQuoted(sql, ref index, '\'', '\'');
                // 反斜杠转义受 MySQL SQL mode 影响，根表例外不支持此类字符串。
                if (sql.AsSpan(start, index - start).Contains('\\'))
                {
                    return [];
                }

                tokens.Add("'");
            }
            else if (sql[index] == '@')
            {
                var start = index;
                SkipParameter(sql, ref index);
                tokens.Add(sql[start..index]);
            }
            else if (IsIdentifierStart(sql[index]) || sql[index] is '[' or '`' or '"')
            {
                if (!TryReadIdentifierForward(sql, ref index, out var identifier))
                {
                    return [];
                }

                tokens.Add(identifier.ToString());
            }
            else
            {
                tokens.Add(sql[index++].ToString());
            }
        }

        return tokens;
    }

    private static bool TokenEquals(string token, string expected) =>
        string.Equals(token, expected, StringComparison.OrdinalIgnoreCase);

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
