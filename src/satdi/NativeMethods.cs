using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace Satdi;

/// <summary>
/// The tiny slice of Win32 we need: registry change notifications.
/// </summary>
[SupportedOSPlatform("windows")]
internal static class NativeMethods
{
    /// <summary>
    /// Flags for <see cref="RegNotifyChangeKeyValue"/>. We watch both for new
    /// subkeys (Name) and for writes to existing values (LastSet), because a
    /// tray icon can either be added (new subkey) or have its IsPromoted value
    /// flipped by the OS (LastSet).
    /// </summary>
    [Flags]
    internal enum RegChangeNotifyFilter : uint
    {
        Name = 0x00000001,
        Attributes = 0x00000002,
        Value = 0x00000004,
        LastSet = 0x00000008,
        Security = 0x00000010,
    }

    /// <summary>
    /// Notifies the caller about changes to the attributes or contents of a
    /// specified registry key.
    /// </summary>
    [DllImport("advapi32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool RegNotifyChangeKeyValue(
        IntPtr hKey,
        [MarshalAs(UnmanagedType.Bool)] bool watchSubtree,
        RegChangeNotifyFilter filter,
        IntPtr eventHandle,
        [MarshalAs(UnmanagedType.Bool)] bool asynchronous);
}
