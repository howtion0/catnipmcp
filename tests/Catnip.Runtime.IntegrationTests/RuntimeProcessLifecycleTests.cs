using System.Diagnostics;
using System.Security.Cryptography;
using System.Text.Json;
using Catnip.Ipc.Management;
using Catnip.Runtime.Hosting;
using Catnip.Shared.Management;
using Catnip.Shared.Serialization;

namespace Catnip.Runtime.IntegrationTests;

public sealed class RuntimeProcessLifecycleTests
{
    [Fact]
    public async Task IndependentRuntime_PingSnapshotAndShutdownExitCleanly()
    {
        string apiKey = Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
        string pipeName = PipeNames.Management($"p-{Guid.NewGuid():N}"[..10]);
        string runtimeAssembly = Path.Combine(AppContext.BaseDirectory, "Catnip.Runtime.dll");
        var startInfo = new ProcessStartInfo("dotnet")
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };
        startInfo.ArgumentList.Add(runtimeAssembly);
        startInfo.ArgumentList.Add("--urls");
        startInfo.ArgumentList.Add("http://127.0.0.1:0");
        startInfo.Environment["ASPNETCORE_ENVIRONMENT"] = "Production";
        startInfo.Environment[RuntimeApplication.InboundApiKeyEnvironmentVariable] = apiKey;
        startInfo.Environment[RuntimeApplication.ManagementPipeEnvironmentVariable] = pipeName;

        using var process = new Process { StartInfo = startInfo };
        Assert.True(process.Start());
        Task<string> standardOutput = process.StandardOutput.ReadToEndAsync(
            TestContext.Current.CancellationToken);
        Task<string> standardError = process.StandardError.ReadToEndAsync(
            TestContext.Current.CancellationToken);

        try
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(
                TestContext.Current.CancellationToken);
            timeout.CancelAfter(TimeSpan.FromSeconds(10));
            await using var client = new NamedPipeManagementClient(pipeName);
            await client.ConnectAsync(timeout.Token);

            ManagementResponse ping = await client.PingAsync(timeout.Token);
            (ManagementResponse snapshotResponse, RuntimeSnapshot? snapshot) =
                await WaitForRunningSnapshotAsync(client, timeout.Token);
            ManagementResponse shutdown = await client.ShutdownRuntimeAsync(
                graceful: true,
                timeout.Token);

            await process.WaitForExitAsync(timeout.Token);
            string output = await standardOutput.WaitAsync(timeout.Token);
            string error = await standardError.WaitAsync(timeout.Token);

            Assert.True(ping.Success);
            Assert.True(snapshotResponse.Success);
            Assert.NotNull(snapshot);
            Assert.Equal(RuntimeProcessState.Running, snapshot.ProcessState);
            Assert.Equal(
                typeof(RuntimeApplication).Assembly.GetName().Version!.ToString(3),
                snapshot.Version);
            Assert.True(shutdown.Success);
            Assert.Equal(0, process.ExitCode);
            Assert.Contains("Application is shutting down", output, StringComparison.Ordinal);
            Assert.DoesNotContain(apiKey, output, StringComparison.Ordinal);
            Assert.DoesNotContain(apiKey, error, StringComparison.Ordinal);
        }
        finally
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
                await process.WaitForExitAsync(TestContext.Current.CancellationToken);
            }
        }
    }

    private static async Task<(ManagementResponse Response, RuntimeSnapshot? Snapshot)>
        WaitForRunningSnapshotAsync(
            NamedPipeManagementClient client,
            CancellationToken cancellationToken)
    {
        while (true)
        {
            ManagementResponse response = await client.GetRuntimeSnapshotAsync(cancellationToken);
            RuntimeSnapshot? snapshot = response.Payload?.Deserialize<RuntimeSnapshot>(
                SharedJsonSerializerOptions.Create());
            if (snapshot?.ProcessState == RuntimeProcessState.Running)
            {
                return (response, snapshot);
            }

            await Task.Delay(TimeSpan.FromMilliseconds(25), cancellationToken);
        }
    }
}
