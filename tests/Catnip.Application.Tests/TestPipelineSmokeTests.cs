namespace Catnip.Application.Tests;

public sealed class TestPipelineSmokeTests
{
    [Fact]
    public void TestProjectIsDiscoverable()
    {
        Assert.Equal("Catnip.Application.Tests", typeof(TestPipelineSmokeTests).Assembly.GetName().Name);
    }
}
