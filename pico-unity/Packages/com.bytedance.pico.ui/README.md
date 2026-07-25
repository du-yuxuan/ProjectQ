# PICO UI

A 3D UI component library for PICO XR applications, built on Unity GUI.

## Overview

PICO UI (`com.bytedance.pico.ui`) provides ready-to-use 3D UI components designed for VR/XR experiences on PICO devices. It includes prefabs, runtime scripts, and editor tooling for rapid UI construction in Unity.

## Requirements

- Unity 2022.3 LTS or later
- Universal Render Pipeline (URP)
- XR Interaction Toolkit 2.0+

## Dependencies

| Package | Minimum Version |
|---------|----------------|
| com.unity.ugui | 1.0.0 |
| com.unity.textmeshpro | 3.0.0 |
| com.unity.xr.interaction.toolkit | 2.0.0 |

## Installation

### Via Unity Package Manager (Git URL)

1. Open Unity Editor
2. Navigate to **Window > Package Manager**
3. Click **+** > **Add package from git URL...**
4. Enter the repository URL

### Via Local Path

1. Clone this repository
2. In Unity Package Manager, click **+** > **Add package from disk...**
3. Select the `package.json` in the root of this repository

## Components

| Component | Description |
|-----------|-------------|
| **Slider** | Multiple variants (Small/Regular/Max) with stepless and segmented modes, icon support |
| **Switch** | Toggle switch control |
| **TabBar** | Tab navigation with badge notification support |
| **SideNavigation** | Expandable/collapsible side navigation items |
| **ToolBar** | Toolbar with icon and text variants |
| **Menu** | Dropdown/popup menu container with menu items |
| **List** | List item component |
| **PanelLayout** | Curved multi-panel layout for immersive XR environments |

## Usage

All components are available via the Unity Editor menu:

**GameObject > UI > PICO-UI**

This menu provides one-click creation for all supported components. Each item instantiates the corresponding prefab under a Canvas with proper hierarchy setup.

### Example: Adding a Slider

1. In the Unity menu, go to **GameObject > UI > PICO-UI > Slider Regular Stepless**
2. The slider prefab is instantiated under a Canvas in your scene
3. Configure the slider value range and behavior in the Inspector

### Example: Curved Panel Layout

```csharp
// PXR_PanelController arranges child panels in a curved arc
// Configure rotation angle, spacing, and vertical alignment in the Inspector
var panelController = GetComponent<PXR_PanelController>();
panelController.rotationAngleBetweenPanels = 15f;
```

## Project Structure

```
PICO-UISet/
├── Assets/
│   ├── Prefabs/          # Ready-to-use UI prefabs (20 total)
│   └── Textures/         # Shared sprite atlas
├── Editor/
│   └── Scripts/          # Editor menu integration (PICOUICreator)
├── Runtime/
│   └── Scripts/
│       ├── Core/         # Base input handler (PXR_UIInputHandler)
│       └── Components/   # Component controllers
└── package.json
```

## Architecture

- **PXR_UIInputHandler** - Base class for interactive UI components. Handles pointer and drag events, converts XR ray hits to normalized local coordinates on RectTransforms.
- **Component Controllers** - Per-component scripts that manage visual states, layout, and interaction logic.
- **Prefabs** - Pre-configured GameObjects with proper component setup, ready for scene instantiation.
