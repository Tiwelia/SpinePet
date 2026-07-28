# Character icon downloader

cd E:\SpinePet\tools\icons-downloader
.\Update-CharacterIcons.ps1


`Update-CharacterIcons.ps1` matches local Spine resources such as `c017_00.skel`
to the corresponding `icons-char-mi(hd)` bundle in `internal_ids.json`. It then:

1. reads the latest `dp` BaseUri from the NIKKE `Player.log`;
2. downloads only icons required by the current `res` directory;
3. decrypts each bundle with `NikkeAssetUnpacker`;
4. extracts the matching Unity sprite with UnityPy;
5. writes the PNG into the character's `icons` directory.

Encrypted and decrypted working files are also kept under the selected `res`
directory while the command runs, then removed. The downloader does not use a
system-drive cache for icon assets.

The application looks for `<character-code>_<skin-code>_icon.png` in each
character resource folder and falls back to the character texture when an icon
is unavailable:

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

## Requirements

- `internal_ids.json` beside the script
- `NikkeAssetUnpacker.exe` and its `Keys` directory under
  `tools/NikkeAssetUnpacker`
- Python with the packages listed in `requirements.txt`

Install the Python dependencies:

```powershell
python -m pip install -r .\requirements.txt
```

Preview the matches without downloading:

```powershell
.\Update-CharacterIcons.ps1 -ListOnly
```

Download missing icons:

```powershell
.\Update-CharacterIcons.ps1
```

Replace existing cached icons:

```powershell
.\Update-CharacterIcons.ps1 -Force
```

Exit SpinePet before using `-Force` so WPF is not displaying the files being
replaced. After downloading missing icons, restart the app or use the panel's
Scan button to refresh the cards.

The BaseUri normally comes from the latest matching line in `Player.log`. It can
also be supplied explicitly after a game update:

```powershell
.\Update-CharacterIcons.ps1 -BaseUri 'https://cloud.nikke-kr.com/.../pck/dp/.../'
```

`internal_ids.json`, the unpacker package, downloaded bundles, decrypted bundles,
and the entire `res` directory are local-only and must not be committed.
