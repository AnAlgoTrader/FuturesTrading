using System;
using System.Runtime.InteropServices;

namespace FootPrintTrader;

internal class Program
{
    [DllImport("libc", SetLastError = true)]
    private static extern int setenv(string name, string value, int overwrite);

    [STAThread]
    private static void Main()
    {
        ConfigureWebKitForNvidiaLinux();

        var app = new TradingApp();
        app.Run();
    }

    // WebKitGTK's DMA-BUF renderer fails on NVIDIA under X11 (GBM buffer
    // allocation is rejected), leaving a blank window. Force shared-memory
    // buffer handoff while keeping GPU compositing.
    //
    // This must be applied natively: Environment.SetEnvironmentVariable only
    // updates .NET's managed copy, which WebKit (native getenv) never reads.
    // Must also run before the first PhotinoWindow is created. Process-scoped
    // only; an existing user-provided value is preserved.
    private static void ConfigureWebKitForNvidiaLinux()
    {
        if (!OperatingSystem.IsLinux())
            return;

        if (Environment.GetEnvironmentVariable("WEBKIT_DMABUF_RENDERER_FORCE_SHM") is not null ||
            Environment.GetEnvironmentVariable("WEBKIT_DISABLE_DMABUF_RENDERER") is not null)
            return;

        setenv("WEBKIT_DMABUF_RENDERER_FORCE_SHM", "1", 0);
    }
}
