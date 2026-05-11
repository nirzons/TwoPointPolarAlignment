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
- **Rough Finder**: Enable this checkbox to active fully-automated backup Blind Solving rescue if your initial deployment error exceeds normal solver capabilities!
- **Alt Knob Visual**: Match the UI arrows to your specific mechanical gearing: standard, clockwise, or counter-clockwise icons.

### 3. The Alignment Cycle
1. Press **[Start Alignment Sequence]**.
2. The plugin captures **Measurement 1** and executes the RA rotation.
3. The plugin captures **Measurement 2** at the destination angle.
4. The real-time **Phase F (Live Adjustment)** loop begins automatically!
5. Review the **Alt / Az Cards**: Follow the clear text prompts (e.g., `Move Up`, `← Move Left`) to turn your physical knobs while watching the numbers drop in real-time.
6. Once satisfied, press **[Stop]** or **[HOME]** to finalize.

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
