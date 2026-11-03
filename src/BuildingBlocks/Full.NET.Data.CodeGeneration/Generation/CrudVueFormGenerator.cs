using Full.NET.Data.CodeGeneration.Schema;

namespace Full.NET.Data.CodeGeneration.Generation;

/// <summary>按线格式生成输入、空值转换与精度校验；Int64/Decimal 从不经过 JavaScript Number。</summary>
internal static class CrudVueFormGenerator
{
    internal static string Default(FullNetColumn column) => column.IsNullable ? "null" : column.ScalarType switch
    {
        FullNetScalarType.Boolean => "false", FullNetScalarType.Int32 => "0", FullNetScalarType.Int64 or FullNetScalarType.Decimal => "'0'", _ => "''",
    };

    internal static string Field(string form, FullNetColumn column, string? property = null, bool query = false, string? label = null, bool inlineDate = false)
    {
        property ??= column.JsonPropertyName;
        var access = form + "." + property;
        var nullable = query || column.IsNullable;
        var empty = query ? "undefined" : nullable ? "null" : column.ScalarType == FullNetScalarType.Int32 ? "0" : "''";
        var title = label ?? $"t('generatedCrud.field', {{ name: '{column.DatabaseName}' }})";
        var control = column.ScalarType switch
        {
            FullNetScalarType.Int32 => $"<el-input-number :model-value=\"{access} ?? undefined\" @update:model-value=\"value => {access} = value ?? {empty}\" :precision=\"0\" :min=\"-2147483648\" :max=\"2147483647\" :aria-label=\"{title}\" />",
            FullNetScalarType.Boolean when nullable => $"<el-radio-group :model-value=\"{access} === true ? 'true' : {access} === false ? 'false' : 'unset'\" @update:model-value=\"value => {access} = value === 'true' ? true : value === 'false' ? false : {empty}\" :aria-label=\"{title}\"><el-radio value=\"unset\">{{{{ t('generatedCrud.unset') }}}}</el-radio><el-radio value=\"true\">{{{{ t('generatedCrud.yes') }}}}</el-radio><el-radio value=\"false\">{{{{ t('generatedCrud.no') }}}}</el-radio></el-radio-group>",
            FullNetScalarType.Boolean => $"<el-switch v-model=\"{access}\" :aria-label=\"{title}\" />",
            FullNetScalarType.DateTimeUtc => "<el-date-picker " + (inlineDate
                ? $":model-value=\"{access} ? new Date({access}!) : null\" @update:model-value=\"value => {access} = value instanceof Date ? value.toISOString() : {(nullable ? "null" : "''")}\""
                : $"v-model=\"{DateModel(form, property)}\"") + $" type=\"datetime\" format=\"YYYY-MM-DD HH:mm:ss\" :clearable=\"{Bool(nullable)}\" :aria-label=\"{title}\" /><small>{{{{ t('generatedCrud.localTimeHint') }}}}</small>",
            _ => $"<el-input :model-value=\"{access} ?? ''\" @update:model-value=\"value => {access} = value === '' ? {empty} : String(value)\""
                + (column.ScalarType == FullNetScalarType.Int64 ? " inputmode=\"numeric\"" : column.ScalarType == FullNetScalarType.Decimal ? " inputmode=\"decimal\"" : string.Empty)
                + (column.ScalarType == FullNetScalarType.String && column.ResolvedUi.ControlKind == FullNetColumnControlKind.Textarea ? " type=\"textarea\"" : string.Empty)
                + (column.ScalarType == FullNetScalarType.String ? $" :maxlength=\"{column.MaxLength}\"" : column.ScalarType == FullNetScalarType.Uuid ? " :maxlength=\"36\"" : string.Empty)
                + (column.ScalarType is FullNetScalarType.Int64 or FullNetScalarType.Decimal ? " :maxlength=\"128\"" : string.Empty)
                + $" :aria-label=\"{title}\" />",
        };
        return $"<el-form-item :label=\"{title}\" prop=\"{property}\">{control}</el-form-item>";
    }

    internal static string DateModels(FullNetCrudSchema schema, IReadOnlyList<FullNetColumn> fields)
    {
        var models = new List<string>();
        foreach (var form in new[] { "createForm", "editForm" })
            models.AddRange(fields.Where(column => column.ScalarType == FullNetScalarType.DateTimeUtc
                && (form == "createForm" ? column.ResolvedUi.IncludeInCreate : column.ResolvedUi.IncludeInUpdate)).Select(column =>
                $"const {DateModel(form, column.JsonPropertyName)} = dateModel(() => {form}.{column.JsonPropertyName}, value => {form}.{column.JsonPropertyName} = value ?? {(column.IsNullable ? "null" : "''")});"));
        models.AddRange(CrudListQueryGenerator.Fields(schema).Where(field => field.Column.ScalarType == FullNetScalarType.DateTimeUtc).Select(field =>
            $"const {DateModel("query", field.QueryName)} = dateModel(() => query.{field.QueryName} ?? null, value => query.{field.QueryName} = value ?? undefined);"));
        if (models.Count == 0) return string.Empty;
        return $$"""
            // 只在用户编辑时转换为 UTC；读取日期不回写模型，保留原始微秒和 null。
            function dateModel(get: () => string | null, set: (value: string | null) => void) {
              return computed<Date | null>({ get: () => get() ? new Date(get()!) : null,
                set: value => set(value ? value.toISOString() : null) });
            }
            {{string.Join("\n", models)}}
            """;
    }

    internal static string Rules(IReadOnlyList<FullNetColumn> columns)
    {
        string Metadata(bool create) => RuleFields(columns.Where(column => create ? column.ResolvedUi.IncludeInCreate : column.ResolvedUi.IncludeInUpdate));
        return $$"""
            interface GeneratedField { name: string; kind: string; required: boolean; nullable: boolean; length: number; precision: number; scale: number }
            const createRules = computed(() => buildRules({{Metadata(true)}}, locale.value));
            const editRules = computed(() => buildRules({{Metadata(false)}}, locale.value));
            function buildRules(fields: readonly GeneratedField[], _locale: string): Record<string, FormItemRule[]> {
              return Object.fromEntries(fields.map(field => [field.name, [{ trigger: ['blur', 'change'],
                validator: (_rule: unknown, value: unknown, callback: (error?: Error) => void) => {
                  const message = validateField(value, field);
                  callback(message ? new Error(message) : undefined);
                } }]]));
            }
            function validateField(value: unknown, field: GeneratedField): string | undefined {
              if (value === null || value === undefined) return field.nullable && !field.required ? undefined : t('generatedCrud.valueRequired');
              if (field.kind === 'String') {
                if (typeof value !== 'string' || field.required && value.trim() === '') return t('generatedCrud.valueRequired');
                return value.length > field.length ? t('generatedCrud.valueTooLong', { count: field.length }) : undefined;
              }
              if (field.kind === 'Boolean') return typeof value === 'boolean' ? undefined : t('generatedCrud.valueInvalid');
              if (field.kind === 'Int32') return typeof value === 'number' && Number.isInteger(value) && value >= -2147483648 && value <= 2147483647 ? undefined : t('generatedCrud.integerRange');
              if (typeof value !== 'string') return t('generatedCrud.valueInvalid');
              if (field.kind === 'Int64') {
                if (value.length > 128 || !/^-?(?:0|[1-9]\d*)$/.test(value)) return t('generatedCrud.integerRange');
                const integer = BigInt(value);
                return integer >= -9223372036854775808n && integer <= 9223372036854775807n ? undefined : t('generatedCrud.integerRange');
              }
              if (field.kind === 'Decimal') {
                if (value.length > 128 || !/^-?(?:0|[1-9]\d*)(?:\.\d+)?$/.test(value)) return t('generatedCrud.decimalPrecision');
                const [integer = '', rawFraction = ''] = value.replace(/^-/, '').split('.');
                const fraction = rawFraction.replace(/0+$/, '');
                const digits = integer.replace(/^0+/, '');
                if (digits.length > field.precision - field.scale || fraction.length > field.scale || fraction.length > 28
                  || BigInt((integer + fraction) || '0') > 79228162514264337593543950335n) return t('generatedCrud.decimalPrecision');
                return undefined;
              }
              if (field.kind === 'Uuid') return /^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/i.test(value)
                && (!field.required || value !== '00000000-0000-0000-0000-000000000000') ? undefined : t('generatedCrud.uuidInvalid');
              if (field.kind === 'DateTimeUtc') {
                const date = new Date(value);
                const fraction = /\.(\d+)/.exec(value)?.[1]?.replace(/0+$/, '') ?? '';
                return !Number.isNaN(date.valueOf()) && date.getUTCFullYear() >= 1000 && date.getUTCFullYear() <= 9999 && fraction.length <= 6 ? undefined : t('generatedCrud.dateInvalid');
              }
              return t('generatedCrud.valueInvalid');
            }
            """;
    }

    private static string DateModel(string form, string property) => form + char.ToUpperInvariant(property[0]) + property[1..] + "Date";
    internal static string RuleFields(IEnumerable<FullNetColumn> columns) => "[" + string.Join(",\n", columns.Select(column =>
        $"  {{ name: '{column.JsonPropertyName}', kind: '{column.ScalarType}', required: {Bool(column.ResolvedUi.Required)}, nullable: {Bool(column.IsNullable)}, length: {column.MaxLength ?? 0}, precision: {column.NumericPrecision ?? 0}, scale: {column.NumericScale ?? 0} }}")) + "]";
    private static string Bool(bool value) => value ? "true" : "false";
}
