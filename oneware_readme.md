## Overview

FSM Editor is a OneWare Studio extension for designing and exporting Finite State Machines. Draw states and transitions on a graphical canvas, define signals and variables, then generate **VHDL** or **C code** with a single click.

---

## Features

- **Visual canvas** — drag-and-drop states, draw transitions by hovering a state and dragging to another, inline label editing, marquee selection
- **Grid & snap** — toggleable grid background and snap-to-grid (50-unit grid)
- **Zoom & pan** — scroll to zoom, right-click-drag to pan; Zoom In/Out buttons with live % display
- **Canvas overlay** — filename and graph type shown in the top-left corner at all times
- **Moore / Mealy** — switch graph type at any time; Moore outputs on states, Mealy outputs on transitions
- **Signals** — collapsible panel with count badge; each signal has Name, Direction (IN/OUT/INOUT), Type (BIT / BIT_N / SIGNED / UNSIGNED) and Size
- **Variables** — collapsible panel with count badge; internal variables with Name, Type and Size (no direction)
- **Code generation** — Generate VHDL, Generate C, and Verify buttons in the Backend panel
- **Undo / Redo** — full history including signal and variable changes
- **`.fsmxml` file format** — XML-based, SCXML-compatible

### Signal & Variable Types

| UI type | Size | Saved as |
|---------|------|----------|
| BIT | — | `bit` |
| BIT_N | 4 | `nibble` |
| BIT_N | 8 | `byte` |
| BIT_N | other | `vector` + size |
| SIGNED | any | `integer` + size |
| UNSIGNED | any | `vector` + size |

---

## Transition Properties
- Boolean **condition** expression (e.g. `a && b` `a || b` `a || !(b&&c)`)
- **Output assignments** on transitions (Mealy)
- Auto-routed curves with adjustable bend and anchor handles

## Getting Started

1. Click the **FSM Editor** toolbar button to open a blank canvas, or double-click any `.fsmxml` file in the Project Explorer
2. Click **Add New State** — the first state becomes the initial state automatically
3. Hover a state until the connector dot appears, then drag to another state to create a transition
4. Define port signals in the **Signals** section and internal variables in the **Variables** section
5. Set output assignments on states (Moore) or transitions (Mealy)
6. Click **VHDL** or **C** in the **Backend** section to generate code
7. Press **Ctrl+S** to save

### Keyboard Shortcuts

| Shortcut | Action |
|----------|--------|
| `Ctrl+S` | Save |
| `Ctrl+Z` | Undo |
| `Ctrl+Shift+Z` | Redo |
| `Delete` | Delete selected |
| `Escape` | Cancel / deselect |
| `Double-click` | Edit state or transition label |
| `Scroll wheel` | Zoom |
| `Right-click drag` | Pan |

---

## Installation

Search for **FSM Editor** in the OneWare Studio Package Manager and click **Install**.

---

## License

This project is licensed under the [MIT License](License.md).
