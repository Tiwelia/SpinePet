# SpinePet brand exports

`Generate-BrandAssets.ps1` turns the approved square avatar artwork into the
deterministic files embedded by the application:

```powershell
.\tools\brand-assets\Generate-BrandAssets.ps1 -Source <approved-avatar.png>
```

Python 3 and Pillow are required. The script keeps the circular badge inside a
transparent safe area, exports 1024, 512, and 256 pixel PNGs, and writes a
multi-frame Windows ICO containing 16 through 256 pixel variants.

The generated manifest records the sizes, safe area, and approved palette.
