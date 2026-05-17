# 📂 Codebase Structure Overview
## 2-Point Polar Alignment for N.I.N.A.

This document serves as an architectural map for developers contributing to the plugin. Following the v1.0.2.0 SOLID refactoring, the codebase has been cleanly decoupled into four core layers.

---

## 🏗️ The 4-Layer SOLID Architecture

### 1. 🧮 Domain & Mathematics Layer (Stateless)
Pure, unit-testable components completely disconnected from the N.I.N.A. UI or Hardware.

| File Name | Relative Path | Role / Responsibility | Line Count |
| :--- | :--- | :--- | :--- |
| **`TwoPointPolarSolver.cs`** | `\Math` | **The Brain**: Stateless 3D rotation matrix engine. Receives two coordinates, returns precise polar alignment error vectors. | **~106** |
| **`Vector3D.cs`** | `\Domain` | **Primitives**: High-performance double-precision 3D vectors with inline operator overloading. | **~70** |
| **`AlignmentWorkflowContext.cs`** | `\Domain` | **DTO**: Encapsulates all state variables (Coordinates, Timestamps) shared across workflow phases. | **~23** |

### 2. ⚙️ Workflow Orchestration Layer
The operational heart of the plugin that sequences asynchronous operations.

| File Name | Relative Path | Role / Responsibility | Line Count |
| :--- | :--- | :--- | :--- |
| **`AlignmentWorkflowController.cs`** | `\Workflow` | **The Sequencer**: Governs the linear alignment flow (Capture -> Solve -> Rotate -> Capture). Fully asynchronous, utilizing strict `CancellationToken` passing to safely interact with hardware and emit `IProgress` reports. | **~492** |

### 3. 🛡️ Services & Safety Layer
Global systems designed to protect the host application (N.I.N.A.) and persist state.

| File Name | Relative Path | Role / Responsibility | Line Count |
| :--- | :--- | :--- | :--- |
| **`SettingsManager.cs`** | `\Services` | **State Persistence**: A reactive singleton mediating between UI checkboxes and N.I.N.A.'s profile registry. Contains backward-compatibility migration logic. | **~249** |
| **`HardwareTeardownTimeoutException.cs`**| `\Domain` | **Safety Guard**: Custom exception thrown by global semaphores if an ASCOM driver refuses to yield within 30 seconds during an emergency abort. | **~9** |

### 4. 🎨 ViewModels & Views Layer (Reactive UI)
Lightweight bindings that only react to DTO streams; devoid of celestial math or background loops.

| File Name | Relative Path | Role / Responsibility | Line Count |
| :--- | :--- | :--- | :--- |
| **`PolarAlignmentDockableVM.cs`** | `\` | **Main UI Bridge**: Subscribes to `IProgress<AlignmentProgressReport>` and projects values to the primary WPF view. | **~853** |
| **`Options.xaml`** | `\` | **Primary Layout**: The main N.I.N.A. dashboard view defining the visual layout, error cards, and configuration inputs. | **~490** |
| **`ManualRotationWindow.xaml`** | `\Views` | **Push-To UI**: A sleek standalone dialog window instructing users how far to rotate non-computerized mounts. | **~89** |
| **`ManualRotationVM.cs`** | `\ViewModels` | **Push-To Logic**: "Dumb" view model projecting data from `ManualTrackingProgress` updates. | **~105** |

---

## 🚀 Lifecycle Summary

When a user clicks "Start":
1. **`PolarAlignmentDockableVM`** instantiates an **`AlignmentWorkflowContext`** and passes it to the **`AlignmentWorkflowController`**.
2. The Controller securely acquires hardware locks (Camera/Telescope) using `SemaphoreSlim` guards.
3. The Controller executes Phases A-F, taking exposures and fetching coordinates.
4. Calculations are deferred to the stateless **`TwoPointPolarSolver`**.
5. During the active phase, the Controller broadcasts **`AlignmentProgressReport`** DTOs back via `IProgress`.
6. The `PolarAlignmentDockableVM` receives these reports on the main UI thread and updates the XAML databindings instantly.
