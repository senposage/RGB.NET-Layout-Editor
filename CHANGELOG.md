# Changelog

## Unreleased

### Canvas Overhaul
- Replaced WPF ItemsControl rendering with a custom `LayoutCanvas` control using direct `OnRender` drawing. Dramatically faster with large LED counts.
- Mouse wheel zoom with zoom percentage displayed in status bar.
- Middle-click pan.
- Configurable grid overlay with selectable grid color.
- Snap-to-grid for drag operations.
- LED context menu (right-click) with alignment, distribution, and editing options.
- Circle shape rendering — LEDs with `Shape.Circle` now draw as ellipses.
- Selection rectangle — Shift+drag in empty area draws a box to select multiple LEDs.
- Status bar shows selected LED ID, position, and size.

### Alignment and Distribution Tools
- **Align Top / Bottom / Left / Right** — Snap all selected LEDs to the edge of the outermost LED in the selection.
- **Space evenly (horizontal)** — Distribute selected LEDs with equal gaps along the X axis (Ctrl+H).
- **Space evenly (vertical)** — Distribute selected LEDs with equal gaps along the Y axis (Ctrl+J).
- **Match Width / Match Height** — Set all selected LEDs to the same width or height as the primary selected LED.

### Multi-Select Improvements
- Batch resize — Change width/height on one LED and it applies to all selected.
- Batch shape change — Changing shape propagates to all selected LEDs.
- Multi-select drag — LEDs no longer drift apart when dragging a group. Uses stored start positions for correct delta calculation.

### Undo / Redo
- Batch undo — Multi-LED operations (drag, resize, align) undo as a single step.
- Full redo stack alongside undo.

### OpenRGB Integration
- Live TCP connection to OpenRGB SDK server (port 6742).
- Hover and selection highlighting — Selected or hovered LEDs light up on the physical device.
- Auto-fill from device — Pull the LED list from an OpenRGB device and auto-place using matrix positions, scaled to fit the device image.
- LED name mapping — Comprehensive dictionary mapping OpenRGB names to RGB.NET `LedId` enum names for symbol keys, numpad, modifiers, navigation, and media keys.
- Unmatched LEDs (secondary per-key LEDs, palm rest, etc.) are automatically assigned `Keyboard_Custom{N}` IDs for Artemis compatibility.
- Custom LED highlighting — `Keyboard_Custom{N}` LEDs correctly highlight on the physical device when selected in the editor.

### Save / Load
- **Save As** — File > Save As to save to a new location.
- **Automatic backups** — Pre-save and post-save backups stored in `.layout-backups/` alongside the layout file.
- **Device image persistence** — `CustomData/DeviceImage` is injected into the XML on save. Image file is copied next to the layout.
- **Device image fallback resolution** — Checks absolute path, relative to layout, same directory, then absolute from CustomData.
- **Dimension sync** — When a layout has no dimensions set, the editor auto-sizes from the device image and writes those values back so they stay consistent.
- **Proportional rescaling** — Changing device Width/Height rescales all LED positions and sizes proportionally.
- **Width/Height apply on focus loss** — Dimension fields only commit when you tab or click away, preventing per-keystroke disruption.
- **Unit normalization on load** — Layouts are normalized to `LedUnitWidth=1` on load so all editor operations work in absolute millimeters.

### Bug Fixes
- Fixed LED delete crash (NullReferenceException from stale selection references).
- Fixed LED position corruption on save/reload (unit multiplier normalization).
- Fixed device image lost on reload.
- Fixed multi-select drag causing LEDs to fly apart.
- Fixed batch shape change not working.
- Fixed partial name matching false positives (e.g., Fn key lighting up C).
- Fixed OpenRGB Fn key highlighting (tries both "Key: Right Fn" and "Key: Left Fn").
- Removed empty-ID LEDs from auto-fill output.
- Thinner LED borders for cleaner appearance when zoomed in.

### Infrastructure
- Upgraded to .NET 9.
- Updated RGB.NET NuGet packages.
- Added OpenRGB.NET v3.1.1 dependency.
- LED rename dialog.
