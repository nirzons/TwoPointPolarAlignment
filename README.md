# 2-Point Polar Alignment Plugin for N.I.N.A. 🔭

A premium, modern, and highly accurate polar alignment plugin for N.I.N.A. (Nighttime Imaging 'N' Astronomy) that allows astrophotographers to align their telescope mounts with extreme precision using just two plate-solved points.

---

## ✨ Features & Aesthetics

*   **Glassmorphic Design**: Built using customized premium dark-mode styling with subtle color accents and clean typography (Inter/Roboto inspired) to match N.I.N.A.'s native professional aesthetics.
*   **Vector Art Integration**: Custom 16x16 pixel-aligned vector geometries representing a precise tracking arc and alignment stars, ensuring maximum clarity on dense screens.
*   **Dynamic Logs Console**: Built-in interactive logger terminal displaying colored real-time statuses with precise timestamps.

---

## 🚀 What is Currently Working (Phases A-E: Verified Under the Stars!)

The core polar alignment sequence has been successfully tested and verified under real stars (non-simulation):

1.  **Phase A: Pre-Flight Safety Audit**:
    *   Validates Camera and Mount connection state (`IsCameraConnected` / `IsMountConnected`) in real-time.
    *   Dynamically retrieves your N.I.N.A. active profile and validates that your selected Plate Solver is installed and configured.

2.  **Phase B: Initial Positioning & Verification**:
    *   Verifies mount starting home position and initializes reference anchors.
    *   Supports automatic slewing to pre-rotate the RA axis by half-range if requested.

3.  **Phase C & E: Capture & Plate Solve Measurements**:
    *   Triggers camera exposures using custom gain, exposure time, filter, and binning.
    *   Integrates with the active plate solver to parse solved coordinates and orientation angles.
    *   Features a built-in solver-mount physical separation safety threshold.

4.  **Phase D: Precise RA Rotation**:
    *   Supports fully automatic RA axis slewing or guides manual rotation with on-screen prompts.

---

## 🗺️ Implementation Roadmap

*   [x] **Phase A**: Pre-Flight Hardware & Plate Solver Validation (Verified under real stars! ⭐)
*   [x] **Phase B**: Initial Positioning & Verification (Verified under real stars! ⭐)
*   [x] **Phase C**: First Exposure Measurement (Verified under real stars! ⭐)
*   [x] **Phase D**: Precise RA Axis Rotation (Verified under real stars! ⭐)
*   [x] **Phase E**: Second Exposure Measurement (Verified under real stars! ⭐)
*   [ ] **Phase F**: Error Calculation & Live Correction Guide (Phase F math is complete; currently conducting daytime simulation testing before real star validation)
*   [ ] **Phase G**: GUI Improvement & Visual Polish (Once fully satisfied with functional performance, commence premium UI enhancements to make the interface look incredibly professional and polished)
