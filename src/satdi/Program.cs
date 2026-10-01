using Microsoft.Win32;
using System.Runtime.Versioning;

namespace Satdi;

/// <summary>
/// satdi — Show All The Damn Icons.
///
/// Windows 11 hides every *new* system tray icon by default. Each tray icon
/// gets a subkey under:
///
///     HKEY_CURRENT_USER\Control Panel\NotifyIconSettings
///
/// and the subkey's "IsPromoted" DWORD decides whether the icon is shown
/// (1) or banished to the "Other system tray icons" overflow (0). New apps
/// and updates create fresh subkeys that default to 0, which is why you end
/// up in Taskbar settings toggling icons on every time you install something.
///
/// This program:
///   1. Sweeps the key once on launch and sets IsPromoted = 1 everywhere.
///   2. Subscribes to registry change notifications for that key and
///      re-sweeps the instant Windows adds another hidden icon.
///
/// No admin rights required: it's all HKEY_CURRENT_USER.
/// </summary>
internal static class Program
{
    private const string NotifyIconSettingsKey = @"Control Panel\NotifyIconSettings";
    private const string IsPromotedValue = "IsPromoted";

    /// <summary>
    /// Entry point. Kept as a normal <c>Main</c> (no [STAThread] needed — we
    /// touch no UI) so it runs cleanly as a WinExe background process.
    /// </summary>
    [SupportedOSPlatform("windows")]
    private static void Main(string[] args)
    {
        // A single "--sweep" run is handy for testing / manual invocation:
        //     satdi.exe --sweep
        // Sweep once, print nothing, and exit.
        bool sweepOnce = args.Any(a => string.Equals(a, "--sweep", StringComparison.OrdinalIgnoreCase));

        // Make sure a second copy doesn't fight the first. A named mutex is
        // the cheapest "is this already running?" check on Windows.
        using var single = new Mutex(initiallyOwned: true, name: @"Global\satdi-singleton", out bool isNew);
        if (!isNew && !sweepOnce)
        {
            // Already running; nothing to do.
            return;
        }

        try
        {
            ShowAllTheDamnIcons(promoted: true);

            if (sweepOnce)
            {
                return;
            }

            // From here on we're a resident, event-driven watcher.
            WatchForNewIcons();
        }
        catch (Exception ex)
        {
            // Never crash a background process noisily. Keep a breadcrumb so
            // "why did satdi die?" is at least answerable.
            TryLog($"satdi fatal error: {ex}");
        }
    }

    /// <summary>
    /// Walks every subkey under NotifyIconSettings and forces IsPromoted = 1.
    /// Returns the number of subkeys touched.
    /// </summary>
    [SupportedOSPlatform("windows")]
    private static int ShowAllTheDamnIcons(bool promoted)
    {
        int count = 0;

        using RegistryKey? key = Registry.CurrentUser.OpenSubKey(NotifyIconSettingsKey, writable: true);
        if (key is null)
        {
            // No tray icons have ever been configured yet. Nothing to do.
            return 0;
        }

        foreach (string subKeyName in key.GetSubKeyNames())
        {
            using RegistryKey? sub = key.OpenSubKey(subKeyName, writable: true);
            if (sub is null)
            {
                continue;
            }

            object? current = sub.GetValue(IsPromotedValue);
            int desired = promoted ? 1 : 0;

            // Only write when we actually need to; avoids pointless churn
            // (and the registry-change storm that churn would cause).
            if (current is int value && value == desired)
            {
                continue;
            }

            sub.SetValue(IsPromotedValue, desired, RegistryValueKind.DWord);
            count++;
        }

        return count;
    }

    /// <summary>
    /// Auto-reset event the registry watcher blocks on.
    /// </summary>
    private static readonly AutoResetEvent ChangeEvent = new(initialState: false);

    /// <summary>
    /// Resides in memory and re-sweeps whenever a new tray icon shows up.
    ///
    /// The signal we rely on is the creation/removal of subkeys under
    /// NotifyIconSettings: every tray icon gets its own subkey, so "a new app
    /// appeared" == "a new subkey appeared". We register a <em>synchronous</em>
    /// (asynchronous: false) RegNotifyChangeKeyValue with the Name filter and
    /// an event handle; the call blocks on the calling thread until a subkey
    /// changes, at which point the event is signalled and we sweep + re-arm.
    ///
    /// Note on the API's quirky corners, learned the hard way:
    ///  - The asynchronous (true) form needs the event created with special
    ///    access and fails silently with a managed AutoResetEvent, so we use
    ///    the synchronous form instead.
    ///  - Passing a real event handle (rather than IntPtr.Zero) is what makes
    ///    the block reliably wake; IntPtr.Zero did not fire in testing.
    ///
    /// A slow periodic sweep runs alongside it as belt-and-braces: registry
    /// notifications are coalesced by the OS and can, very rarely, be missed.
    /// The timer also catches a user manually hiding an icon.
    /// </summary>
    [SupportedOSPlatform("windows")]
    private static void WatchForNewIcons()
    {
        while (Registry.CurrentUser.OpenSubKey(NotifyIconSettingsKey) is null)
        {
            // Key doesn't exist yet (brand-new profile). Wait for Windows to
            // create it, then start watching.
            Thread.Sleep(TimeSpan.FromSeconds(5));
            ShowAllTheDamnIcons(promoted: true);
        }

        // Background safety net: sweep every 60 seconds no matter what.
        var safetySweep = new Thread(() =>
        {
            while (true)
            {
                Thread.Sleep(TimeSpan.FromSeconds(60));
                try
                {
                    ShowAllTheDamnIcons(promoted: true);
                }
                catch (Exception ex)
                {
                    TryLog($"safety sweep recovered: {ex.Message}");
                }
            }
        })
        {
            IsBackground = true,
            Name = "satdi-safety-sweep",
        };
        safetySweep.Start();

        // Primary loop: block on subkey changes, sweep, repeat.
        while (true)
        {
            // Read access is enough: KEY_READ on the parent includes KEY_NOTIFY.
            using RegistryKey? key = Registry.CurrentUser.OpenSubKey(NotifyIconSettingsKey);
            if (key is null)
            {
                // Key got recreated out from under us; try again shortly.
                Thread.Sleep(TimeSpan.FromSeconds(1));
                continue;
            }

            try
            {
                // Blocks (synchronously) until a subkey is added or removed
                // anywhere beneath NotifyIconSettings; the return value is
                // deliberately ignored — the sweep is idempotent regardless.
                NativeMethods.RegNotifyChangeKeyValue(
                    key.Handle.DangerousGetHandle(),
                    watchSubtree: true,
                    filter: NativeMethods.RegChangeNotifyFilter.Name,
                    eventHandle: ChangeEvent.SafeWaitHandle.DangerousGetHandle(),
                    asynchronous: false);

                // A tray icon (dis)appeared — show all the damn icons again.
                ShowAllTheDamnIcons(promoted: true);
            }
            catch (Exception ex)
            {
                TryLog($"watch loop recovered: {ex.Message}");
                Thread.Sleep(TimeSpan.FromSeconds(1));
            }
        }
    }

    [SupportedOSPlatform("windows")]
    private static void TryLog(string message)
    {
        try
        {
            string path = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "satdi",
                "satdi.log");
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.AppendAllText(path, $"{DateTimeOffset.Now:O}  {message}{Environment.NewLine}");
        }
        catch
        {
            // If we can't even log, give up silently. It's a tray icon fixer.
        }
    }
}
