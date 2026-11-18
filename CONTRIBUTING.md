# Contributing

## Development

Install the .NET SDK selected by `global.json`.
The GUI uses Uno Skia Desktop with Win32 hosting on Windows x64 and X11 hosting
on Linux x64. GUI builds default to the host operating system's runtime. An
explicit project-level runtime identifier overrides that default.

```powershell
dotnet build AMS2CM.sln -c Release -p:Platform=x64
dotnet test AMS2CM.sln -c Release -p:Platform=x64 --no-build --no-restore
dotnet run --project src/GUI -p:Platform=x64 -- --Game:Path="C:\TestGame"
```

On Linux, the normal solution commands automatically exclude the Windows
installer from restore, build, rebuild, clean, publish, and test operations.
Linux packaging is separate work.

To launch the Linux GUI, install the .NET 11 runtime and use an X11 desktop
session, or XWayland under Wayland. The host requires graphics drivers with
OpenGL support (such as Mesa), fontconfig, libXrandr, libXi, and D-Bus. See
[Uno's Linux prerequisites](https://platform.uno/docs/articles/features/using-skia-desktop.html#additional-setup)
for distribution-specific setup.

```bash
dotnet build AMS2CM.sln -c Release -p:Platform=x64
dotnet run --project src/GUI -c Release -p:Platform=x64 -- --Game:Path="/absolute/path/to/TestGame"
```

Linux Steam discovery reads `steamapps/libraryfolders.vdf` and finds the library
containing the configured app ID, including libraries on other drives. It looks
for configuration through native Steam aliases under `~/.steam`, the absolute
`XDG_DATA_HOME/Steam` location, and `~/.local/share/Steam`; Flatpak locations under
`~/.var/app/com.valvesoftware.Steam/{.local/share,data}/Steam`; and Snap locations
under `~/snap/steam/common/.local/share/Steam` and
`~/.snap/data/steam/common/.local/share/Steam`. Symlink aliases are deduplicated.
Unreadable or malformed configurations are reported with their paths. If the
app is absent or appears in multiple distinct libraries, supply an absolute
`Game:Path` to bypass discovery and select the game directory explicitly.

Safe deletion requires the `gio` command on `PATH`, provided by the GIO
command-line tools (the `libglib2.0-bin` package on Debian/Ubuntu). Files,
directories, and symlinks are moved to the desktop trash using `gio trash`.
Failures are reported without falling back to permanent deletion. Constructors
and service resolution do not launch GIO or require it to be installed.
Filesystem portability work and the existing Linux test failures are deferred;
a successful build does not yet imply full Linux mod-operation support.

Use a disposable game directory when testing mod operations. The GUI preserves
the existing command-line configuration, including the beta game overrides.
The installer build publishes a framework-dependent, untrimmed GUI to its
intermediate `gui-publish` directory and packages that output in the MSI.
Users must install the .NET 11 runtime for Windows x64 separately. See
[installer validation](src/Installer/README.md) for Windows Sandbox checks.

Core and CLI target plain .NET. Defaults for `ISteamLibraryLocator` and
`ISafeFileDelete` are registered behind OS guards and can be overridden before
calling `AddCoreServices`. Future desktop hosts must supply their own platform
services. Shared UI code uses Uno APIs rather than Windows App SDK or WinUIEx.

Uno requires explicit XAML base types in the four code-behind classes. Rider
may report these declarations as redundant; retain them to avoid Uno's
`UXAML0002` warnings. This inspection exception is approved for this migration.

## Releasing

Assuming we want to release version 1.2.3

- Tag the release and push it to the main repo
  ```
  git tag -a v1.2.3 -m "Release 1.2.3"
  git push origin 1.2.3
  ```
- Create a [new release](https://github.com/OpenSimTools/AMS2CM/releases/new)
  with that tag.
  - Set title to "v1.2.3"
  - Press "Generate release notes" and filter only what is relevant from the
    "What's Changed" section
  - Upload artefacts from the tag build.
  - "Set as a pre-release" if applicable
  - "Publish release"
