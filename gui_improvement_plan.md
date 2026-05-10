# Phase G: Premium GUI Improvement & Visual Polish Plan

This document outlines the detailed architectural, functional, and visual plan to elevate the **2-Point Polar Alignment** plugin interface into a state-of-the-art, premium user experience.

---

## 🎨 1. Dynamic Total Error Panel with Color-Coding

We will compute the absolute polar misalignment (the Euclidean norm of the Altitude and Azimuth errors) in real-time:
$$\text{Total Error} = \sqrt{\text{AltError}^2 + \text{AzError}^2}$$

### Thresholds and Color Schemes
The **Total Error** value will dynamically bind its text color to a `SolidColorBrush` based on the alignment precision achieved (measured in arcminutes):

| Error Range (Arcminutes) | Quality Level | Brush Hex Code | WPF SolidColorBrush |
| :--- | :--- | :--- | :--- |
| **$\le 1.0'$** | ✨ Excellent | `#00FF7F` | `SpringGreen` |
| **$1.0' < \text{Error} \le 3.0'$** | 🟢 Good | `#98FB98` | `PaleGreen` |
| **$3.0' < \text{Error} \le 10.0'$** | 🟡 Fair | `#FFD700` | `Gold` |
| **$> 10.0'$** | 🔴 Poor | `#FF4D4D` | `LightCoral` / `Red` |

### ViewModel Properties
We need to add these properties to `PolarAlignmentDockableVM.cs`:
*   `double TotalErrorValue`: The raw numeric hypotenuse (arcminutes) used for thresholds.
*   `string TotalError`: The formatted string (e.g., `00° 03' 20"`) shown in the UI.
*   `Brush TotalErrorColor`: The active color brush bound to the UI text element.

---

## 🧭 2. Real-Time Knob Turn Instructions

To guide the user through physical knob turns, the plugin will translate positive/negative coordinate offsets into direct, plain-English instructions with high-visibility arrows:

### Azimuth Instruction Logic
*   **If Azimuth Error $> 0$ (Pointing too far East)**:
    *   *Text*: `← Move Left / West`
*   **If Azimuth Error $< 0$ (Pointing too far West)**:
    *   *Text*: `Move Right / East →`

### Altitude Instruction Logic
*   **If Altitude Error $> 0$ (Pointing too far High)**:
    *   *Text*: `Move Down ↓`
*   **If Altitude Error $< 0$ (Pointing too far Low)**:
    *   *Text*: `Move Up ↑`

### ViewModel Properties
*   `string AzimuthInstruction`: Bound to the Azimuth card subtitle.
*   `string AltitudeInstruction`: Bound to the Altitude card subtitle.

---

## 🏛️ 3. Card-Based WPF Layout (`Options.xaml`)

We will rearrange the error displays in `Options.xaml` into a cohesive, responsive horizontal strip of cards with subtle rounded corners:

```
+------------------------+------------------------+------------------------+
|     AZIMUTH ERROR      |     ALTITUDE ERROR     |      TOTAL ERROR       |
|      00° 02' 18"       |      -00° 02' 25"      |      00° 03' 20"       |
|   ← Move left / west   |       Move up ↑        |    [Fair Alignment]    |
+------------------------+------------------------+------------------------+
```

Each card will follow a consistent vertical stack:
1.  **Header Label**: Semi-transparent, small, gray text (e.g., `AZIMUTH ERROR`).
2.  **Large Offset Value**: High-visibility text (e.g., `00° 02' 18"`).
3.  **Instruction Subtitle**: Clear, colored text showing the directional arrow and action (e.g., `← Move Left / West`).

---

## 🎯 4. Star-Centering Bullseye Radar HUD (Future Graphic Concept)

Instead of the cluttered rectangles and concentric circles over image crops found in standard alignment tools, we will eventually build an intuitive, gamified **Radar HUD**:

```
                 [ ALTITUDE UP ↑ ]
                       |
                       |      ★ (Current Mount Axis Pointer)
                 .---. | .---.
                /     \|/     \
               |   .-. | .-.   |
        [← LEFT] --| (★) |-- [RIGHT →]
               |   '-' | '-'   |
                \     /| \    /
                 '---' | '---'
                       |
                [ ALTITUDE DOWN ↓ ]
```

*   **Center Bullseye**: Represents the true Celestial Pole.
*   **Star Marker ($\bigstar$)**: Shifts dynamically across the canvas coordinates in real-time as the user turns the physical knobs.
*   ** Centering Mechanics**: The user turns knobs to slide the star marker onto the center crosshair. The target pulses glowing green when sub-arcminute alignment is reached.
