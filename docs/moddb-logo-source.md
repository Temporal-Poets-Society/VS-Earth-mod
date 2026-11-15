# Earth logo source

The selected publication logo uses the real map render at `Earth/preview/global.png`, wrapped around a cylinder by `Earth/tools/make-logo.py`. The layout, cylindrical projection, polar cap, arrow and typography follow `Cworld/tools/make-logo.py`, matching the author's supplied Cylinder World reference.

Since Earth 2.0 the title carries the gold mark `v2` (`--badge v2`, the author, 15 November 2026). Final upload: `docs/moddb-logo.png`, exactly 480 × 320 pixels. Master: `docs/moddb-logo-master.png`, 1440 × 960 pixels. Both use the original raster as the cylinder's surface texture, with lighting applied to show its curved shape.

From the Earth repository, reproduce them with:

```powershell
python tools/make-logo.py preview/global.png docs/moddb-logo.png --badge v2
python tools/make-logo.py preview/global.png docs/moddb-logo-master.png --badge v2 --scale 3
```

The renderer uses NumPy and Pillow. No dataset is downloaded and no game, package or save is changed. Upload the 480 × 320 file through Screenshots, then select it as the ModDB logo.

The earlier imagegen concept is retained separately as `moddb-logo-generated-concept.png` and `moddb-logo-generated-concept-master.png`. It is not the selected logo; the author requested the actual map render instead.
