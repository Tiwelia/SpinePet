# Atlas cleaner

This optional utility removes atlas regions whose names do not appear in the
matching Spine skeleton binary.

Because the comparison is heuristic, the cleaner creates a `.bak` copy beside
each changed atlas by default. Use `-WhatIf` to preview changes without writing.

```powershell
.\Clean-AllAtlases.ps1
.\Clean-AllAtlases.ps1 -WhatIf
.\Clean-Atlas.ps1 -Folder E:\SpinePet\res\CharacterName
```
