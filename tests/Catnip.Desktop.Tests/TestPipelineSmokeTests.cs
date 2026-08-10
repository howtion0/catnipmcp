namespace Catnip.Desktop.Tests;

public sealed class TestPipelineSmokeTests
{
    [Fact]
    public void TestProjectIsDiscoverable()
    {
        Assert.Equal("Catnip.Desktop.Tests", typeof(TestPipelineSmokeTests).Assembly.GetName().Name);
    }
}
