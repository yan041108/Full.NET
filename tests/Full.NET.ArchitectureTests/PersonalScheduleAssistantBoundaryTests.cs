using Full.NET.Modules.Ai;
using Full.NET.Modules.Calendar;
using Full.NET.Modules.Calendar.Contracts;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Configuration;
using System.Reflection;
using System.Text.Json;
using System.Text.RegularExpressions;
using Full.NET.Data.Abstractions;

namespace Full.NET.ArchitectureTests;

/// <summary>首个真实业务消费者必须经独立窄契约及显式运行依赖，不反向耦合模块实现。</summary>
[TestClass]
public sealed class PersonalScheduleAssistantBoundaryTests
{
    [TestMethod]
    public void Worker_registers_both_source_owner_and_ai_consumer_without_calendar_http_management()
    {
        var services=new ServiceCollection();var configuration=new ConfigurationBuilder().Build();
        new CalendarModule().AddBackgroundServices(services,configuration);new AiModule().AddBackgroundServices(services,configuration);
        Assert.IsTrue(services.Any(value=>value.ServiceType==typeof(IPersonalScheduleKnowledgeSource)));
        Assert.IsTrue(services.Any(value=>value.ServiceType==typeof(IPersonalScheduleCompletion)));
        Assert.IsTrue(services.Any(value=>value.ServiceType.FullName=="Full.NET.Modules.Ai.Knowledge.AiPersonalScheduleSourceReader"));
        Assert.IsFalse(services.Any(value=>value.ServiceType.FullName=="Full.NET.Modules.Calendar.Features.ManageMyPersonalSchedules.PersonalScheduleManagementService"));
    }
    [TestMethod]
    public void Ai_consumes_calendar_contract_without_referencing_calendar_implementation()
    {
        var references = typeof(AiModule).Assembly.GetReferencedAssemblies().Select(value => value.Name).ToArray();
        CollectionAssert.Contains(references, typeof(IPersonalScheduleKnowledgeSource).Assembly.GetName().Name);
        CollectionAssert.DoesNotContain(references, typeof(CalendarModule).Assembly.GetName().Name);
        CollectionAssert.Contains(new AiModule().Dependencies.ToArray(), "Calendar");
    }

    [TestMethod]
    public void Calendar_owns_the_contract_and_has_no_reverse_ai_dependency()
    {
        var references = typeof(CalendarModule).Assembly.GetReferencedAssemblies().Select(value => value.Name ?? "").ToArray();
        CollectionAssert.Contains(references, typeof(IPersonalScheduleKnowledgeSource).Assembly.GetName().Name);
        Assert.IsFalse(references.Any(name => name.StartsWith("Full.NET.Modules.Ai", StringComparison.Ordinal)));
        Assert.IsFalse(new CalendarModule().Dependencies.Contains("Ai", StringComparer.Ordinal));
        // 原任务后台核证复用Identity所有的稳定会话绑定，契约仍不引用任何模块实现或Ai。
        CollectionAssert.AreEqual(new[] { "Full.NET.Modules.Identity.Contracts" }, typeof(IPersonalScheduleKnowledgeSource).Assembly.GetReferencedAssemblies()
            .Select(reference => reference.Name).Where(name => name?.StartsWith("Full.NET.Modules.", StringComparison.Ordinal) == true).ToArray());
    }

    [TestMethod]
    public void Completion_sql_stays_in_its_owner_and_every_global_statement_has_an_exact_scope_catalog_entry()
    {
        using var catalog=JsonDocument.Parse(File.ReadAllText(Path.Combine(ArchitectureRepositoryRoot.Find(),"contracts/architecture/global-sql-statements.json")));
        var entries=catalog.RootElement.GetProperty("entries").EnumerateArray().ToArray();var checkedStatements=0;
        foreach(var owner in new[]{(Assembly:typeof(AiModule).Assembly,Type:"Full.NET.Modules.Ai.Persistence.AiScheduleCompletionSql",Prefix:"fn_ai_"),
            (Assembly:typeof(CalendarModule).Assembly,Type:"Full.NET.Modules.Calendar.Persistence.PersonalScheduleCompletionSql",Prefix:"fn_calendar_")})
        {
            var type=owner.Assembly.GetType(owner.Type,true)!;
            foreach(var field in type.GetFields(BindingFlags.Static|BindingFlags.Public|BindingFlags.NonPublic).Where(value=>value.FieldType==typeof(SqlStatement)))
            {
                var statement=(SqlStatement)field.GetValue(null)!;Assert.AreEqual(SqlDataScope.Global,statement.Scope);
                foreach(Match table in Regex.Matches(statement.Text,@"\bfn_[a-z0-9_]+\b"))
                    Assert.IsTrue(table.Value.StartsWith(owner.Prefix,StringComparison.Ordinal),"完成链路SQL不得跨模块读写权威表。");
                var entry=entries.Single(value=>value.GetProperty("statementName").GetString()==statement.Name);
                Assert.AreEqual(owner.Type+"."+field.Name,entry.GetProperty("declaration").GetString());
                foreach(var fragment in entry.GetProperty("requiredSqlFragments").EnumerateArray())StringAssert.Contains(statement.Text,fragment.GetString()!);
                checkedStatements++;
            }
        }
        Assert.AreEqual(14,checkedStatements,"新增语句必须扩展此聚焦边界，不借全仓扫描超时跳过新SQL登记。");
    }
}
