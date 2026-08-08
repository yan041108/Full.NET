using System.Reflection;
using System.Reflection.Emit;
using Full.NET.Data.Abstractions;

namespace Full.NET.ArchitectureTests;

internal static class SqlStatementConstructionScanner
{
    private static readonly OpCode[] OneByteOpCodes = new OpCode[0x100];
    private static readonly OpCode[] TwoByteOpCodes = new OpCode[0x100];

    static SqlStatementConstructionScanner()
    {
        foreach (var field in typeof(OpCodes).GetFields(BindingFlags.Public | BindingFlags.Static))
        {
            if (field.GetValue(null) is not OpCode opCode)
            {
                continue;
            }

            var value = unchecked((ushort)opCode.Value);
            if (opCode.Size == 1)
            {
                OneByteOpCodes[value] = opCode;
            }
            else
            {
                TwoByteOpCodes[value & 0xff] = opCode;
            }
        }
    }

    public static string[] FindViolations(
        IEnumerable<Assembly> assemblies,
        IEnumerable<string>? allowedRuntimeCloneMethods = null) =>
        FindViolations(assemblies
                .Distinct()
                .SelectMany(GetLoadableTypes),
            allowedRuntimeCloneMethods);

    public static string[] FindViolations(
        IEnumerable<Type> types,
        IEnumerable<string>? allowedRuntimeCloneMethods = null)
    {
        var violations = new List<string>();
        var allowedClones = (allowedRuntimeCloneMethods ?? [])
            .ToHashSet(StringComparer.Ordinal);
        foreach (var type in types.Distinct())
        {
            if (type.IsNested)
            {
                continue;
            }

            var constructionMethods = GetDeclaredMethods(type)
                .Select(method => new
                {
                    Method = method,
                    Scan = ScanSqlStatementCreations(method),
                })
                .Where(item => item.Scan.TotalCount > 0)
                .ToArray();

            foreach (var item in constructionMethods.Where(item =>
                         item.Method != type.TypeInitializer))
            {
                var methodIdentity = $"{type.FullName}.{item.Method.Name}";
                if (allowedClones.Contains(methodIdentity)
                    && item.Scan.ConstructorCount == 0
                    && item.Scan.CloneCount > 0
                    && !item.Scan.MutatesScopeMetadata)
                {
                    continue;
                }

                violations.Add(
                    $"SqlStatement construction outside a static declaration: "
                    + $"{methodIdentity} ({item.Scan.TotalCount}).");
            }

            var staticConstructionCount = constructionMethods
                .Where(item => item.Method == type.TypeInitializer)
                .Sum(item => item.Scan.TotalCount);
            var declaredStatementCount = CountDeclaredSqlStatementMembers(type);
            if (staticConstructionCount != declaredStatementCount)
            {
                violations.Add(
                    $"SqlStatement static construction/declaration count mismatch: "
                    + $"{type.FullName} constructs {staticConstructionCount} and declares "
                    + $"{declaredStatementCount}.");
            }
        }

        return violations
            .OrderBy(value => value, StringComparer.Ordinal)
            .ToArray();
    }

    private static int CountDeclaredSqlStatementMembers(Type type)
    {
        const BindingFlags Flags = BindingFlags.Public
            | BindingFlags.NonPublic
            | BindingFlags.Static
            | BindingFlags.DeclaredOnly;
        return type.GetFields(Flags).Count(field => field.FieldType == typeof(SqlStatement))
            + type.GetProperties(Flags).Count(property =>
                property.PropertyType == typeof(SqlStatement)
                && property.GetIndexParameters().Length == 0);
    }

    private static IEnumerable<MethodBase> GetDeclaredMethods(Type type)
    {
        const BindingFlags Flags = BindingFlags.Public
            | BindingFlags.NonPublic
            | BindingFlags.Instance
            | BindingFlags.Static
            | BindingFlags.DeclaredOnly;

        var methods = type.GetMethods(Flags)
            .Cast<MethodBase>()
            .Concat(type.GetConstructors(Flags));
        if (type.TypeInitializer is not null)
        {
            methods = methods.Append(type.TypeInitializer);
        }

        return methods.Distinct();
    }

    private static SqlStatementCreationScan ScanSqlStatementCreations(MethodBase method)
    {
        var il = method.GetMethodBody()?.GetILAsByteArray();
        if (il is null)
        {
            return new SqlStatementCreationScan(0, 0, false);
        }

        var constructorCount = 0;
        var cloneCount = 0;
        var mutatesScopeMetadata = false;
        var offset = 0;
        while (offset < il.Length)
        {
            var opCode = ReadOpCode(il, ref offset);
            if (opCode.OperandType == OperandType.InlineMethod)
            {
                var metadataToken = BitConverter.ToInt32(il, offset);
                var calledMethod = method.Module.ResolveMethod(
                    metadataToken,
                    method.DeclaringType?.GetGenericArguments(),
                    method is MethodInfo methodInfo
                        ? methodInfo.GetGenericArguments()
                        : null);
                var constructsStatement = opCode == OpCodes.Newobj
                    && calledMethod is ConstructorInfo
                    && calledMethod.DeclaringType == typeof(SqlStatement);
                var clonesStatement = opCode is var callOpCode
                    && (callOpCode == OpCodes.Call || callOpCode == OpCodes.Callvirt)
                    && calledMethod?.DeclaringType == typeof(SqlStatement)
                    && string.Equals(
                        calledMethod.Name,
                        "<Clone>$",
                        StringComparison.Ordinal);
                var isGeneratedCloneBody = method.DeclaringType == typeof(SqlStatement)
                    && string.Equals(method.Name, "<Clone>$", StringComparison.Ordinal);
                if ((constructsStatement && !isGeneratedCloneBody) || clonesStatement)
                {
                    if (constructsStatement && !isGeneratedCloneBody)
                    {
                        constructorCount++;
                    }

                    if (clonesStatement)
                    {
                        cloneCount++;
                    }
                }

                if (calledMethod?.DeclaringType == typeof(SqlStatement)
                    && (string.Equals(
                            calledMethod.Name,
                            $"set_{nameof(SqlStatement.Scope)}",
                            StringComparison.Ordinal)
                        || string.Equals(
                            calledMethod.Name,
                            $"set_{nameof(SqlStatement.TenantBinding)}",
                            StringComparison.Ordinal)))
                {
                    mutatesScopeMetadata = true;
                }
            }

            offset += GetOperandSize(opCode.OperandType, il, offset);
        }

        return new SqlStatementCreationScan(
            constructorCount,
            cloneCount,
            mutatesScopeMetadata);
    }

    private static OpCode ReadOpCode(byte[] il, ref int offset)
    {
        var first = il[offset++];
        return first == 0xfe
            ? TwoByteOpCodes[il[offset++]]
            : OneByteOpCodes[first];
    }

    private static int GetOperandSize(OperandType operandType, byte[] il, int offset) =>
        operandType switch
        {
            OperandType.InlineNone => 0,
            OperandType.ShortInlineBrTarget or OperandType.ShortInlineI
                or OperandType.ShortInlineVar => 1,
            OperandType.InlineVar => 2,
            OperandType.InlineBrTarget or OperandType.InlineField
                or OperandType.InlineI or OperandType.InlineMethod
                or OperandType.InlineSig or OperandType.InlineString
                or OperandType.InlineTok or OperandType.InlineType
                or OperandType.ShortInlineR => 4,
            OperandType.InlineI8 or OperandType.InlineR => 8,
            OperandType.InlineSwitch => 4 + (BitConverter.ToInt32(il, offset) * 4),
            _ => throw new InvalidDataException(
                $"Unsupported IL operand type: {operandType}."),
        };

    private static IEnumerable<Type> GetLoadableTypes(Assembly assembly)
    {
        try
        {
            return assembly.GetTypes();
        }
        catch (ReflectionTypeLoadException exception)
        {
            var loaderMessages = exception.LoaderExceptions
                .Where(loaderException => loaderException is not null)
                .Select(loaderException => loaderException!.Message)
                .Distinct(StringComparer.Ordinal)
                .OrderBy(message => message, StringComparer.Ordinal);
            throw new InvalidOperationException(
                $"Could not load every type from {assembly.FullName}: "
                + string.Join(" | ", loaderMessages),
                exception);
        }
    }

    private sealed record SqlStatementCreationScan(
        int ConstructorCount,
        int CloneCount,
        bool MutatesScopeMetadata)
    {
        public int TotalCount => ConstructorCount + CloneCount;
    }
}

internal static class StaticSqlStatementDeclarationFixture
{
    public static readonly SqlStatement Statement = new(
        "fixture.static",
        "SELECT 1",
        SqlDataScope.HostOnly);
}

internal static class InlineSqlStatementConstructionFixture
{
    public static SqlStatement Create() =>
        new(
            "fixture.inline",
            "SELECT 1",
            default);
}

internal static class SafeSqlStatementCloneFixture
{
    public static SqlStatement Clone(SqlStatement statement) =>
        statement with
        {
            Name = statement.Name + ".filtered",
            Text = statement.Text + " AND Id = @Id",
        };
}

internal static class ScopeMutatingSqlStatementCloneFixture
{
    public static SqlStatement Clone(SqlStatement statement) =>
        statement with
        {
            Scope = SqlDataScope.Global,
        };
}
