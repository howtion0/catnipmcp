namespace Catnip.Runtime.IntegrationTests;

public sealed class TestPipelineSmokeTests
{
    [Fact]
    public void TestProjectIsDiscoverable()
    {
        Assert.Equal(
            "Catnip.Runtime.IntegrationTests",
            typeof(TestPipelineSmokeTests).Assembly.GetName().Name);
    }
}
