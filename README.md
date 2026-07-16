# 🔭 2-Point Polar Alignment for N.I.N.A.

[![License: MIT](https://img.shields.io/badge/License-MIT-yellow.svg)](https://opensource.org/licenses/MIT)
[![NINA Version](https://img.shields.io/badge/N.I.N.A.-3.1%2B-blue.svg)](https://nighttime-imaging.eu/)

A premium, lightweight, and highly accurate polar alignment plugin for **N.I.N.A. (Nighttime Imaging 'N' Astronomy)**. 

Achieve precision polar alignment within minutes using a rock-solid 3D rotation matrix solver requiring only **two plate-solved images** and a short 90-degree RA rotation.

---

## 💾 Installation

### 1. Recommended: N.I.N.A. Plugin Store (Automatic)
The easiest and recommended way to install the plugin is directly through the official N.I.N.A. Plugin Store:
1. Open N.I.N.A. and navigate to the **Plugins** tab on the left.
2. Select **Available** in the top tab menu.
3. Locate **2-Point Polar Alignment** (use search if needed) and click **Install**.
4. Restart N.I.N.A. to activate.

### 2. Manual Installation (For Development & Offline PCs)
If you need to install the plugin manually (e.g., from a custom compiled release or on an offline computer):
1. Download the plugin ZIP archive containing the compiled binaries.
2. Navigate to your local AppData directory: `%LOCALAPPDATA%\NINA\Plugins\3.0.0\` (typically `C:\Users\<YourUsername>\AppData\Local\NINA\Plugins\3.0.0\`).
3. Create a new subfolder named precisely **`2-Point Polar Alignment`**.
4. Extract the compiled binary files into that folder:
   - `NirZonshine.NINA.TwoPointPolarAlignment.dll`
   - `NirZonshine.NINA.TwoPointPolarAlignment.pdb`
   - `NirZonshine.NINA.TwoPointPolarAlignment.deps.json`
   - `NirZonshine.NINA.TwoPointPolarAlignment.runtimeconfig.json`
   - `NirZonshine.NINA.TwoPointPolarAlignment.dll.config`
5. Restart N.I.N.A. to activate.

---

> [!NOTE]
> **Filter Selection & Profile Synchronization**:
> The plugin's filter selection list is populated from your active N.I.N.A. profile configuration during startup. If you edit, rename, or add filters in N.I.N.A. (under Options -> Equipment -> Filter Wheel), you **must restart N.I.N.A.** for the updated filter list to be reflected in the plugin dropdown.

Please refer to the comprehensive **[User & Operation Manual](USER_MANUAL.md)** for detailed settings explanations, sequencer integration details, smart restart features, and custom polar home override guides.

---

## ✨ Key Features

- **Two-Point Simplicity**: No specialized view required. Aligns accurately starting from your mount's natural home position with a single 90-degree RA rotation.
- **Advanced Sequencer Integration**: Run polar alignment fully automatically as part of your N.I.N.A. Advanced Sequencer workflows. Exposes all configuration options as overrides, displays warning badges when equipment is disconnected, and automatically pauses at Phase F adjustments (showing a purple `[Resume Sequence]` button) until manual tweaks are confirmed.
- **Multi-Frame Sampling & Outlier Rejection**: Capture Single, Double, or Triple sub-frames at each measurement station to average out seeing jitter and wind gusts. Uses 3D unit-vector spherical geometry to automatically reject coordinate outliers on triple exposures.
- **Sidereal LST Time-Drift Normalization**: Tracks and mathematically corrects for Earth's sidereal rotation during mount slews and consecutive sub-frames, "freezing" the sky to achieve sub-arcminute accuracy matching the plate solver's limit.
- **Support for Single-Knob Bases**: Adds selectable Clockwise (↻) and Counter-Clockwise (↺) mechanical rotation arrows for the Azimuth axis—perfect for single-knob bases like the ZWO AM5 or William Optics base.
- **Stabilized 3D Solver**: Employs advanced 3D rotation matrix mathematics hardened against Earth's rotation drift, achieving direct mathematical parity with industry benchmarks.
- **Global Compatibility**: Full native support for both Northern AND Southern Hemispheres, with directionally intuitive control prompts automatically adapted to your location.
- **Premium Glassmorphic UI**: Sleek, high-visibility dark-mode interface featuring real-time visual status cards, dynamic hover effects, and intuitive large-scale alignment typography.
- **Adaptive Priority Highlighting**: Intelligently monitors absolute variance and applies dynamic golden glow accents to whichever axis requires immediate corrective priority.
- **Real-Time Visual Dashboard**: Features a dedicated, high-frequency feedback widget instantly broadcasting mechanical states (Slewing, Solving, Success, Failure) for true operational awareness.
- **Live Confidence Tracking**: Deep-wired data validation loop that instantly flags onscreen number reliability using active red/green solver alerts—guaranteeing your physical tweaks are always synchronized.
- **Rough Finder Rescue Engine**: Hardened localized failsafe mode leveraging secondary Blind Solvers (Astrometry.net) to dynamically recover your position through extreme initial misalignments.
- **Adversarial Thread Hardening**: Core logic contains multi-layer architectural fortification ensuring comprehensive thread atomicity, secure cancellation hygiene, and atomic lifecycle disposal hooks.
- **Profile Native**: Seamlessly integrates into N.I.N.A.'s native profiles system—automatically backing up and reloading setup configurations, filter choices, and knob preferences per-rig.
- **Smart Restart & Safety Alternation**: Instantly detects consecutive runs from stopped positions to bypass homing, while automatically alternating rotation directions (East ➔ West ➔ East) to eliminate cable-wrap or pier-collision hazards.
- **Stale Measurement Dimming**: Instantly drops historic error card opacities to **45%** when starting a new run, clearly communicating stale status until new exposures solve and brighten the board.
- **Immediate Slew Interruption**: Hard-wired ASCOM slew abort brings telescope motors to an instant halt upon clicking **Stop** or **Home** mid-slew.

---

## 🏗️ Developer Architecture

The codebase separates the core into 4 distinct layers:
1. **Stateless Domain Math** (`\Domain`, `\Math`): Pure 3D vector calculation engine (`TwoPointPolarSolver.cs`) testable fully offline.
2. **Workflow Orchestrator** (`\Workflow\AlignmentWorkflowController.cs`): The master asynchronous sequencer handling all plate-solving, mount slewing, and hardware timing loops.
3. **Reactive UI Bindings** (`\ViewModels`, `\Views`): Lightweight, WPF Views and ViewModels (`PolarAlignmentDockableVM`, `ManualRotationWindow`) that strictly ingest `IProgress<T>` DTOs.
4. **Hardware Safety Interlocks** (`\Services`): Global semaphores preventing ASCOM driver lockups using a strict 30-second `HardwareTeardownTimeoutException` guard.

> [!NOTE]
> **Adversarial Hardening & Feature Restoration (v1.0.3.5 / v1.0.3.4)**: Core settings services feature leakproof event disposals (`IDisposable`), WPF properties leverage cross-thread static pre-frozen brushes, background string updates are marshaled securely via Dispatcher queues, and hardware interlock mechanisms are strictly centralized in the workflow engine. Additionally, v1.0.3.4 introduced an adaptive 200ms real-time slewing guard querying mount motion telemetry directly to prevent motor collisions, and v1.0.3.5 successfully restored the fully-automated, state-decoupled **Rough Finder Rescue Mode** and integrated real-time blind solver status warning banners.

For a comprehensive file-by-file breakdown of line counts, structural diagrams, and responsibilities, please see the:
👉 **[Codebase Architecture Overview](codebase_structure_overview.md)**
👉 **[Mathematical Model Documentation](MATHEMATICAL_MODEL.md)**

---

## 📜 Changelog

For a detailed history of all official releases, features, and fixes, please see the **[CHANGELOG.md](CHANGELOG.md)**.

---

## 📄 License

Distributed under the **MIT License**. See `LICENSE` for more information.

*By Nir Zonshine.*
