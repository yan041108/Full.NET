using Full.NET.Data.Dapper;

namespace Full.NET.UnitTests.Data;

[TestClass]
public sealed class DatabaseAdmissionPriorityScopeTests
{
    [TestMethod]
    public void EnterCritical_SupportsNestingAndRestoresNormalPriority()
    {
        var priority = new DatabaseAdmissionPriorityScope();
        Assert.IsFalse(priority.IsCritical);

        using (priority.EnterCritical())
        {
            Assert.IsTrue(priority.IsCritical);
            using (priority.EnterCritical())
            {
                Assert.IsTrue(priority.IsCritical);
            }

            Assert.IsTrue(priority.IsCritical);
        }

        Assert.IsFalse(priority.IsCritical);
    }
}
