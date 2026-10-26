using InSalute.Core.Data;
using Xunit;

namespace InSalute.Tests;

public class SmokeTests
{
    [Fact]
    public void DbContext_type_is_available()
    {
        Assert.NotNull(typeof(AppDbContext));
    }
}
