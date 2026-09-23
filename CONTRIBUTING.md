# Contributing

## Development

The project is currently in a state of flux. Accepting contributions is going
to be challenging for a few weeks until it converges to a stable state.

The GUI uses [Uno Platform's Skia desktop host](https://platform.uno/docs/articles/features/using-skia-desktop.html)
with the Win32 backend. Development requires Windows x64 and the .NET 10 SDK.
The Uno SDK version is pinned in `src/GUI/GUI.csproj` and restored by NuGet.
No Windows App SDK runtime or mobile workloads are required.

Build and run the GUI from the repository root:

```powershell
dotnet build src/GUI/GUI.csproj -p:Platform=x64
dotnet run --project src/GUI/GUI.csproj -p:Platform=x64
```

The GUI retains Uno's WinUI-compatible XAML and `Microsoft.UI.Xaml` APIs.
Only Windows is supported: Core still uses the Windows registry and recycle bin.
Its plain `net10.0` target allows the Uno desktop project to reference it;
the assembly remains annotated as Windows-only.

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
