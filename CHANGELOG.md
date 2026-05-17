# 2-Point Polar Alignment — Changelog

## v1.0.3.3 — Adversarial Hardening (2026-05-17)

### 🛡️ Adversarial Hardening & Stability
- **M-1: SettingsManager Profile-Leak Prevention**: Implemented `IDisposable` in `SettingsManager` and detached the `ProfileChanged` event listener. The View's `Dispose()` now cleanly unbinds the settings service, preventing memory leaks during profile changes.
- **M-2: Manual Rotation Dialog Leak Fix**: Wrapped the dialog's `CancellationTokenSource` in a `try/finally` block inside the callback wrapper to guarantee proper disposal of unmanaged timer/event resources.
- **W-1: WPF Static Frozen Brushes**: Replaced per-access dynamic `SolidColorBrush` creations inside `ManualRotationVM` with pre-frozen static readonly brushes. This guarantees thread safety for cross-threaded WPF calls and optimizes CPU cycles.
- **W-2: Thread-Safe Log Mutations**: Log modifications now utilize `Dispatcher.BeginInvoke` to marshal string updates safely onto the WPF UI thread, preventing cross-threaded property notification crashes.
- **T-3: Consolidated Hardware Interlocks**: Removed duplicate `SemaphoreSlim` interlock and utility methods from the main ViewModel. The ViewModel now calls mediators directly, deferring interlocks exclusively to the master controller (`AlignmentWorkflowController`).
- **F-4: Diagnostic GetLatitude Fallbacks**: Configured clear `Logger.Warning` statements within `GetLatitude()` to capture and log reflection failures rather than silently swallowing errors and falling back to 45.0° default latitude.

### 📋 Meta
- Assembly version updated to `1.0.3.3`.

## v1.0.3.1 — Safety & UI Enhancements (2026-05-17)

### 🛡️ Mount Physical Safety
- **State-Aware Alternating Smart Restarts**: Smart restarts now dynamically track the direction used in the previous aborted run. Consecutive restarts automatically alternate the rotation direction (East ➔ West ➔ East), causing the mount to oscillate safely back and forth in the exact same physical space to prevent cord-wrap, cable strain, or tripod/pier collisions.
- **Immediate Slew Interruption**: Pressing **Stop** or **Home** in the middle of a slew now immediately dispatches a hardware-level ASCOM slew abort command (`StopSlew`), instantly bringing the mount's motors to a halt instead of waiting for the slew to complete.

### 🎨 User Interface & Diagnostics
- **Dynamic Reversed Flow Indicator**: The green "Reversed Flow Active" UI badge now dynamically lights up *only* if the smart restart's active direction is reversed relative to your default configured setting. If the safety oscillation alternates the direction back to your default, the badge automatically hides to prevent user confusion.
- **Stale Results Visual Dimming**: Starting a new run (either fresh or smart restart) now immediately dims the Azimuth, Altitude, and Total Error summary cards to **45% opacity** using a WPF style trigger. This clearly indicates that they are stale, past values. The cards instantly brighten back to full 100% opacity once the first fresh calculation completes.

### 📋 Meta
- Assembly version incremented to `1.0.3.1`.
- Fully documented all safety, smart restart, and dimming behaviors in `README.md`.

## v1.0.2.0 — Architecture Refactor (2026-05-17)

### 🏗️ Architecture & Refactoring
- **SOLID Re-Architecture**: Shrunk the massive 2,500+ line UI monolith (`PolarAlignmentDockableVM`) down to a lightweight 850-line data binder by decoupling math, hardware, and workflow logic.
- **Stateless Math Engine**: Extracted celestial 3D math and solver routines into a dedicated, unit-testable `TwoPointPolarSolver`.
- **Centralized Workflow Orchestrator**: All alignment phases, from slewing to plate-solving, are now governed by a dedicated asynchronous `AlignmentWorkflowController` rather than scattered UI threads.
- **Decoupled Manual UI**: Migrated the legacy imperative Manual Rotation dialog into a proper declarative MVVM `ManualRotationWindow.xaml` and `ManualRotationVM`.
- **Global Hardware Interlock**: Introduced a robust 30-second `HardwareTeardownTimeoutException` semaphore to prevent ASCOM drivers from zombie-locking the main N.I.N.A. application during emergency aborts.
- **Strict Cancellation Hygiene**: Enhanced `CancellationToken` passing guarantees instantaneous hardware halting upon user "Stop" or window closure.

### 🐛 Bug Fixes
- Fixed a text encoding bug in the instruction strings that caused Unicode symbols (like arrows and emojis) to display as garbled characters (e.g. `â†’`).

## v1.0.1.0 — Beta 1.0 (2026-05-10)

### ✨ Features
- **Stabilized 3D Rotation Matrix Solver**: Rock-solid polar axis calculation hardened against Earth rotation drift and multi-hemisphere coordinate flips.
- **Full Manual Mode Support**: Camera-only operation for non-computerized, un-plugged, or manual mounts — no mount connection required.
- **Premium Glassmorphic UI**: Sleek dark-mode interface with dynamic hover glow effects, real-time error cards, and large-scale alignment typography.
- **Event-Driven Equipment Awareness**: Asynchronous `ICameraConsumer` / `ITelescopeConsumer` tracking with zero-latency detection of hardware connectivity changes.
- **Native Profile Integration**: Settings automatically persist per-rig via N.I.N.A.'s native profile system.
- **Smart Restart Detection**: Automatic reversed-flow handling when re-running alignment from the previous endpoint.
- **Hemisphere-Adaptive Instructions**: Directional adjustment prompts auto-invert based on site latitude (Northern vs. Southern).
- **Blind Solver Failover**: Recursive blind-solver backup enabled for mountless autonomous lock-on in Manual mode.

### 🛡️ Robustness
- Safety intercept system prevents alignment from proceeding when plate-solved coordinates diverge too far from mount telemetry.
- Pre-flight validation of camera, mount, filter wheel, and plate solver configuration before any mount movement.
- Comprehensive retry logic with configurable attempt count for plate solve failures.

### 📋 Meta
- Assembly version incremented to `1.0.1.0`.
- License unified to MIT across all metadata.
- Full manual installation guide in README.