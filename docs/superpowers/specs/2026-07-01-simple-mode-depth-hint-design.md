# Simple Mode Depth Hint — Design

- **Date:** 2026-07-01
- **Status:** Approved
- **Scope:** Web only. No Core changes.

## 1. Problem & Goal

When a user paints depth levels in the `DepthPainter` and then switches to (or remains in) Simple mode, the painted depths are silently ignored during extrusion. There is no visual signal that the depth data exists but is being discarded. This is confusing because the overlay labels are still visible on the canvas.

**Goal:** Show a concise inline hint inside `DepthPainter` when Simple mode is active and the current item has at least one pixel painted beyond the default level 1 — making clear why the 3D preview doesn't reflect the painted depths, and pointing the user toward the fix.

## 2. Trigger Condition

The hint is shown when **both** of the following are true:

1. `Settings.PerPixelMode == false` (Simple mode active)
2. `Item?.DepthLevels?.Any(l => l > 1) == true` (at least one pixel painted to depth > 1)

The `Any(l => l > 1)` check short-circuits on the first match and is fast enough for all practical pixel art sizes. It does not require new state or memoisation.

Erased pixels (level 0 on opaque pixels) are not included in the trigger — the most meaningful signal is a user who has actively painted higher depth values.

## 3. Design

### 3.1 Changes

**`DepthPainter.razor`** — add computed property and conditional markup:

```csharp
private bool ShowSimpleModeHint =>
    !Settings.PerPixelMode &&
    Item?.DepthLevels?.Any(l => l > 1) == true;
```

Markup inserted between `.painter-tools` and `.depth-canvas-wrap`:

```razor
@if (ShowSimpleModeHint)
{
    <p class="depth-mode-hint">Painted depths ignored in Simple mode — switch to Per-pixel to use them.</p>
}
```

**`app.css`** — new rule appended:

```css
.depth-mode-hint { font-size: 12px; color: var(--mut); background: var(--bg2); border-radius: 6px; padding: 4px 10px; margin: 0; }
```

Muted text on a subtle background — visible but not alarming. Consistent with `.slicer-hint` and `.depth-painter`'s existing padding/gap.

### 3.2 What does NOT change

- No changes to `Home.razor`, `OptionsPanel.razor`, `ExtrudeSettings`, or any Core type.
- The hint is not dismissible — it disappears automatically when `PerPixelMode` becomes `true` or when all depth levels are reset to 1.
- No button in the hint (the mode toggle is on-screen in the options widget).

## 4. Testing

- `DepthPainterStateTests` — no changes needed; hint logic is a pure computed property, no state mutations.
- Manual: load sample → switch to Per-pixel → paint any pixel to level > 1 → switch back to Simple → hint appears. Switch to Per-pixel → hint gone.
