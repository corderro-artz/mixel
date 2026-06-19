# mixel Web — Manual Acceptance

Run: `dotnet run --project src/Mixel.Web` (or serve `artifacts/web/wwwroot`).

1. App loads in Vaporsoft Dark theme; top bar shows `mixel` + `PWA · offline`.
2. Drop a transparent PNG → it appears in the file list, selected, and the 3D
   model renders in the center stage; orbit with mouse drag.
3. Change Depth / Voxel size / Pivot → preview regenerates.
4. Theme picker: switch among all 14 themes → colors update live; reload →
   the chosen theme persists.
5. Export (glb) → a `.glb` downloads and opens in Blender/Windows 3D Viewer.
6. Export gltf / gltf-embedded → correct file(s) download (gltf → .zip).
7. Drop multiple PNGs → "Export all (.zip)" downloads a zip with one output each.
8. Install the PWA (browser install prompt). Stop the dev server / go offline →
   reopen the installed app → it still loads and extrudes (no network).
