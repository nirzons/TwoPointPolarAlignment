# Mathematical Model of the 2-Point Polar Alignment Algorithm

## Abstract
This document details the exact mathematical model implemented in the 2-Point Polar Alignment plugin. The algorithm calculates the true mechanical polar axis of an equatorial mount by analyzing two plate-solved images taken at different right ascension (RA) angles. The model employs 3D vector kinematics, orthogonal frame rotations, Local Sidereal Time (LST) drift normalization, and Rodrigues' rotation formula to achieve sub-arcminute precision.

---

## 1. Celestial Coordinate Geometry and 3D Vector Representation

The foundation of the algorithm relies on mapping standard equatorial coordinates—Right Ascension ($\alpha$) and Declination ($\delta$)—onto a 3-dimensional Cartesian unit sphere. 

Given an equatorial coordinate $(\alpha, \delta)$, where $\alpha$ is measured in hours ($0 \le \alpha < 24$) and $\delta$ is measured in degrees ($-90 \le \delta \le 90$), the Cartesian unit vector $\mathbf{v}$ is defined as:

$$
v_x = \cos(\delta) \cos(\alpha_{rad})
$$

$$
v_y = \cos(\delta) \sin(\alpha_{rad})
$$

$$
v_z = \sin(\delta)
$$

where $\alpha_{rad} = \alpha \times \frac{15\pi}{180}$ and $\delta$ is converted to radians. The celestial North Pole corresponds to the vector $(0, 0, 1)$.

## 2. Local Sidereal Time (LST) Drift Normalization

A fundamental challenge in multi-point astrometric alignment is the continuous apparent motion of the celestial sphere due to Earth's rotation. During the alignment sequence, mount tracking is disabled to isolate the pure mechanical RA rotation of the mount. Consequently, between the capture of Point 1 ($t_1$) and Point 2 ($t_2$), the sky drifts.

To eliminate this systematic error, the algorithm applies an LST time-drift normalization. Let $LST_1$ and $LST_2$ be the Local Sidereal Times at the moments of capture for the two points.

The drift is calculated as:

$$
\Delta LST = LST_2 - LST_1
$$

The coordinate of Point 1 is mathematically shifted forward in time to "freeze" the celestial sphere relative to Point 2:

$$
\alpha_{1, norm} = \alpha_1 + \Delta LST \pmod{24}
$$

This ensures that the spatial transformation between Point 1 and Point 2 represents purely the mechanical rotation of the mount around its physical polar axis, completely decoupled from Earth's rotation.

## 3. Derivation of the Mechanical Polar Axis

Let $\mathbf{v}_1$ and $\mathbf{v}_2$ be the 3D unit vectors of the normalized Point 1 and Point 2, respectively. The plate solver also provides the Position Angle ($PA$) for both points, representing the camera sensor's rotation relative to celestial North.

### 3.1 Establishing Orthogonal Local Frames
For each point $i \in \{1, 2\}$, we construct a local orthonormal basis. First, we define the vector pointing to celestial North, orthogonalized against $\mathbf{v}_i$:

$$
\mathbf{N}_i = \frac{(0,0,1) - v_{i,z} \mathbf{v}_i}{|| (0,0,1) - v_{i,z} \mathbf{v}_i ||}
$$

The East vector is the cross product:

$$
\mathbf{E}_i = \frac{(0,0,1) \times \mathbf{v}_i}{|| (0,0,1) \times \mathbf{v}_i ||}
$$

Using the camera's Position Angle ($PA_i$), the camera's local Y-axis and X-axis are derived:

$$
\mathbf{Y}_i = \cos(PA_i) \mathbf{N}_i + \sin(PA_i) \mathbf{E}_i
$$

$$
\mathbf{X}_i = \mathbf{Y}_i \times \mathbf{v}_i
$$

### 3.2 Calculating the Rotation Matrix and Axis
The mechanical rotation of the mount transitions the basis $(\mathbf{X}_1, \mathbf{Y}_1, \mathbf{v}_1)$ to $(\mathbf{X}_2, \mathbf{Y}_2, \mathbf{v}_2)$. This transition is described by a $3 \times 3$ rotation matrix $R$.

The components of $R$ are calculated via dot products of the basis vectors. The physical axis of rotation, $\mathbf{P}$, corresponds to the eigenvector of $R$ associated with the eigenvalue $\lambda = 1$. It can be extracted algebraically from the skew-symmetric components of $R$:

$$
r_{32} = \mathbf{X}_{2,z} \mathbf{X}_{1,y} + \mathbf{Y}_{2,z} \mathbf{Y}_{1,y} + \mathbf{v}_{2,z} \mathbf{v}_{1,y}
$$

$$
r_{23} = \mathbf{X}_{2,y} \mathbf{X}_{1,z} + \mathbf{Y}_{2,y} \mathbf{Y}_{1,z} + \mathbf{v}_{2,y} \mathbf{v}_{1,z}
$$

$$
r_{13} = \mathbf{X}_{2,x} \mathbf{X}_{1,z} + \mathbf{Y}_{2,x} \mathbf{Y}_{1,z} + \mathbf{v}_{2,x} \mathbf{v}_{1,z}
$$

$$
r_{31} = \mathbf{X}_{2,z} \mathbf{X}_{1,x} + \mathbf{Y}_{2,z} \mathbf{Y}_{1,x} + \mathbf{v}_{2,z} \mathbf{v}_{1,x}
$$

$$
r_{21} = \mathbf{X}_{2,y} \mathbf{X}_{1,x} + \mathbf{Y}_{2,y} \mathbf{Y}_{1,x} + \mathbf{v}_{2,y} \mathbf{v}_{1,x}
$$

$$
r_{12} = \mathbf{X}_{2,x} \mathbf{X}_{1,y} + \mathbf{Y}_{2,x} \mathbf{Y}_{1,y} + \mathbf{v}_{2,x} \mathbf{v}_{1,y}
$$

The unnormalized axis vector $\mathbf{P}_{raw}$ is given by:

$$
\mathbf{P}_{raw} = (r_{32} - r_{23}, r_{13} - r_{31}, r_{21} - r_{12})
$$

Normalizing this vector yields the true unit polar axis $\mathbf{P}$. The direction (sign) of the Z-component is forced to match the site's hemisphere (positive for Northern, negative for Southern).

## 4. Live Error Evaluation via Rodrigues' Formula

During the adjustment phase, continuous plate solving provides a live coordinate $\mathbf{v}_{live}$ at time $LST_{live}$. 
To maintain accuracy, the live coordinate's RA is first corrected for any LST drift that has occurred since the baseline Point 2 ($LST_2$).

To determine the current position of the polar axis as the mount is physically adjusted, we compute the rotation required to move the original Point 2 ($\mathbf{v}_2$) to the current live coordinate ($\mathbf{v}_{live}$). 

The rotation axis $\mathbf{k}$ and angle $\theta$ are:

$$
\mathbf{k} = \frac{\mathbf{v}_2 \times \mathbf{v}_{live}}{|| \mathbf{v}_2 \times \mathbf{v}_{live} ||}
$$

$$
\sin(\theta) = || \mathbf{v}_2 \times \mathbf{v}_{live} ||
$$

$$
\cos(\theta) = \mathbf{v}_2 \cdot \mathbf{v}_{live}
$$

We apply this exact rotation to the previously computed initial polar axis $\mathbf{P}$ using Rodrigues' rotation formula:

$$
\mathbf{P}_{calculated} = \mathbf{P} \cos(\theta) + (\mathbf{k} \times \mathbf{P}) \sin(\theta) + \mathbf{k} (\mathbf{k} \cdot \mathbf{P}) (1 - \cos(\theta))
$$

This provides a mathematically rigorous live polar axis that holds true even for macroscopic adjustments, entirely avoiding small-angle approximations.

## 5. Horizontal Error Calculation

Finally, $\mathbf{P}_{calculated}$ is converted into Horizontal coordinates (Altitude and Azimuth) to provide actionable mechanical feedback. 
The expected altitude of the true celestial pole is exactly equal to the Site Latitude ($\phi$), and the true azimuth is $0^\circ$ (North) or $180^\circ$ (South).

By computing the Alt/Az of $\mathbf{P}_{calculated}$ and subtracting the true pole Alt/Az coordinates, the algorithm yields the exact orthogonal Altitude and Azimuth error components in arcminutes, directing the user to mechanical alignment.

## 6. Summary

The 2-Point Polar Alignment algorithm integrates 3D vector transformations with precise time-domain corrections to deliver a robust and highly accurate alignment solution. By neutralizing apparent celestial motion through LST Time-Drift Normalization, the math is strictly isolated to the mechanical geometry of the mount. Furthermore, the use of continuous matrix-based spatial rotations (via Rodrigues' formula) ensures that real-time error evaluations remain mathematically rigorous, even during large mechanical adjustments. This comprehensive approach eliminates systematic errors found in simpler planar approximations, yielding sub-arcminute alignment precision under all practical field conditions.
