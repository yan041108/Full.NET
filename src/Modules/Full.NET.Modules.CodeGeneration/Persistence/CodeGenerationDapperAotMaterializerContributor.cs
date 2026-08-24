#if FULLNET_AOT_COMPILE
using System.Data.Common;
using Full.NET.Data.Dapper;
using Full.NET.Modules.CodeGeneration.Persistence;

namespace Full.NET.Modules.CodeGeneration.Persistence;

/// <summary>
/// CodeGeneration 模块 Native AOT 行物化器注册。
/// </summary>
internal sealed class CodeGenerationDapperAotMaterializerContributor : IDapperAotMaterializerContributor
{
    public void RegisterMaterializers(DapperAotMaterializerRegistrar registrar)
    {
        registrar.Register<CodeGenerationCatalogTableRow>(ReadCatalogTableRow);
    }

    private static CodeGenerationCatalogTableRow ReadCatalogTableRow(DbDataReader reader) =>
        new()
        {
            TableName = reader.GetString(0),
        };
}
#endif
