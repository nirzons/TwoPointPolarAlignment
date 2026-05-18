# 🔭 2-Point Polar Alignment for N.I.N.A.

[![License: MIT](https://img.shields.io/badge/License-MIT-yellow.svg)](https://opensource.org/licenses/MIT)
[![NINA Version](https://img.shields.io/badge/N.I.N.A.-3.1%2B-blue.svg)](https://nighttime-imaging.eu/)
[![Stability](https://img.shields.io/badge/Stability-v1.0%20Stable-green.svg)](#)

A premium, lightweight, and highly accurate polar alignment plugin for **N.I.N.A. (Nighttime Imaging 'N' Astronomy)**. 

Achieve precision polar alignment within minutes using a rock-solid 3D rotation matrix solver requiring only **two plate-solved images** and a short 90-degree RA rotation. 

---

## ✨ Key Features

- **Two-Point Simplicity**: No specialized view required. Aligns accurately using just your starting position and a single RA rotation.
- **Stabilized 3D Solver**: Employs advanced 3D rotation matrix mathematics hardened against Earth's rotation drift, achieving direct mathematical parity with industry benchmark benchmarks.
- **Global Compatibility**: Full native support for both Northern AND Southern Hemispheres, with directionally intuitive control prompts automatically adapted to your location.
- **Premium Glassmorphic UI**: Sleek, high-visibility dark-mode interface featuring real-time visual status cards, dynamic hover effects, and intuitive large-scale alignment typography.
- **Adaptive Priority Highlighting**: Intelligently monitors absolute variance and applies dynamic golden glow accents to whichever axis requires immediate corrective priority.
- **Real-Time Visual Dashboard**: Features a dedicated, high-frequency feedback widget instantly broadcasting mechanical states (Slewing, Solving, Success, Failure) for true operational awareness.
- **Live Confidence Tracking**: Deep-wired data validation loop that instantly flags onscreen number reliability using active red/green solver alerts—guaranteeing your physical tweaks are always synchronized.
- **Rough Finder Rescue Engine**: Hardened localized failsafe mode leveraging secondary Blind Solvers (Astrometry.net) to dynamically recover your position through extreme initial misalignments.
- **Adversarial Thread Hardening**: Core logic contains multi-layer architectural fortification ensuring comprehensive thread atomicity, secure cancellation hygiene, and atomic lifecycle disposal hooks.
- **Custom Mechanical Logic**: Adapt instruction outputs directly to your hardware. Features selectable 'Altitude Knob Turn Visualization' mapping linear movements seamlessly to rotational symbols (↻ / ↺).
- **Profile Native**: Seamlessly integrates into N.I.N.A.'s native profiles system—automatically backing up and reloading setup configurations, filter choices, and knob preferences per-rig.
- **Smart Restart & Safety Alternation**: Instantly detects consecutive runs from stopped positions to bypass homing, while automatically alternating rotation directions (East ➔ West ➔ East) to eliminate cable-wrap or pier-collision hazards.
- **Stale Measurement Dimming**: Instantly drops historic error card opacities to **45%** when starting a new run, clearly communicating stale status until new exposures solve and brighten the board.
- **Immediate Slew Interruption**: Hard-wired ASCOM slew abort brings telescope motors to an instant halt upon clicking **Stop** or **Home** mid-slew.

---

## 🚀 Operation Manual

Follow these simple steps to align your mount:

### 1. Preparation
1. Connect your **Camera** and **Telescope** in N.I.N.A.
2. Open the **2-Point Polar Alignment** tab (typically inside the Imaging dashboard).
3. Ensure your mount is unparked and at its Home position.

### 2. Configure Settings
- **Exposure & Gain**: Set values that allow your plate solver to reliably detect stars in < 5 seconds.
- **Rotation Direction**: Set to "East" by default (or "West" depending on meridian clearance).
- **Rotation Method**: Choose **Automatic** (recommended) for computerized mounts, or **Manual** for push-to trackers.
- **Rough Finder**: Enable this checkbox to activate a fully-automated backup Blind Solving rescue if your initial deployment error exceeds normal solver capabilities!
- **Alt Knob Visual**: Match the UI arrows to your specific mechanical gearing: standard, clockwise, or counter-clockwise icons.

### 3. The Alignment Cycle
1. Press **[Start Alignment Sequence]**.
2. The plugin captures **Measurement 1** and executes the RA rotation.
3. The plugin captures **Measurement 2** at the destination angle.
4. The real-time **Phase F (Live Adjustment)** loop begins automatically!
5. Review the **Alt / Az Cards**: Follow the clear text prompts (e.g., `Move Up`, `← Move Left`) to turn your physical knobs while watching the numbers drop in real-time.
6. Once satisfied, press **[Stop]** or **[HOME]** to finalize.

### 🔄 Smart Restart & Mount Safety Control

The plugin implements a highly sophisticated **Smart Restart & Mount Safety Control** layer designed specifically for high-end mechanical safety under real night skies:

*   **Bypassing Homing Checks**: If you press **[Stop]** to adjust physical cables, tweak dials, or check equipment, and then press **[Start Alignment Sequence]** again without moving the mount, the plugin automatically detects a **Smart Restart**. It completely bypasses all homing checks and starts the sequence *instantly* from your current position.
*   **Safety Rotation Alternation**: To prevent guide-scope cables or camera wires from wrapping, or the telescope mount from colliding with the tripod/pier, the plugin automatically **alternates the RA rotation direction** on consecutive smart restarts (e.g., East ➔ West ➔ East ➔ West). This oscillates the mount safely back and forth in the exact same physical space rather than letting it rotate endlessly in one direction!
*   **Dynamic Reversed Flow Indicator**: A green **"Reversed Flow Active"** badge automatically lights up in your UI *only* when the current active direction is running in reverse to your default configured direction—giving you full, clear, and reassuring visual feedback.
*   **Immediate Slew Abort**: Clicking **[Stop]** or **[HOME]** at any point in the middle of a telescope slew immediately triggers a hardware-level ASCOM slew abort, halting the mount's motors in their tracks instantly rather than waiting for the slew to complete.
*   **Visual Stale Data Dimming**: The moment you click **[Start]** on a fresh run or smart restart, your historic Azimuth, Altitude, and Total Error cards are instantly dimmed to **45% opacity**. This visually signals that they are stale, past values. They remain dimmed until your camera completes its first fresh exposure and the solver calculates new active measurements, at which point the dashboard instantly brightens back to full 100% opacity!

---

### ⚠️ Custom Polar Home Position Override (For Specific Mounts)

#### **When to use this feature?**
By default, the plugin requires the mount to start in its native **Home Position** (pointing directly toward the Celestial Pole, with Declination near 90°). 

The plugin uses an **intelligent declination heuristic** during the startup checks to dynamically handle different mount types and user configurations:

*   **Case 1: Standard Polar Homing Mounts (e.g. GSServer, OnStepX)**: 
    *   These mounts physically home near the celestial pole. 
    *   *If the mount is not homed*: The plugin will halt and prompt you to run a native **[HOME]** command.
    *   *If the mount is homed but slightly misaligned (`|Dec| >= 45°` but not within 1.0°)*: The plugin raises a **"Mount Homing Misaligned"** warning prompting you to check your physical index marks or slew closer to the pole. **No override is required!**
*   **Case 2: Custom / Non-Polar Homing Mounts (e.g. ASCOM Simulator, Alt-Az, custom configurations)**:
    *   These mounts define "Home" differently (e.g., ASCOM Simulator homes pointing at the Equator, `Dec = 0°`).
    *   *Heuristic*: The plugin detects that the native home points away from the pole (`|Dec| < 45°`).
    *   *Action*: The plugin raises a **"Polar Home Override Required"** warning directing you to enable the Custom Polar Home override.
    *   *Safety Guard*: To prevent unsafe movement, if you click the plugin's **[HOME]** button while this override is disabled, the plugin will **automatically intercept and block the native ASCOM home slew**, displaying a warning instructing you to enable the override instead.
*   **Case 3: Mechanical Constraints & Line-of-Sight Obstructions (User-Preferred Starting Position)**:
    *   Even if your mount natively homes perfectly pointing at the pole, physical variables might make a custom starting location preferable.
    *   *Examples*:
        *   **Cabling & Guide Scopes**: Standard 90-degree RA rotations could put severe strain on guide scope cables, camera connections, or heating bands.
        *   **Line-of-Sight Blocks**: Trees, roofs, or walls might obstruct the view of the sky in the exact direction the telescope would rotate during the default alignment cycle.
    *   *Action*: By enabling the override, you can manually position the telescope to a custom, safe starting RA/Dec orientation (still pointing near the pole) to clear obstructions and avoid cable strain, then lock it as your custom "Polar Home". 
    *   *Recommendation*: Be sure to select **"Start at Home"** in the plugin's *Starting Position* dropdown menu. This guarantees the alignment sequence starts exactly at your custom locked Polar Home orientation, rotating safely from there.

#### **How to use it?**
1. **Enable the Override**: In the configuration card, change the **Override Mount Home** setting from **False** to **True**.
2. **Position your Mount**: Slew your mount to its physical starting alignment position near the celestial pole (the Declination axis must be pointing near the pole, `|Dec| ≈ 90°`).
3. **Lock the Custom Position**: Click the rose-red **[Lock Polar Home]** button next to the override parameter. A N.I.N.A.-themed confirmation popup will confirm your custom RA/Dec starting coordinates are now locked.
4. **Start Homing & Alignment**:
   - The plugin's startup routine will now validate your starting position against your **custom locked coordinates** instead of the mount's native ASCOM home state.
   - Clicking the **[HOME]** button inside the plugin will now automatically slew the telescope directly to your custom locked coordinates.
   - These locked coordinates are saved persistently within your active N.I.N.A. profile!

---

## 🏗️ Developer Architecture

The codebase recently underwent a massive SOLID refactoring separating the core into 4 distinct layers:
1. **Stateless Domain Math** (`\Domain`, `\Math`): Pure 3D vector calculation engine (`TwoPointPolarSolver.cs`) testable fully offline.
2. **Workflow Orchestrator** (`\Workflow\AlignmentWorkflowController.cs`): The master asynchronous sequencer handling all plate-solving, mount slewing, and hardware timing loops.
3. **Reactive UI Bindings** (`\ViewModels`, `\Views`): Lightweight, "dumb" WPF Views and ViewModels (`PolarAlignmentDockableVM`, `ManualRotationWindow`) that strictly ingest `IProgress<T>` DTOs.
4. **Hardware Safety Interlocks** (`\Services`): Global semaphores preventing ASCOM driver lockups using a strict 30-second `HardwareTeardownTimeoutException` guard.

> [!NOTE]
> **Adversarial Hardening & Feature Restoration (v1.0.3.5 / v1.0.3.4)**: The entire architectural pipeline has undergone exhaustive adversarial hardening and feature restoration. Core settings services feature leakproof event disposals (`IDisposable`), WPF properties leverage cross-thread static pre-frozen brushes, background string updates are marshaled securely via Dispatcher queues, and hardware interlock mechanisms are strictly centralized in the workflow engine. Additionally, v1.0.3.4 introduced an adaptive 200ms real-time slewing guard querying mount motion telemetry directly to prevent motor collisions, and v1.0.3.5 successfully restored the fully-automated, state-decoupled **Rough Finder Rescue Mode** and integrated real-time blind solver status warning banners.

For a comprehensive file-by-file breakdown of line counts, structural diagrams, and responsibilities, please see the:
👉 **[Codebase Architecture Overview](codebase_structure_overview.md)**

---

## 🛠️ Manual Installation Guide (Current Beta)

Until we integrate into the official N.I.N.A. Plugin Store, you can install this beta manually:

1. Download the latest Release zip file from the [GitHub Releases section](https://github.com/nirzons/TwoPointPolarAlignment/releases).
2. Close N.I.N.A if it is currently running.
3. Navigate to your Local Plugins directory:
   `%LocalAppData%\NINA\Plugins\3.0.0\` (or your target N.I.N.A major version folder)
4. Create a new folder named `TwoPointPolarAlignment`.
5. Extract the contents of the downloaded zip into that folder.
6. Launch N.I.N.A., and activate the plugin via the **Plugins** management screen.

---

## 🗺️ Future Goals & Ecosystem Integration

This plugin is tracking toward full incorporation into the **N.I.N.A. Ecosystem**.
Current development milestones include:

- [x] **Beta 1.0**: Core logic, mathematics, multi-hemisphere support, UI Polish, Native Profiles.
- [ ] **Beta 1.x**: Wide-scale real-world user testing and performance logging.
- [ ] **Official Release**: Submission to N.I.N.A.'s centralized plugin repository (`.npack`) for one-click installation globally!

---

## 📄 License

Distributed under the **MIT License**. See `LICENSE` for more information.

*By Nir Zonshine.*
