# 📖 2-Point Polar Alignment - User & Operation Manual
*Applicable Version: Beta v1.0.3.8*

Welcome to the **2-Point Polar Alignment** plugin for N.I.N.A.! This manual provides clear, comprehensive, and step-by-step instructions to install, configure, and operate the plugin under real night skies.

---

## 🛠️ Manual Installation Guide (Current Beta)

Until the plugin is officially published in the N.I.N.A. Plugin Store, please use these steps to install the current beta:

1. **Download**: Navigate to the [GitHub Releases section](https://github.com/nirzons/TwoPointPolarAlignment/releases). Locate the latest release block, expand the **Assets** section, and download **`TwoPointPolarAlignment-Beta-v1.0.3.8.zip`**.
   - *Tip*: You can also download a PDF version of this manual from the same Assets list for easy offline reference on your tablet or phone under the stars!
2. **Close N.I.N.A.**: Ensure that N.I.N.A. is not currently running.
3. **Navigate to Plugins Folder**: Go to your local N.I.N.A. plugins directory in Windows Explorer:
   `%LocalAppData%\NINA\Plugins\3.0.0\` (Replace `3.0.0` with your target major N.I.N.A. version if different).
4. **Create Directory**: Create a new folder named `TwoPointPolarAlignment` (if it does not already exist). If the folder is already there, you will overwrite its contents in the next step.
5. **Extract**: Extract all files from the downloaded zip package directly into that `TwoPointPolarAlignment` folder (replacing/overwriting any existing files if they were present). Make sure the `.dll` files are located in the root of that folder (not nested in a secondary subfolder).
6. **Activate**: Launch N.I.N.A. and verify that **2-Point Polar Alignment** appears as active in your **Plugins** dashboard. You can add it to your Imaging layout as a dockable tab!

<div style="page-break-after: always;"></div>

## 🚀 Step-by-Step Operation Manual

### 1. Preparation
1. Connect your **Camera**, **Mount/Telescope**, and **Plate Solver** in N.I.N.A.
2. Open the **2-Point Polar Alignment** tab.
3. Ensure your telescope mount is unparked and situated near its Home Position.
4. Configure your camera exposure, gain, filter, and other hardware properties inside the plugin interface (see the detailed [Configuration Settings](#2-configuration-settings) section below for specific recommended values).

---

### ⚡ Quick Start (Quick & Dirty)

1. **Point near the Pole**: Ensure your telescope is homed and pointing roughly at your hemisphere's celestial pole.
2. **Launch**: Click the large green **`[Start Alignment Sequence]`** button.
3. **Let the Mount Slew & Solve**: The telescope will automatically capture a starting image, plate-solve, rotate 90° in Right Ascension (RA), capture a second image, and solve again. Do not touch or nudge the mount while it is slewing or solving.
4. **Tweak your Knobs**: The interactive live loop starts instantly. Watch the live Altitude and Azimuth error cards on the dashboard and physically turn your mount's alignment knobs in the directions indicated (e.g., `Move Up ↑`, `← Move Left`) until the error numbers drop into a green rating.
5. **Redo**: Click **`[Stop]`** and then **`[Start Alignment Sequence]`** again to redo the entire flow in the opposite direction (Smart Restart).
6. **Finish**: Once satisfied with the alignment, click **`[Stop]`** or **`[HOME]`** to finalize and complete the sequence.

<div style="page-break-after: always;"></div>

### 2. Configuration Settings
Before starting, adjust these core parameters in the plugin interface:

| Parameter | Recommended Value | Description |
| :--- | :--- | :--- |
| **Exposure (s)** | `2.0` to `5.0` seconds | Long enough to capture a high star count for reliable plate solving. |
| **Gain** | Standard Capture Gain | Match your standard DSO imaging gain. |
| **Binning** | `1x1` or `2x2` | `2x2` is recommended to speed up downloads and improve solver performance. |
| **Rotation Amount (°)** | `90.0°` (Default) | The total RA axis rotation. Can be decreased if physical limits require it. |
| **Direction** | `East` or `West` | Select based on telescope clearance relative to the meridian or tripod legs. |
| **Rotation Method** | `Automatic` | **Automatic** commands the mount to slew automatically. Choose **Manual** for push-to trackers. |
| **Starting Point** | `Start at Home` | Defines the sequence start. `Pre-rotate Half Range` moves the mount RA by `-45°` before taking the first shot. |
| **Filter** | `(Current)` | Wide-band filter (like Luminance/L) to maximize star count, or `(Current)` to use the wheel's active slot. |
| **Offset** | `0` | The camera offset parameter to use during exposure capture. |
| **Plate Solve Retries**| `5` | Recommended number of plate solving retry attempts before raising a solve failure warning. |
| **Rough Finder** | `Enabled` | **Highly recommended!** Automatically activates a backup Blind Solving rescue if your initial deployment error is large. |
| **Alt Knob Visual** | `Standard` | Map Altitude instructions to standard arrows, Clockwise (↻), or Counter-Clockwise (↺) symbols. |
| **Override Mount Home**| `False` | Set to `True` to unlock custom celestial home position starting parameters (described in detail below). |
| **Lock Polar Home** | Action Button | Press to save the mount's active RA/Dec coordinates persistently in your N.I.N.A. profile as the custom starting position (active only when **Override Mount Home** is set to True). |

<div style="page-break-after: always;"></div>

## 🔄 Smart Restart & Mount Safety Control

The plugin includes several safety and automation features:

* **Smart Restarts (No Homing Required)**: Pressing **[Stop]** and then **[Start Alignment Sequence]** again (without moving the mount RA/Dec) triggers a **Smart Restart**. The plugin bypasses homing checks and starts the sequence instantly from the current position.
* **Safety Rotation Alternation**: The plugin automatically alternates the RA rotation direction on consecutive smart restarts (e.g., East ➔ West ➔ East ➔ West) to safely swing the mount back and forth in the same physical clearance space and prevent cable wrap.
* **Dynamic Reversed Flow Indicator**: A green **"Reversed Flow Active"** badge displays in the UI when the safety alternation runs the mount in the opposite direction from your configured **Direction** setting.
* **Immediate Slew Abort**: Clicking **[Stop]** or **[HOME]** at any point in the middle of a telescope slew immediately triggers a hardware-level ASCOM slew abort, halting the mount's motors in their tracks instantly.
* **Infinite Oscillation**: You can stop and restart the sequence as many times as needed. The safety alternation loop continues indefinitely, toggling the rotation direction back and forth (and turning the green badge on and off accordingly) on each consecutive restart.

<div style="page-break-after: always;"></div>

## ⚠️ Custom Polar Home Position Override

### **When to use this feature?**
By default, the plugin expects the mount to start in its native **Home Position** (pointing directly toward the Celestial Pole, with Declination near 90°). The plugin uses an **intelligent declination heuristic** during startup checks to dynamically handle different mount types:

* **Case 1: Standard Polar Homing Mounts (e.g., GSServer, OnStepX)**: 
  These mounts physically home near the celestial pole. *If the mount is not homed*: The plugin will halt and prompt you to run a native **[HOME]** command. *If the mount is homed but slightly misaligned*: The plugin raises a **"Mount Homing Misaligned"** warning prompting you to check your physical index marks. **No override is required!**
* **Case 2: Custom / Non-Polar Homing Mounts (e.g., ASCOM Simulator)**:
  These mounts define "Home" differently (e.g., ASCOM Simulator homes pointing at the Equator, `Dec = 0°`). The plugin detects this and raises a **"Polar Home Override Required"** warning. To prevent unsafe movement, if you click the plugin's **[HOME]** button while this override is disabled, the plugin will **automatically intercept and block the native ASCOM home slew**.
* **Case 3: Mechanical Constraints & Line-of-Sight Obstructions**:
  Even if your mount natively homes perfectly, physical variables (cable strain, trees, roofs, walls) might make a custom starting location preferable. By enabling the override, you can manually position the telescope to a custom, safe starting RA/Dec orientation (still pointing near the pole) to clear obstructions and avoid cable strain, then lock it.

### **How to configure it?**
1. **Enable the Override**: In the configuration panel, change the **Override Mount Home** setting from **False** to **True**.
2. **Position your Mount**: Slew your mount to your desired physical starting alignment position near the celestial pole (Declination axis must be pointing near the pole, `|Dec| ≈ 90°`).
3. **Lock the Custom Position**: Click the rose-red **[Lock Polar Home]** button next to the override parameter. A confirmation popup will confirm your custom RA/Dec starting coordinates are now locked.
4. **Operation**:
   - The plugin's startup routine will now validate your starting position against your **custom locked coordinates** instead of the mount's native ASCOM home state.
   - Clicking the **[HOME]** button (which dynamically changes to **[CUSTOM HOME]** when the override is active) will automatically slew the telescope directly to your custom locked coordinates.
   - These locked coordinates are saved persistently within your active N.I.N.A. profile!

<div style="page-break-after: always;"></div>

## 🧭 Live Adjustment Tips

When you enter the **interactive adjustment loop**, the plugin provides live Altitude and Azimuth cards:

1. **Watch the live numbers**: Make physical mechanical knob adjustments on your mount.
2. **Follow the direction prompts**:
   - **Altitude**: Follow `Move Up ↑` or `Move Down ↓` (or your chosen circular arrows).
   - **Azimuth**: Follow `← Move Left` or `Move Right →` (relative to facing the celestial pole).
3. **Golden Highlight**: If one axis has a significantly larger error than the other, that card will glow with a golden highlight, indicating it is the **priority axis** you should adjust first.
4. **Finish**: Once you reach a **🟢 Good** or **✨ Excellent** rating, click **[Stop]** to finalize your alignment!
