# 📖 2-Point Polar Alignment - User & Operation Manual
*Applicable Version: v1.5.3.0*

Welcome to the **2-Point Polar Alignment** plugin for N.I.N.A.! This manual provides clear, comprehensive, and step-by-step instructions to install, configure, and operate the plugin under real night skies.

---

## 🛠️ Installation Guide

### 1. Recommended: N.I.N.A. Plugin Store (Automatic)
The plugin is officially published in the centralized N.I.N.A. Plugin Store, making installation a simple, one-click process:

1. **Launch N.I.N.A.**: Ensure your imaging PC is connected to the internet.
2. **Navigate to Plugins**: Go to the **Plugins** tab on the left-side navigation panel.
3. **Browse Available Plugins**: Click on **Available** in the top tab menu.
4. **Find 2-Point Polar Alignment**: Scroll down to locate **2-Point Polar Alignment** (or use the search bar).
5. **Install**: Click the **Install** button. N.I.N.A. will automatically download and unpack the correct plugin package.
6. **Restart N.I.N.A.**: Close and restart N.I.N.A. to activate the plugin. 
7. **Verify**: Open N.I.N.A. and confirm that **2-Point Polar Alignment** appears active under your **Plugins** -> **Installed** list. You can now add it as a dockable tab to your **Imaging** layout!

### 2. Manual Installation (For Development & Offline PCs)
If you need to install the plugin manually (e.g., from a custom compiled release or on an offline computer):

1. **Download / Build**: Obtain the plugin ZIP archive containing the compiled binaries, or build the project from source.
2. **Locate plugins directory**: Navigate to your local AppData directory: `%LOCALAPPDATA%\NINA\Plugins\3.0.0\` (typically `C:\Users\<YourUsername>\AppData\Local\NINA\Plugins\3.0.0\`).
3. **Create folder**: Create a new subfolder named precisely **`2-Point Polar Alignment`**.
4. **Extract files**: Extract/copy the compiled binary files into that folder:
   - `NirZonshine.NINA.TwoPointPolarAlignment.dll`
   - `NirZonshine.NINA.TwoPointPolarAlignment.pdb`
   - `NirZonshine.NINA.TwoPointPolarAlignment.deps.json`
   - `NirZonshine.NINA.TwoPointPolarAlignment.runtimeconfig.json`
   - `NirZonshine.NINA.TwoPointPolarAlignment.dll.config`
5. **Restart & Verify**: Restart N.I.N.A. to load the plugin.

---

## 🚀 Step-by-Step Operation Manual

### 1. Preparation
1. Connect your **Camera**, **Mount/Telescope**, and **Plate Solver** in N.I.N.A.
2. Open the **2-Point Polar Alignment** tab.
3. Ensure your telescope mount is unparked and situated near its Home Position.
4. Configure your camera exposure, gain, filter, and other hardware properties inside the plugin interface.

---

### ⚡ Quick Start (Quick & Dirty)

1. **Point near the Pole**: Ensure your telescope is homed and pointing roughly at your hemisphere's celestial pole.
2. **Launch**: Click the large green **`[Start Alignment Sequence]`** button.
3. **Let the Mount Slew & Solve**: The telescope will automatically capture a starting image, plate-solve, rotate 90° in Right Ascension (RA), capture a second image, and solve again. Do not touch or nudge the mount while it is slewing or solving.
4. **Tweak your Knobs**: The interactive live loop starts instantly. Watch the live Altitude and Azimuth error cards on the dashboard and physically turn your mount's alignment knobs in the directions indicated (e.g., `Move Up ↑`, `← Move Left`) until the error numbers drop into a green rating.
5. **Redo**: Click **`[Stop]`** and then **`[Start Alignment Sequence]`** again to redo the entire flow in the opposite direction (Smart Restart).
6. **Finish**: Once satisfied with the alignment, click **`[Stop]`** or **`[HOME]`** to finalize and complete the sequence.

---

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
| **Filter** | `(Current)` | Wide-band filter (like Luminance/L) to maximize star count, or `(Current)` to use the active slot. |
| **Offset** | `0` | The camera offset parameter to use during exposure capture. |
| **Plate Solve Retries**| `5` | Recommended number of plate solving retry attempts before raising a solve failure warning. |
| **Exposures Per Point**| `1`, `2`, or `3` | Multi-frame averaging. Captures up to 3 exposures at each station to average out seeing jitter and wind gusts. Uses unit-vector based outlier rejection. |
| **Rough Finder** | `Enabled` | **Highly recommended!** Automatically activates a backup Blind Solving rescue if your initial deployment error is large. |
| **Alt Knob Visual** | `Standard` | Map Altitude instructions to standard arrows, Clockwise (↻), or Counter-Clockwise (↺) symbols. |
| **Azimuth Knob Visual**| `Standard` | Map Azimuth instructions to standard arrows, Clockwise (↻), or Counter-Clockwise (↺) symbols (perfect for single-knob AM5 mounts). |
| **Override Mount Home**| `False` | Set to `True` to unlock custom celestial home position starting parameters (described in detail below). |
| **Lock Polar Home** | Action Button | Press to save the mount's active RA/Dec coordinates persistently in your active profile. |

---

> [!NOTE]
> **Filter Configuration & Profile Synchronization**:
> The list of filters displayed in the dropdown is loaded from your active N.I.N.A. profile (configured in N.I.N.A. under Options -> Equipment -> Filter Wheel). If you edit, rename, or add filter names there, you **must restart N.I.N.A.** for the updated list to be populated in the plugin's interface. If no filters are defined in your active profile, the plugin will fallback to a default list (`Luminance`, `Red`, `Green`, `Blue`, `Ha`, `OIII`, `SII`).

---

## ⚙️ Advanced Sequencer Integration

Version `1.3.0.0` introduces native integration with N.I.N.A.'s **Advanced Sequencer**, allowing you to automate your polar alignment cycle as part of your nightly sequence!

### 1. Adding the Instruction
1. Open N.I.N.A.'s **Advanced Sequencer** panel.
2. In the right-side **Instructions** search bar, type `2-Point Polar Alignment`.
3. Locate the instruction under the **Polar Alignment** category (represented by our custom curved arc + two stars icon).
4. Drag and drop it into your sequence list (e.g., at the very start of your target sequence, right after cool-down).

### 2. Live Validation Alerts
The sequencer item contains a built-in pre-flight validation checker:
* If your **Camera** or **Telescope Mount** are not connected in N.I.N.A. when you compile or run your sequence, a **red exclamation mark** will light up next to the instruction name in the sequence tree.
* Hovering over the warning icon will display a clear tooltip: `"Please connect the camera."` and/or `"Please connect the telescope mount."`.
* Once you connect the equipment, the validation warning will immediately clear.

### 3. Setting Parameter Overrides
When you click on the sequence block, you will see a polished options grid exposing all 12 configuration parameters.
* Any values you configure here will act as **temporary overrides**. 
* When the sequence launches this instruction, these overrides are injected directly into the `SettingsManager` and the main Dockable UI fields will automatically lock (`CanEditSettings = false`) to prevent human interference or UI conflicts.
* Once the instruction completes, the overrides are cleanly wiped, restoring your default profile settings.

### 4. The "Resume Sequence" Gatekeeper (Manual Alignment Hold)
When the sequencer executes the instruction:
1. It unparks the mount and automatically coordinates the starting exposures, slews, and plate solves.
2. Once the mathematical solver completes, the plugin enters the **Live Adjustments** tuning stage.
3. The sequencer **pauses and holds** execution on this step, preventing the rest of your target sequence from running while you make physical mechanical adjustments.
4. Watch the live error dashboard on your PC (or a tablet/phone under the stars) and physically tweak your ALT/AZ knobs.
5. In the plugin's main Dockable UI, the "Start Alignment" button automatically shifts to display side-by-side with a new purple-accented **`[Resume Sequence]`** button.
6. Once you are satisfied with the polar error numbers, click the **`[Resume Sequence]`** button.
7. The plugin instantly halts the live loop, unbinds all settings overrides, unlocks the user interface, marks the sequence item as successful, and N.I.N.A. immediately advances to the next step in your imaging run!

### 5. Automated Sequence Auto-Complete, Verification Passes, Measurement Only & Force Home Position
From version 1.5.1.0, you can automate or customize the completion and homing behavior of the sequencer instruction:
* **Force Home Position**: Enabled (`true`) by default. When checked, the plugin automatically commands the mount to find its Home position (or slews to your locked Custom Polar Home if *Override Mount Home* is active) before starting the alignment routine. When unchecked, the plugin assumes the mount is already at home or in a position ready for alignment.
* **Measurement Only**: When checked, the plugin performs the initial two measurements, logs the results, and immediately auto-completes and advances the sequence without entering the live adjustment loop. This is designed for permanent observatories that want to programmatically check their polar alignment drift over time.
* **Auto-Complete Tolerance**: Specify a target polar error threshold in arcminutes (e.g. `1.0`′). If set to `0.0`, auto-complete is disabled, and manual resume is required.
* **Stable Frames**: The number of consecutive solved frames (default `3`) that must remain below the tolerance threshold to trigger completion. This prevents premature completion due to seeing jitter or wind during adjustments. If a plate solve fails or error spikes above the tolerance, the stability counter is immediately reset to `0`.
* **Verification Passes**: Specify the number of validation sweeps to perform in the opposite direction (default `0`). If set to `1` or more, once the target error is stable, the plugin automatically stops, slews the mount in the **reverse direction** back to home (leveraging the plugin's Smart Restart rotation safety logic), recalculates the polar alignment from scratch, and ensures it remains below the tolerance before finally advancing. This validates that your physical adjustments are mechanically stable and gear backlash has settled.
* **Real-time Error Display**: The measured total polar error is dynamically updated in the title of the instruction block (e.g. `2-Point Polar Alignment (01' 10")`) as well as on the right-hand side of the instruction block under a "Total Error" label, making it visible on both the Sequencer and Imaging tabs during and after sequence completion.

---

## 🔄 Smart Restart & Mount Safety Control

The plugin includes several safety and automation features:

* **Smart Restarts (No Homing Required)**: Pressing **[Stop]** and then **[Start Alignment Sequence]** again (without moving the mount RA/Dec) triggers a **Smart Restart**. The plugin bypasses homing checks and starts the sequence instantly from the current position.
* **Safety Rotation Alternation**: The plugin automatically alternates the RA rotation direction on consecutive smart restarts (e.g., East ➔ West ➔ East ➔ West) to safely swing the mount back and forth in the same physical clearance space and prevent cable wrap.
* **Dynamic Reversed Flow Indicator**: A green **"Reversed Flow Active"** badge displays in the UI when the safety alternation runs the mount in the opposite direction from your configured **Direction** setting.
* **Immediate Slew Abort**: Clicking **[Stop]** or **[HOME]** at any point in the middle of a telescope slew immediately triggers a hardware-level ASCOM slew abort, halting the mount's motors in their tracks instantly.
* **Infinite Oscillation**: You can stop and restart the sequence as many times as needed. The safety alternation loop continues indefinitely, toggling the rotation direction back and forth (and turning the green badge on and off accordingly) on each consecutive restart.

---

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

---

## 🧭 Live Adjustment Tips

When you enter the **interactive adjustment loop**, the plugin provides live Altitude and Azimuth cards:

1. **Watch the live numbers**: Make physical mechanical knob adjustments on your mount.
2. **Follow the direction prompts**:
   - **Altitude**: Follow `Move Up ↑` or `Move Down ↓` (or your chosen circular arrows).
   - **Azimuth**: Follow `← Move Left` or `Move Right →` (relative to facing the celestial pole).
3. **Golden Highlight**: If one axis has a significantly larger error than the other, that card will glow with a golden highlight, indicating it is the **priority axis** you should adjust first.
4. **Finish**: Once you reach a **🟢 Good** or **✨ Excellent** rating, click **[Stop]** to finalize your alignment!
