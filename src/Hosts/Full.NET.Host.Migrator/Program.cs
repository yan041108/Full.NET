using Full.NET.Composition;
using Full.NET.Hosting.Migrator;

var builder = FullNetMigratorHost.CreateBuilder(args);
builder.Services.AddFullNetApplicationModules(builder.Configuration, FullNetHostProfile.Migrator);
return await FullNetMigratorHost.RunAsync(builder, args);

/// <summary>Full.NET一次性迁移入口；共享生命周期只在Migrator运行，模块通过显式Profile装配。</summary>
public partial class Program;
