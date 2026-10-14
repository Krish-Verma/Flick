using System;
using System.Diagnostics;
using LocalClip.Core.Diagnostics;
using Xunit;

namespace LocalClip.Core.Tests;

public class ResourceMonitorTests
{
    [Fact]
    public void TestResourceMonitorSamplesCurrentProcess()
    {
        var monitor = new ResourceMonitor();
        var sample = monitor.Sample();

        Assert.NotNull(sample);
        Assert.True(sample.AppPrivateBytes > 0, "AppPrivateBytes should be > 0");
        Assert.True(sample.AppWorkingSet > 0, "AppWorkingSet should be > 0");
        Assert.True(sample.AppHandleCount > 0, "AppHandleCount should be > 0");
        Assert.True(sample.SystemTotalMemoryBytes > 0, "SystemTotalMemoryBytes should be > 0");
        Assert.True(sample.SystemAvailableMemoryBytes > 0, "SystemAvailableMemoryBytes should be > 0");
        Assert.True(sample.TotalAppOwnedPrivateBytes >= sample.AppPrivateBytes);
        Assert.Equal(1, sample.OwnedProcessCount);
    }

    [Fact]
    public void TestResourceMonitorSamplesSubprocess()
    {
        var monitor = new ResourceMonitor();
        using var testProc = System.Diagnostics.Process.Start(new ProcessStartInfo
        {
            FileName = "cmd.exe",
            Arguments = "/c ping 127.0.0.1 -n 3 > nul",
            CreateNoWindow = true,
            UseShellExecute = false
        });

        try
        {
            var sample = monitor.Sample(testProc);
            Assert.Equal(2, sample.OwnedProcessCount);
            Assert.True(sample.CaptureHostWorkingSet >= 0);
        }
        finally
        {
            try
            {
                if (testProc != null && !testProc.HasExited)
                {
                    testProc.Kill();
                }
            }
            catch { }
        }
    }
}
