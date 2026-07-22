# 2-Point Polar Alignment — Changelog

All notable changes to this project are documented below. Only officially released versions published to GitHub/N.I.N.A. Store are listed.

## v1.5.2.0 — Filter Wheel Resolver & Position Hardening (2026-07-22)

### 🐛 Bug Fixes & Stability
- **Fixed Filter Wheel Position Resolver**: Resolved a bug where constructing `CaptureSequence` objects without explicitly populating `FilterType.Position` caused `Position` to default to 0. This previously led N.I.N.A.'s internal `CaptureSolver` to issue unwanted filter change requests (e.g. switching back and forth between Position 1 and Position 0) on every exposure for both manual and electronic filter wheels.
- **Synchronized CaptureSequence Filter State**: Enforced exact filter position mapping (`GetTargetFilterInfo`) across all workflow phases (initial positioning, rotation, measurement, and rescue solver loops).

---

## v1.5.1.0 — Sequencer Measurement Only & Error Display (2026-07-19)

### 🤖 Sequencer Enhancements
- **Added Force Home Position Option**: Exposes a "Force Home Position" checkbox (enabled by default). When set, the plugin commands the mount to find Home (or slews to your Custom Polar Home) before starting alignment measurements.
- **Added Measurement Only Mode**: Exposes a "Measurement Only" checkbox in the sequencer parameters. When enabled, the plugin completes the initial two measurements, logs the results, and immediately auto-completes and advances the sequence without entering the live adjustment loop.
- **Added Real-time Error Display**: Dynamically displays the measured alignment error (e.g. `00° 01' 24"`) in bold under the "Total Error" label on the right side of the sequencer instruction block, as well as updating the instruction title (e.g. `2-Point Polar Alignment (01' 10")`). This display is active in both the Sequencer screen and the Imaging panel.

---

## v1.5.0.0 — Automated Sequencer Auto-Complete & Verification Passes (2026-07-18)

### 🤖 Advanced Automation & Sequencer Control
- **Added Auto-Complete on Target**: Exposes a target error tolerance (in arcminutes) inside the Advanced Sequencer block. When the polar error stays below this target, the plugin automatically completes and advances the sequence. Set to 0 to keep the manual resume flow.
- **Added Consecutive Stable Frames Validation**: Requires the error to stay below the tolerance for a set number of consecutive frames (default 3) before auto-completing, ensuring the mount is physically settled. The counter is immediately reset on any plate solve failure or error spike above the target.
- **Added Reverse-Direction Verification Passes**: Exposes a validation runs parameter. When set, the plugin automatically restarts the alignment cycle in the reverse direction (using the built-in Smart Restart rotation logic) to verify that the physical adjustments are consistent, backlash has settled, and the locked mount remains aligned in both RA directions.
- **Improved Status Reporting**: Continuously broadcasts the current pass, measured error, and stable frame count (e.g. `Pass 1/2: Adjusting (Error: 1.25′ | Stable: 2/3)`) to both the N.I.N.A. sequencer progress and the main plugin dashboard GUI status indicator.
- **Linked Cancellation token & Hard Timeout Aborts**: Linked N.I.N.A.'s sequencer abort trigger for instantaneous response, and added a 15-second shutdown timeout safeguard that throws a `TimeoutException` to abort sequence execution cleanly in case of unresponsive camera/mount drivers.

### 🎨 Cosmetic & Store Presentation Improvements
- **Custom Featured Logo**: Added a premium custom logo (`logo.png`) that displays next to the plugin name and in the details pane of N.I.N.A.'s plugin manager.
- **Improved Store Description**: Replaced the long description in N.I.N.A.'s plugin metadata with a clean, structured overview of key features.

---

## v1.4.2.0 — Filter Wheel Position Resolution (2026-07-16)

### ⚙️ Equipment Control & Stability
- **Fixed Filter Wheel Position Resolution**: Corrected a bug where the plugin always commanded the filter wheel to Position 0 (slot 1) regardless of the selected filter. The plugin now dynamically queries the active profile's `FilterWheelFilters` using safe type conversions (`Convert.ToInt32()`) to determine the correct slot position index.
- **Bypassed Filter Selection for Current Filter**: When `(Current)` is selected in the settings, the filter change command is skipped entirely, keeping the filter wheel on the user's active filter.

---

## v1.4.1.0 — Live Solve Double-Precession Fix (2026-06-04)

### ✨ Core Math Accuracy
- **Fixed Live Loop Double-Precession**: Corrected a bug where the JNOW-precessed polar axis returned by `EvaluateLiveError` was precessed a second time inside `ReportAlignmentProgress`, shifting the user's live alignment target by ~8.5 arcminutes. Smart restarts now show stable, consistent Alt/Az coordinates matching the end of the previous run with zero coordinate jump.

---

## v1.4.0.0 — J2000/JNOW Epoch Pipeline Fix — Systematic 9-Arcminute Error Resolved (2026-06-04)

### 🔬 Critical Math Accuracy Fix (Issue #3)
- **Root-Cause Fix for Systematic ~9-Arcminute Alignment Error**: Identified and corrected a coordinate/Position Angle epoch mismatch introduced in v1.3.0.0. The previous approach precessed solver *Coordinates* from J2000 to JNOW at the **input stage** while leaving the camera's *Position Angle (PA)* in its native J2000 frame. Because lines of RA converge near the celestial pole, the J2000→JNOW pole shift (~8.5–8.8 arcminutes) rotates the direction of Celestial North at the measured coordinates by up to 8–16 degrees, causing the PA-derived camera orientation vectors to point in the wrong direction and injecting a systematic phantom error of the same ~9-arcminute magnitude.
- **New Approach — Precess Only the Final Axis Vector**: The entire 3D kinematics pipeline (LST normalization, rotation matrix derivation, Rodrigues' formula) now runs in the solver's native **J2000.0** frame, keeping coordinates and Position Angles perfectly consistent. Only at the very end — inside `CalculateErrorFromAxis` — is the computed mechanical polar axis precessed from J2000 to JNOW before projecting into Alt/Az offsets. This ensures the final pole comparison is made against the real-time physical JNOW pole with zero mismatch.

---

## v1.3.1.0 — High-Precision Polar Guard Expansion & Rig Stability (2026-06-02)

### ✨ Core Math Accuracy
- **High-Precision Polar Guard Expansion**: Solved an alignment failure crash on highly precise rigs during verification passes by increasing the celestial pole safety guard threshold to `0.9999999999` ($89.99999^\circ$ / 36 milliarcseconds). This expands the allowable alignment zone to the absolute limits of physical and atmospheric resolution, preventing mathematical crashes on perfect alignments while retaining 100% numerical and floating-point stability.

---

## v1.3.0.0 — Epoch Precession Correction (J2000 to JNOW) (2026-06-01)

### ✨ Core Math Accuracy
- **Coordinate Precession (J2000 to JNOW)**: Fixed a systematic ~8.5-arcminute polar alignment error offset reported by the community. Incoming plate solver coordinates (which are J2000.0) are now precessed natively to JNOW before running the mathematical solver, bringing 2PPA into perfect alignment calculations matching PHD2 guiding logs and other native solvers.

---

## v1.2.1.0 — Advanced Sequencer Automation & Multi-Frame Sampling (2026-05-31)

This major release delivers full automation via N.I.N.A.'s Advanced Sequencer, robust multi-frame averaging, and visual adaptations for single-knob base mounts.

### ⚙️ Sequencer & Automation
- **Automated Sequencer Item Integration**: Added a native `TwoPointPolarAlignmentSequenceItem` instruction to launch the polar alignment cycle automatically within Advanced Sequencer runs.
- **Dynamic Configuration Overrides**: Sequencer parameters are automatically injected as overrides into the `SettingsManager` and UI input fields are locked (`CanEditSettings = false`) during runs to prevent operational conflicts.
- **Contextual "Resume Sequence" Gatekeeper**: Automatically holds sequence execution in Phase F adjustments (live tuning), displaying a purple **[Resume Sequence]** button in the Dockable UI to let the user manually confirm final mechanical tweaks.
- **Agility & Homing Safetynet**: Allows the operator to stop, start, or home the mount manually as many times as they want inside the plugin without aborting the sequence instruction.
- **WPF Native Styling**: Refactored sequencer templates to inherit from N.I.N.A.'s native `SequenceBlockView` container, resolving visual glitches and blending seamlessly with the active light/dark themes.
- **Connection Validation Warnings**: Integrated `IValidatable` alerts to show red exclamation marks and tooltip warnings next to the sequence title when Camera or Mount are disconnected.

### ⚡ Multi-Frame Sampling & Mathematical Robustness
- **Multi-Frame Sampling**: Added an "Exposures Per Point" setting supporting Single, Double, and Triple sub-frame exposures at each measurement station to average out seeing jitter and wind gusts.
- **Advanced Spherical Outlier Rejection**: For triple sub-frame exposures, computes great-circle angular distances using 3D unit vectors to isolate and discard the worst coordinate solve (e.g., from a passing satellite or cloud) and averages the remaining two.
- **Sub-frame LST Drift Normalization**: Individual sub-frame solve coordinates are mathematically normalized to the station's anchor Local Sidereal Time ($LST$) before calculations, keeping consecutive exposures perfectly aligned.

### 🎨 Visual & UI UX Enhancements
- **Rotary Azimuth Knob Settings**: Added Clockwise (↻) and Counter-Clockwise (↺) rotational arrow prompts for the Azimuth axis (perfect for mounts with a single horizontal adjustment knob like the ZWO AM5).
- **Phase F Live Diagnostics Logging**: Restored high-precision calculated polar alignment errors in DMS and decimal arcminute formats for Altitude, Azimuth, and Total Error. Continuous live updates are written cleanly to the central N.I.N.A. log files.

---

## v1.1.0.2 — Sidereal LST Time-Drift Normalization & System Hardening (2026-05-28)

This release delivers sub-arcminute calculations using high-precision sidereal drift corrections, smart safety interlocks, and full codebase refactoring.

### ✨ Core Math Accuracy
- **LST Time-Drift Correction**: Captures the exact Local Sidereal Time ($LST_1$ and $LST_2$) at both measurement positions and corrects for celestial rotation during the mount's RA slew. This mathematically "freezes" Earth's rotation, eliminating systematic tracking drift ($3.5'$ to $5.5'$) to achieve sub-arcminute alignment accuracy.

### 🛡️ Safety & UI Enhancements
- **State-Aware Alternating Smart Restarts**: Smart restarts dynamically track and alternate rotation directions (East ➔ West ➔ East) on consecutive aborted runs, causing the mount to oscillate safely back and forth in the exact same physical space to prevent cord-wrap, cable strain, or tripod/pier collisions.
- **Immediate Slew Interruption**: Clicking **Stop** or **Home** in the middle of a slew instantly dispatches an ASCOM slew abort command (`StopSlew`), halting mount motors in their tracks instead of waiting for the slew to complete.
- **Rough Finder Failsafe Engine**: Restored the fully-automated, state-decoupled Rough Finder blind-solving rescue engine (leveraging secondary local solvers like Astrometry.net or AllSky) to dynamically recover initial misalignments $>10^\circ$.
- **Stale Results Visual Dimming**: Starting a new run immediately dims historic error card opacities to **45%** to clearly communicate stale status until new exposures solve and brighten the board.
- **WPF Thread Isolation**: Configured dispatcher queues to marshal string updates safely onto the UI thread, and pre-froze static brushes to guarantee thread-safe cross-threaded WPF calls.

### 🏗️ SOLID Architecture Refactor
- **Codebase Restructuring**: Consensually split the massive UI monolith into four testable layers: Stateless Domain Math (`TwoPointPolarSolver`), Asynchronous Workflow Controller (`AlignmentWorkflowController`), Reactive UI bindings (`ManualRotationWindow`), and Hardware Safety Interlocks (`SettingsManager`).
- **ASCOM Semaphore Interlocks**: Introduced a robust 30-second `HardwareTeardownTimeoutException` semaphore to prevent ASCOM drivers from zombie-locking the main N.I.N.A. application during emergency aborts.

---

## v1.1.0.0 / v1.0.3.8 — Reactive UI Custom Home & Manual Polish (2026-05-19)

### 🎨 Visual & UI UX Enhancements
- **Dynamic CUSTOM HOME Button**: The main `[HOME]` button dynamically transforms into a high-visibility, royal blue **`[CUSTOM HOME]`** button with updated ToolTips when the `Override Mount Home` parameter is toggled to `True`.
- **Lock Polar Home Row Added**: Fully documented the `Lock Polar Home` action button row in the configuration settings table, active only when `Override Mount Home` is enabled.
- **Manual Polish**: Removed Alt-Az mount references from the Custom Home position override cases in the User Manual to focus exclusively on standard equatorial astrophotography workflows.

---

## v1.0.0-beta — Beta 1.0 (2026-05-10)

Initial stable beta release of the 2-Point Polar Alignment plugin for N.I.N.A.

### ✨ Core Features
- **3D Rotation Matrix Solver**: Polar axis calculations hardened against Earth rotation drift and multi-hemisphere coordinate flips.
- **Full Manual Mode Support**: Camera-only operation for non-computerized, un-plugged, or manual mounts — no mount connection required.
- **Premium Glassmorphic UI**: Sleek dark-mode interface with dynamic hover glow effects, real-time error cards, and large-scale alignment typography.
- **Event-Driven Equipment Awareness**: Asynchronous tracking with zero-latency detection of hardware connectivity changes.
- **Native Profile Integration**: Settings automatically persist per-rig via N.I.N.A.'s native profile system.