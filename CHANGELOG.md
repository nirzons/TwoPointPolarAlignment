# 2-Point Polar Alignment — Changelog

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