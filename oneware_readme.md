## Overview

FSM Editor is a OneWare Studio extension that lets you design, visualize, and export Finite State Machines directly inside your IDE. Draw states and transitions on a graphical canvas, define input/output signals, and generate synthesizable **VHDL** or portable **C code** from your design with a single click.

---

## Features

### Visual Editor
- **Drag-and-drop canvas** — create and reposition states freely
- **Interactive transitions** — drag from a state's hover connector to another state to create a transition; supports self-transitions and parallel transitions
- **Inline editing** — double-click any state or transition label to rename it or set conditions/output assignments directly on the canvas
- **Marquee selection** — click and drag on empty canvas to box-select multiple states at once
- **Zoom & pan** — scroll wheel to zoom, or enable Cursor Mode to pan the canvas

### State Machine Types
| Type | Description |
|------|-------------|
| **Moore** | Outputs are associated with states |
| **Mealy** | Outputs are associated with transitions |

Switch between types at any time from the Graph Type selector in the sidebar.

### Signal Definitions
Define your FSM's interface with typed signals:
- Direction: `in` / `out`
- Types: `bit` (single bit) or `bit_n` (configurable width)
- Signals are used to auto-complete and validate output assignment expressions

### State Properties
- Mark any state as the **initial state** (entry point)
- Mark states as **final states**
- Set **output assignments** (Moore) directly on the state node

### Transition Properties
- Boolean **condition** expression (e.g. `a AND NOT b`)
- **Output assignments** on transitions (Mealy)
- Auto-routed curves with adjustable bend and anchor handles

### Code Generation & Verification
Use the **Backend** panel in the sidebar to:
- **Generate VHDL** — produces a synthesizable VHDL entity from the FSM
- **Generate C** — produces a portable C implementation
- **Verify** — runs backend verification checks on the current FSM

### File Format
FSMs are saved as [SCXML](https://www.w3.org/TR/scxml/)-compatible XML files (`.xml` / `.scxml`). Right-click any `.xml` or `.scxml` file in the Project Explorer and choose **View FSM-Graph** to open it in the editor.

### Undo / Redo
Full undo/redo history for all editing operations.

---

## Keyboard Shortcuts

| Shortcut | Action |
|----------|--------|
| `Ctrl+S` | Save current FSM |
| `Ctrl+Z` | Undo |
| `Ctrl+Shift+Z` | Redo |
| `Delete` | Delete selected state(s) or transition |
| `Escape` | Cancel pending transition / deselect all |
| `Double-click` state | Edit state name / output assignments |
| `Double-click` transition label | Edit transition condition |
| `Scroll wheel` | Zoom in / out |

---

## Installation

### Via OneWare Package Manager
1. Open **OneWare Studio**
2. Navigate to **Extensions → Package Manager**
3. Search for **FSM Editor** and click **Install**

### Manual Installation
Download the latest release from [GitHub Releases](https://github.com/FEntwumS/FEntwumS.FSM/releases) and install it through the OneWare package manager's manual install option.

---

## Getting Started

### Create a New FSM
Click the **FSM Editor** toolbar button in the OneWare Studio toolbar to open a blank canvas.

### Open an Existing FSM
Right-click any `.xml` or `.scxml` file in the Project Explorer and select **View FSM-Graph**.

### Build Your First State Machine
1. Click **Add New State** in the sidebar to place your first state (it becomes the initial state automatically)
2. Add more states as needed
3. Hover over a state until the connector dot appears, then drag to another state to create a transition
4. Double-click transition labels to set conditions
5. Define signals in the **Signals** section of the sidebar
6. Set output assignments on states (Moore) or transitions (Mealy)
7. Press **Ctrl+S** or click **Save** to save as an XML file

### Generate Code
1. Click **VHDL** or **C** in the **Backend** section of the sidebar
2. Choose an output directory when prompted
3. The generated files will be placed in the selected folder

---

## Development Setup

> Follow these steps to build and run the extension locally.

**Prerequisites**
- [Visual Studio Code](https://code.visualstudio.com/)
- [OneWare Studio](https://one-ware.com/docs/getting-started/setup)
- [JDK](https://adoptium.net/) (any current version, with `JAVA_HOME` set for all users)
- .NET 10 SDK

**Steps**
1. Install the .NET 10 SDK (search `>.net install` in VS Code's command palette)
2. Restart VS Code after the SDK installation
3. Clone the repository:
   ```sh
   git clone https://github.com/FEntwumS/FEntwumS.FSM.git
   ```
4. Open the cloned folder in VS Code
5. Run the **Build Solution** task (`Ctrl+Shift+B`)
6. Open the **Run and Debug** panel, select **Run Plugin**, and press the play button
7. OneWare Studio will launch with the FSM Editor extension installed

> **Note:** If OneWare Studio fails to launch, check that the `program` path in `.vscode/launch.json` points to your local `OneWareStudio.exe`.

---

## Project Structure

```
src/OneWare.MyExtension/
├── OneWareMyExtensionModule.cs       # Extension entry point & service registration
├── Services/
│   └── FiniteStateMachineService.cs  # Opens/creates FSM editor documents
├── ViewModels/
│   ├── FiniteStateMachineViewModel.cs # Main editor logic, undo/redo, XML persistence
│   ├── StateItemViewModel.cs          # Individual state node
│   ├── TransitionViewModel.cs         # Transition arc & geometry
│   ├── SignalDefinitionViewModel.cs   # Signal interface definition
│   ├── FsmXmlStateHelper.cs           # SCXML read/write helpers
│   ├── FsmToolbarExtensionViewModel.cs# Toolbar button
│   └── FsmGraphType.cs                # Moore / Mealy enum
└── Views/
    ├── FiniteStateMachineView.axaml    # Canvas UI
    └── FsmToolbarExtensionView.axaml   # Toolbar button UI
```

---

## License

This project is licensed under the [MIT License](License.md).
