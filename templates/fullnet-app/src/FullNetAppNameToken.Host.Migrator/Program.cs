using Full.NET.Composition;
using Full.NET.Hosting.Migrator;
using FullNetAppNameToken.Composition;

// 应用拥有迁移入口；只通过最小Migrator Profile装配自身模块，不装入API运行能力。
var builder = FullNetMigratorHost.CreateBuilder(args);
builder.Services.AddApplicationModules(builder.Configuration, FullNetHostProfile.Migrator);
return await FullNetMigratorHost.RunAsync(builder, args);
