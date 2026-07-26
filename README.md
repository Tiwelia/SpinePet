# SpinePet

SpinePet is a Windows desktop pet application for Spine skeleton assets. It renders
characters in a transparent, click-through WPF/WebView2 host and provides a
configuration panel for importing characters, selecting animations, scaling, and
positioning.

## Requirements

- Windows 10 or later
- .NET 9 SDK
- Microsoft Edge WebView2 Runtime
- A Spine export containing matching `.skel`, `.atlas`, and `.png` files

## Repository layout

```text
docs/                         Design and implementation notes
src/SpinePet/
  Infrastructure/             Paths and local diagnostics
  Models/                     Persisted and renderer state
  Services/                   Application and WebView services
  ViewModels/                 UI presentation state
  Views/                      WPF windows
  Web/                        PIXI/Spine renderer
    vendor/                   Third-party browser libraries
tools/atlas-cleaner/          Optional atlas maintenance utility
```

The local `res/` directory is intentionally ignored by Git because character packs
can be large and may have separate redistribution terms. Put each character in its
own folder:

```text
res/
  CharacterName/
    character.skel
    character.atlas
    character.png
```

## Build

```powershell
dotnet restore
dotnet build SpinePet.sln -c Debug
dotnet build SpinePet.sln -c Release
```

Run the Debug build:

```powershell
dotnet run --project src/SpinePet/SpinePet.csproj
```

Runtime configuration and diagnostics are stored under
`%LOCALAPPDATA%\SpinePet`.

## Third-party components

- PIXI.js 6.5.10
- pixi-spine 3.1.2

See the license headers in `src/SpinePet/Web/vendor/`. Spine runtime usage is
subject to the license distributed with `pixi-spine`.
