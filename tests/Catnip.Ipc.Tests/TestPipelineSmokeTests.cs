namespace Catnip.Ipc.Tests;

public sealed class TestPipelineSmokeTests
{
    [Fact]
    public void TestProjectIsDiscoverable()
    {
        Assert.Equal("Catnip.Ipc.Tests", typeof(TestPipelineSmokeTests).Assembly.GetName().Name);
    }
}
