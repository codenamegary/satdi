# satdi

**S**how **A**ll **T**he **D**amn **I**cons.

A tiny Windows 11 background utility that refuses to let the operating system
hide your system tray icons ever again.

---

## The frustration

You know the ritual.

You install a new app. Or you update an existing one. It has a system tray
icon — a little status doohickey you actually want to see, because you're an
adult and you like knowing what's running. And Windows 11, being helpful,
decides *for you* that this icon should be hidden in the **"Other system tray
icons"** party room behind the little `^` chevron.

So off you go:

> **Settings** → **Personalization** → **Taskbar** → expand **Other system tray icons** → scroll → find the new app → toggle it on.

Install another app next week? Do it again.

Update an app and change its executable path? Windows treats it as brand new,
hides it, and you do it again.

*It never ends.* Every new tray icon is a fresh pilgrimage to the taskbar
settings page, and there is no "just show everything" switch, because
apparently that would be chaos.

It would not be chaos. It would be **correct**.

## What satdi does

satdi sets a single registry value for **every** tray icon, and then keeps it
set:

```
HKEY_CURRENT_USER\Control Panel\NotifyIconSettings\<icon>\IsPromoted = 1
```

- `IsPromoted = 1` → the icon is **shown** in the tray.
- `IsPromoted = 0` → the icon is **hidden** behind the `^` chevron.

New tray icons land in that registry key defaulting to `0`. satdi watches that
key and, the *instant* Windows adds a new hidden icon, flips it back to `1`.

It is **event-driven**, not a polling hack: it uses the Win32
`RegNotifyChangeKeyValue` API to sleep until the registry actually changes, so
it costs you essentially nothing while idle. A slow safety sweep every 60
seconds catches anything the OS notification mechanism drops on the floor.

No admin rights. No services. No drivers. It's all under
`HKEY_CURRENT_USER`, which is your little corner of the registry.

## What it does *not* do

- It does not fight you if *you* hide an icon on purpose... for long. It will
  set it back on the next sweep. This is a "show all the damn icons" tool,
  not a "show most of the icons" tool. You asked for all of them.
- It does not touch other users' profiles or require elevation.
- It does not phone home, install a service, or add a sketchy scheduled task.

## Install

1. Grab `satdi-<version>-win-x64.exe` from the
   [latest release](https://github.com/codenamegary/satdi/releases/latest).
2. Put it somewhere permanent, e.g. `%LOCALAPPDATA%\satdi\satdi.exe`.
3. Make it start with Windows. The simplest way is a shortcut in your Startup
   folder:

   **Win+R** → `shell:startup` → drop a shortcut to `satdi.exe` in there.

That's it. Double-click it once if you don't want to wait for a reboot — it
sweeps immediately and then sits quietly in the background showing all the
damn icons.

### Running it on demand

```powershell
# Promote every existing tray icon right now and exit (no background process).
satdi.exe --sweep
```

You can also just run `satdi.exe` with no arguments: it sweeps once, then
stays resident and watches for new icons.

## How it works (the short version)

1. Open `HKCU\Control Panel\NotifyIconSettings` and set `IsPromoted = 1` on
   every subkey. Each subkey is one tray icon.
2. Call `RegNotifyChangeKeyValue` on that key (whole subtree, `Name` filter)
   and block on it. New tray icon = new subkey = the call returns.
3. Sweep again. Repeat forever.

Built as a single self-contained `.exe` with the .NET 10 SDK — no runtime
install required, no console window, ~10 MB standing still.

## Building from source

Requires the [.NET 10 SDK](https://dotnet.microsoft.com/download).

```powershell
dotnet publish src/satdi/satdi.csproj -c Release -r win-x64 -o publish
```

The result is `publish/satdi.exe`: a single, self-contained, trimmed Windows
executable.

## Releases

Versioning and changelogs are handled by
[release-please](https://github.com/googleapis/release-please) from
[Conventional Commits](https://www.conventionalcommits.org/). Merge the
release PR it opens, and CI builds and attaches the `.exe` to the new GitHub
Release automatically.

## License

MIT. See [LICENSE](LICENSE).

Show all the damn icons.
