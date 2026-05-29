# 🔭 2-Point Polar Alignment for N.I.N.A.

[![License: MIT](https://img.shields.io/badge/License-MIT-yellow.svg)](https://opensource.org/licenses/MIT)
[![NINA Version](https://img.shields.io/badge/N.I.N.A.-3.1%2B-blue.svg)](https://nighttime-imaging.eu/)
[![Stability](https://img.shields.io/badge/Stability-v1.0%20Stable-green.svg)](#)

A premium, lightweight, and highly accurate polar alignment plugin for **N.I.N.A. (Nighttime Imaging 'N' Astronomy)**. 

Achieve precision polar alignment within minutes using a rock-solid 3D rotation matrix solver requiring only **two plate-solved images** and a short 90-degree RA rotation. 

---

> [!IMPORTANT]
> 📖 **First-Time Setup & Operation**: If you are a beta tester or a new user, please refer directly to the comprehensive **[User & Operation Manual](USER_MANUAL.md)** for detailed installation instructions, settings explanations, smart restart features, and custom polar home override guides!

---

## ✨ Key Features

- **Two-Point Simplicity**: No specialized view required. Aligns accurately using just your starting position and a single RA rotation.
- **Stabilized 3D Solver**: Employs advanced 3D rotation matrix mathematics hardened against Earth's rotation drift, achieving direct mathematical parity with industry benchmarks.
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
👉 **[Mathematical Model Documentation](MATHEMATICAL_MODEL.md)**

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
