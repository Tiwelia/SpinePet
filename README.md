# SpinePet

SpinePet is a Windows desktop pet application for Spine skeleton assets. It renders
characters through the native Spine 4.1 C# runtime, Direct3D 11, and
DirectComposition, while WPF provides the configuration panel for importing
characters, selecting animations, scaling, and positioning.

## Requirements

- Windows 10 or later
- .NET 9 SDK
- A Spine export containing matching `.skel`, `.atlas`, and `.png` files

## Repository layout

```text
docs/                         Design and implementation notes
src/SpinePet/
  Infrastructure/             Paths and local diagnostics
  Models/                     Persisted application state
  Rendering/Native/           D3D11/DirectComposition Spine renderer
  Services/                   Application services and render contract
  ViewModels/                 UI presentation state
  Views/                      WPF windows
src/SpineRuntime41/            Isolated Spine 4.1 runtime assembly
third_party/spine-csharp-4.1/  Licensed Spine runtime source
tools/atlas-cleaner/          Optional atlas maintenance utility
tools/icons-downloader/       Local character icon downloader
tools/resource-layout/        Legacy resource layout migration
```

The local `res/` directory is intentionally ignored by Git because character packs
can be large and may have separate redistribution terms. Put each character in its
own folder. All skins for the same official character share the character
directory and are separated by render state:

```text
res/
  Anis Star/
    standing/
      c017_00.skel
      c017_00.atlas
      c017_00_FullNude.png
    aim/
    cover/
    icons/
      c017_00_icon.png
```

Resource file names use `c<character-code>_<skin-code>`. Character names are
resolved through `docs/CharacterNames.json`, while the skin code is shown
separately on each character card. Right-click a card, or focus it and press
Shift+F10, to switch between the available `standing`, `aim`, and `cover`
resources. New characters start in `standing`.

Character cards prefer
`icons/<resource-name>_icon.png` under the character directory. Use
`tools/icons-downloader/Update-CharacterIcons.ps1` to download and extract the
icons required by the current `res` directory.

The Add button accepts an existing `.skel` file or a UnityFS bundle whose file
name starts with:

```text
c<character-id>_<skin-id>_<standing|aim|cover>_
```

UnityFS imports require Python, UnityPy, and Pillow. Install the same dependencies
used by the icon downloader:

```powershell
python -m pip install -r .\tools\icons-downloader\requirements.txt
```

The importer extracts a complete skeleton, atlas, and all referenced atlas
textures directly into the matching state directory. It refuses to overwrite
existing character resources.

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

- Esoteric Software `spine-csharp` 4.1
- Vortice.Windows 3.8.3

Spine runtime usage is subject to the license in
`third_party/spine-csharp-4.1/LICENSE`.
