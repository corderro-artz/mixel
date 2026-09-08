# mixel

Convert transparency-supporting PNGs of pixel art into Z-extruded **glTF 2.0** models — in the browser or from the command line. Local-first, offline-capable, no upload.

Live app: **https://corderro-artz.github.io/mixel/**

## Projects

| Project | Target | What it is |
|---------|--------|------------|
| `Mixel.Core` | net8.0 | Pure library — PNG → silhouette → extruded mesh → glTF. No UI/browser deps. |
| `Mixel.Cli`  | net8.0 | Thin CLI over Core (`System.CommandLine`). |
| `Mixel.Web`  | net10.0 | Blazor WebAssembly PWA — references Core compiled to WASM. Live preview, depth painter, spritesheet slicer, 14 themes, batch/zip export. |

## Run the app locally

```bash
dotnet run --project src/Mixel.Web
```

Then open the printed URL (default **http://localhost:5036**). It's a PWA, so you can install it and it keeps working offline.

Pre-built static output lives in `artifacts/web/` — serve it with any static file server if you'd rather not run the dev server.

## Run the CLI

```bash
dotnet run --project src/Mixel.Cli -- <input.png> [-o out] [-d depth] [-s voxel-size] [-f glb|gltf|gltf-embedded] [-p bottom-center|center|min-corner]
```

Inputs can be one `.png`, several, or a directory (batch); add `-r` to recurse.

## Build & test

```bash
dotnet build mixel.slnx
dotnet test mixel.slnx
```

## Deployment

GitHub Actions (`.github/workflows/deploy.yml`) publishes `Mixel.Web` to GitHub Pages at `/mixel/` on push to `main`. The `base href` and service-worker base path are patched at publish time — don't hardcode `/mixel/` in source.

## License

MIT — see [LICENSE](LICENSE).
