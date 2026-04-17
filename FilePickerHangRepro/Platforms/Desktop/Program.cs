using Uno.UI.Hosting;

namespace FilePickerHangRepro;

internal class Program
{
    // Declaring Main to return Task (even without `async` and with a body that just calls
    // the synchronous host.Run()) is enough to make FileOpenPicker.PickMultipleFilesAsync()
    // hang silently on Windows. Reason: when Main returns Task/Task<int>, the C# compiler
    // makes a synthetic synchronous wrapper the actual process entry point and the [STAThread]
    // attribute on the user-written Task-returning Main is NOT propagated to that synthetic
    // entry point (known Roslyn behavior, dotnet/roslyn#21797). So the process thread starts
    // as MTA, Uno's Win32 host inherits that MTA thread, and IFileOpenDialog.Show() hangs
    // because it requires STA. Change the return type to `void` and [STAThread] is honored.
    [STAThread]
    public static /*async*/ Task Main(string[] args)
    {
        App.InitializeLogging();

        var host = UnoPlatformHostBuilder.Create()
            .App(() => new App())
            .UseX11()
            .UseLinuxFrameBuffer()
            .UseMacOS()
            .UseWin32()
            .Build();

        host.Run();
        return Task.CompletedTask;
    }
}
