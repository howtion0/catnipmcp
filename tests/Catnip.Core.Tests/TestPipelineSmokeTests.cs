namespace Catnip.Core.Tests;

public sealed class TestPipelineSmokeTests
{
    [Fact]
    public void TestProjectIsDiscoverable()
    {
        Assert.Equal("Catnip.Core.Tests", typeof(TestPipelineSmokeTests).Assembly.GetName().Name);
    }
}
