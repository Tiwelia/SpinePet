# Atlas cleaner

This optional utility removes atlas regions whose names do not appear in the
matching Spine skeleton binary.

The cleaner compares each complete atlas region name with the UTF-8 strings in
the matching skeleton. Region names containing spaces are supported. When an
unreferenced region is removed, its complete property block is removed with it.

The cleaner creates a `.bak` copy beside each changed atlas by default and never
overwrites an existing backup. Use `-WhatIf` to preview changes without writing.

```powershell
.\Clean-AllAtlases.ps1
.\Clean-AllAtlases.ps1 -WhatIf
.\Clean-Atlas.ps1 -Folder E:\SpinePet\res\CharacterName\00\standing
```

`Clean-AllAtlases.ps1` searches only `standing` directories recursively, so
legacy `aim` and `cover` resources remain untouched. `Clean-Atlas.ps1` pairs
each `.skel` with the atlas of the same base name.
