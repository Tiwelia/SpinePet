# Character resource layout migration

The application organizes all skins and render states for one official
character under a single directory:

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

Preview migration from the legacy `<name>_<skin>` folders:

```powershell
.\Migrate-CharacterResources.ps1 -WhatIf
```

Run the migration only after the preview has no conflicts:

```powershell
.\Migrate-CharacterResources.ps1
```

The script refuses to overwrite existing destination files and leaves
unrecognized files in the legacy directory.
