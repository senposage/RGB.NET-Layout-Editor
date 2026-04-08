
# RGB.NET Layout Editor

[![Build Status](https://dev.azure.com/artemis-rgb/Artemis/_apis/build/status/RGB.NET%20Layout%20Editor%20build?branchName=master)](https://dev.azure.com/artemis-rgb/Artemis/_build/latest?definitionId=5&branchName=master)

A visual editor for [RGB.NET](https://github.com/DarthAffe/RGB.NET) device layout XML files, used by [Artemis 2](https://github.com/Artemis-RGB/Artemis) and other RGB.NET-based software.

Example layouts can be found at https://github.com/DarthAffe/RGB.NET-Resources

## Building

Requires .NET 9 SDK. Clone and build:

```
dotnet build src/LayoutEditor.UI/LayoutEditor.UI.csproj
```

## Features

### Canvas

- **High-performance rendering** — Custom `LayoutCanvas` using direct WPF `OnRender` drawing. Handles hundreds of LEDs without slowdown.
- **Zoom** — Mouse wheel to zoom in/out. Zoom level shown in the status bar.
- **Pan** — Middle-click and drag to pan the view.
- **Grid overlay** — Toggle a configurable grid. Grid color selectable from the toolbar.
- **Snap to grid** — LEDs snap to the nearest grid intersection during drag.
- **Shape rendering** — Rectangle and Circle shapes rendered correctly.

### Selecting LEDs

- **Click** — Select a single LED.
- **Shift+Click** — Add/remove a LED from the selection.
- **Shift+Drag (empty area)** — Draw a selection rectangle to select all LEDs whose center falls inside.
- **Context menu** — Right-click for alignment, distribution, and editing options.

### Moving LEDs

- **Drag** — Click and drag selected LEDs. Multi-select drag preserves relative positions.
- **Nudge** — Arrow keys move selected LEDs by 1 unit (or by grid size if snap is enabled).

### Alignment and Distribution

Available from the right-click context menu when multiple LEDs are selected:

| Action | Description |
|--------|-------------|
| **Align Top** | Move all selected LEDs to the Y position of the topmost LED |
| **Align Bottom** | Align bottom edges to the bottommost LED |
| **Align Left** | Move all selected LEDs to the X position of the leftmost LED |
| **Align Right** | Align right edges to the rightmost LED |
| **Space evenly (H)** | Distribute LEDs with equal horizontal gaps (Ctrl+H) |
| **Space evenly (V)** | Distribute LEDs with equal vertical gaps (Ctrl+J) |
| **Match Width** | Set all selected LEDs to the same width as the primary selection |
| **Match Height** | Set all selected LEDs to the same height as the primary selection |

**Typical workflow for aligning a row:**
1. Shift+drag to select a row of LEDs
2. Right-click > Align > Align Top (snap them to the same Y)
3. Right-click > Distribute > Space evenly (horizontal)

**Resizing a group uniformly:**
1. Select one LED and set its size in the sidebar
2. Shift+click to add the rest of the group to the selection
3. Right-click > Match size > Match Width / Match Height

### Batch Operations

When multiple LEDs are selected, changes to the following properties apply to all selected LEDs:
- **Width / Height** — Set in the sidebar and click Apply
- **Shape** — Change shape (Rectangle, Circle) in the sidebar

### Undo / Redo

- **Ctrl+Z** — Undo. Multi-LED operations undo as a single step.
- **Ctrl+Y** — Redo.
- Undo/Redo buttons also available in the toolbar.

### Device Dimensions

- **Width / Height (mm)** — Set in the device properties sidebar. Values apply when you click away (focus loss).
- If the layout has no dimensions, the editor auto-sizes from the device image and saves those values.
- Changing dimensions proportionally rescales all LED positions and sizes to match.

### Device Image

- Set via the **Select device image** button in the sidebar.
- The image is saved as a `<CustomData><DeviceImage>` element in the layout XML and the file is copied next to the layout.
- On load, the editor resolves the image from: stored absolute path, relative to layout file, same directory as layout, or absolute path from CustomData.

### Save / Load

- **Ctrl+S** — Quick save to the current file.
- **File > Save As** — Save to a new location.
- **Automatic backups** — Every save creates a `_pre_` and `_post_` backup in `.layout-backups/` next to the layout file.
- Layouts are normalized to `LedUnitWidth=1` on load so all positions are in absolute millimeters.

### OpenRGB Integration

Connect to a running [OpenRGB](https://openrgb.org/) instance to visualize LED selections on physical hardware.

1. Enter the OpenRGB SDK server host and port (default: `localhost:6742`)
2. Click **Connect**
3. Select your device from the dropdown
4. Check **Enable highlighting**

**Features:**
- **Hover highlighting** — Hovering over a LED in the editor lights it up on the physical device.
- **Selection highlighting** — Selected LEDs stay lit.
- **Auto-fill from device** — Click **Auto-fill** to pull all LEDs from the OpenRGB device. LEDs are placed using matrix positions and scaled to fit the device image. LEDs with no RGB.NET match are assigned `Keyboard_Custom{N}` IDs for Artemis compatibility.

### LED Name Mapping

The editor includes a comprehensive mapping between OpenRGB LED names and RGB.NET `LedId` enum values:
- Symbol keys (`Key: -` to `Keyboard_MinusAndUnderscore`)
- Numpad keys (`Key: Number Pad 0` to `Keyboard_Num0`)
- Modifiers (`Key: Left Control` to `Keyboard_LeftCtrl`)
- Navigation (`Key: Up Arrow` to `Keyboard_ArrowUp`)
- Media keys (`Key: Media Play/Pause` to `Keyboard_MediaPlay`)
- Function keys (`Key: Right Fn` / `Key: Left Fn` to `Keyboard_Function`)

LEDs with no standard mapping (secondary per-key LEDs, palm rest LEDs, underglow, etc.) are assigned `Keyboard_Custom1` through `Keyboard_Custom99`.

### Keyboard Shortcuts

| Shortcut | Action |
|----------|--------|
| Ctrl+S | Quick save |
| Ctrl+Z | Undo |
| Ctrl+Y | Redo |
| Ctrl+H | Space evenly (horizontal) |
| Ctrl+J | Space evenly (vertical) |
| Delete | Remove selected LED |
| Arrow keys | Nudge selected LEDs |
| Mouse wheel | Zoom |
| Middle-click drag | Pan |
| Shift+click | Add to / toggle selection |
| Shift+drag (empty) | Selection rectangle |

