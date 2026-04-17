# Uno Skia Win32 — `FileOpenPicker` hangs when `Main` returns `Task`

Minimal repro for an Uno Platform Skia Desktop (Win32) bug on Windows:
`FileOpenPicker.PickMultipleFilesAsync()` silently hangs inside
`IFileOpenDialog.Show()` when `Program.Main` returns `Task` (async or not).
No dialog is ever rendered; the app becomes unresponsive.

## Environment

- Uno.Sdk **6.5.31** (also reproduced on 6.3.28)
- Target framework: **net10.0-desktop**
- Program.cs uses `.UseWin32()`
- Windows 11

## Repro steps

1. `dotnet build FilePickerHangRepro -f net10.0-desktop -c Release`
2. Run `FilePickerHangRepro/bin/Release/net10.0-desktop/FilePickerHangRepro.exe`
3. Click **Pick files...**

## Expected

A native file-open dialog appears; selecting files returns them to the app.

## Actual

The main window freezes. No dialog appears. Alt+Tab does not reveal any
file-picker window. The call stack is blocked inside
`IFileOpenDialog.Show()`:

```
[Managed to Native Transition]
IFileOpenDialog.Show()
Win32FileFolderPickerExtension.PickFiles()
Win32FileFolderPickerExtension.PickMultipleFilesAsync()
FileOpenPicker.PickMultipleFilesTaskAsync()
...
MainPage.OnPickClicked()
...
Win32WindowWrapper.WndProc()
```

## Root cause

The trigger is the **return type of `Main`**, not the `async` keyword.
Any `Task`-returning `Main` hangs — including a non-`async` Main that
just calls the synchronous `host.Run()` and returns `Task.CompletedTask`.
A `void`-returning `Main` with the same body works.

Reason: when `Main` returns `Task` / `Task<int>`, the C# compiler makes a
synthetic **synchronous** wrapper the actual process entry point. The
user-written `Task Main` is called from that wrapper and awaited.
`[STAThread]` is left on the user's Task-returning method — which is
**not** the real entry point — so it has no effect (known Roslyn
behavior, [dotnet/roslyn#21797](https://github.com/dotnet/roslyn/issues/21797)).
The process thread starts as MTA, Uno's Win32 message loop inherits that
MTA thread, and `IFileOpenDialog.Show()` hangs because it requires STA.

Uno's Win32 host does not re-establish STA. `Win32Host` calls
`PInvoke.OleInitialize()` on the dispatcher thread, but `OleInitialize`
*requires* the thread to already be STA; it does not change the apartment.

Relevant upstream paths (on `release/stable/6.3`):

- `src/Uno.UI/Hosting/UnoPlatformHost.cs` — `RunAsync()` just awaits
  `RunCore()` which is synchronous; it does not marshal to an STA thread.
- `src/Uno.UI.Runtime.Skia.Win32/Hosting/Win32Host.cs` — calls
  `OleInitialize` but no `Thread.TrySetApartmentState` /
  `CoInitializeEx(APARTMENTTHREADED)`.
- `src/Uno.UI.Runtime.Skia.Win32/Storage/Pickers/Win32FileFolderPickerExtension.cs` —
  calls `iFileOpenDialog.Value->Show(hwnd)` synchronously on whatever thread
  the caller is on.

## Workaround

Keep `Main` synchronous and block on `RunAsync()` so the STA thread is
reused end-to-end:

```csharp
[STAThread]
public static void Main(string[] args)
{
    var host = UnoPlatformHostBuilder.Create()
        .App(() => new App())
        .UseX11()
        .UseLinuxFrameBuffer()
        .UseMacOS()
        .UseWin32()
        .Build();

    host.RunAsync().GetAwaiter().GetResult();
}
```

Flip the return type of `Main` between `Task` and `void` in this repro's
`Program.cs` to toggle between hang and working — the body is otherwise
identical (synchronous `host.Run()` call).

## Suggested fixes (upstream)

- Re-establish STA on the Win32 dispatcher thread
  (`Thread.TrySetApartmentState(ApartmentState.STA)` /
  `CoInitializeEx(COINIT_APARTMENTTHREADED)`) before running the message
  loop.
- Or marshal the picker call onto an STA thread inside
  `Win32FileFolderPickerExtension.PickFiles()`.
- Document the STA requirement for users with a `Task`-returning `Main`;
  the default template's `Main` is `void`, but any app that switches to
  `async Task Main` (or even a non-async `Task Main`) will silently trip
  this.

## Related

- [unoplatform/uno#21672](https://github.com/unoplatform/uno/issues/21672)
  (FolderPicker hang, closed as fixed in 6.4.53) — fixed the HWND fallback
  but not the apartment issue.
- [unoplatform/uno#19752](https://github.com/unoplatform/uno/pull/19752) /
  commit [`826ca42`](https://github.com/unoplatform/uno/commit/826ca42) —
  changed picker to use `GetActiveWindow()` +
  `Win32WindowWrapper.GetHwnds().First()`; does not address STA.
