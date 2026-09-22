# Contributing

## Development

The desktop GUI uses Avalonia with the Fluent theme. It currently targets Windows
on .NET 10, matching Core and the MSI installer.

```powershell
dotnet run --project src/GUI/GUI.csproj -p:Platform=x64
dotnet test tests/GUI.Tests/GUI.Tests.csproj -p:Platform=x64
```

The GUI tests use Avalonia's headless backend and do not modify a game installation.

The project is currently in a state of flux. Accepting contributions is going
to be challenging for a few weeks until it converges to a stable state.

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