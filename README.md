# 2-Point Polar Alignment Plugin for N.I.N.A. 🔭

A premium, modern, and highly accurate polar alignment plugin for N.I.N.A. (Nighttime Imaging 'N' Astronomy) that allows astrophotographers to align their telescope mounts with extreme precision using just two plate-solved points.

---

## ✨ Features & Aesthetics

*   **Glassmorphic Design**: Built using customized premium dark-mode styling with subtle color accents and clean typography (Inter/Roboto inspired) to match N.I.N.A.'s native professional aesthetics.
*   **Vector Art Integration**: Custom 16x16 pixel-aligned vector geometries representing a precise tracking arc and alignment stars, ensuring maximum clarity on dense screens.
*   **Dynamic Logs Console**: Built-in interactive logger terminal displaying colored real-time statuses with precise timestamps.

---

## 🚀 What is Currently Working (Phase A: Pre-Flight Checks)

The plugin features an intelligent and robust pre-flight safety audit before initiating any sequence:

1.  **Hardware Connectivity Verification**:
    *   Validates Camera connection state (`IsCameraConnected`) in real-time.
    *   Validates Telescope Mount connection state (`IsMountConnected`) in real-time.
    *   Provides instant, localized on-screen error logs and red notifications if gear is disconnected.

2.  **Advanced Plate Solver Validation**:
    *   Dynamically retrieves your N.I.N.A. active profile settings.
    *   Extracts the selected `PlateSolverType` (e.g., `PlateSolve3`, `PlateSolve2`, `ASTAP`).
    *   Inspects the local executable path on your hard drive corresponding to the selected solver.
    *   If the path is unconfigured or the solver executable does **not** exist (like an uninstalled fallback), the plugin **safely blocks execution** and triggers a red toast notification.
    *   If the solver is fully active and operational, it authorizes sequence start with a green success notification.

3.  **Global Log Integration**:
    *   All plugin-specific events are seamlessly recorded inside N.I.N.A.'s native global system logs on your file system under `%LOCALAPPDATA%\NINA\Logs\`.

---

## 🗺️ Implementation Roadmap

*   [x] **Phase A**: Pre-Flight Hardware & Plate Solver Validation
*   [ ] **Phase B**: Initial Positioning & First Exposure Verification (Slewing and Solving)
*   [ ] **Phase C**: Main Slew & Second Point Verification
*   [ ] **Phase D**: Error Calculation & Live Correction Guide (Azimuth & Altitude Fine-Tuning)
