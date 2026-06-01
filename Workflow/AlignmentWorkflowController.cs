using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Media;
using NINA.Core.Model;
using NINA.Core.Model.Equipment;
using NINA.Core.Utility.Notification;
using NINA.Equipment.Interfaces.Mediator;
using NINA.Equipment.Model;
using NINA.PlateSolving;
using NINA.PlateSolving.Interfaces;
using NINA.Profile.Interfaces;
using NirZonshine.NINA.TwoPointPolarAlignment.Domain;
using NirZonshine.NINA.TwoPointPolarAlignment.Solvers;
using NirZonshine.NINA.TwoPointPolarAlignment.Services;
using NINA.Astrometry;

namespace NirZonshine.NINA.TwoPointPolarAlignment.Workflow {
    public class AlignmentWorkflowController {
        private readonly IProfileService _profileService;
        private readonly ICameraMediator _cameraMediator;
        private readonly ITelescopeMediator _telescopeMediator;
        private readonly IPlateSolverFactory _plateSolverFactory;
        private readonly IImagingMediator _imagingMediator;
        private readonly IFilterWheelMediator _filterWheelMediator;
        private readonly IPolarSolver _polarSolver;
        private readonly SettingsManager _settingsManager;

        public Func<RescuePromptArgs, Task<bool>> OnInterventionRequested { get; set; }
        public Func<AlignmentWorkflowContext, double, RotationDirection, Coordinates, CaptureSequence, ICaptureSolver, bool, Task> OnManualRotationRequested { get; set; }

        private readonly System.Threading.SemaphoreSlim _hardwareInterlock = new System.Threading.SemaphoreSlim(1, 1);

        public AlignmentWorkflowController(
            IProfileService profileService, ICameraMediator cameraMediator, ITelescopeMediator telescopeMediator,
            IPlateSolverFactory plateSolverFactory, IImagingMediator imagingMediator, IFilterWheelMediator filterWheelMediator,
            IPolarSolver polarSolver, SettingsManager settingsManager) {
            _profileService = profileService;
            _cameraMediator = cameraMediator;
            _telescopeMediator = telescopeMediator;
            _plateSolverFactory = plateSolverFactory;
            _imagingMediator = imagingMediator;
            _filterWheelMediator = filterWheelMediator;
            _polarSolver = polarSolver;
            _settingsManager = settingsManager;
        }

        private async Task<T> ExecuteHardwareOperationAsync<T>(Func<Task<T>> operation, CancellationToken token, string operationName = "Hardware Operation") {
            bool acquired = false;
            try { acquired = await _hardwareInterlock.WaitAsync(TimeSpan.FromSeconds(30), token); } catch (OperationCanceledException) { throw; }
            if (!acquired) throw new HardwareTeardownTimeoutException($"Hardware driver hung for more than 30 seconds during {operationName}. Safety abort triggered.");
            try { return await operation(); } finally { _hardwareInterlock.Release(); }
        }

        private async Task ExecuteHardwareOperationAsync(Func<Task> operation, CancellationToken token, string operationName = "Hardware Operation") {
            bool acquired = false;
            try { acquired = await _hardwareInterlock.WaitAsync(TimeSpan.FromSeconds(30), token); } catch (OperationCanceledException) { throw; }
            if (!acquired) throw new HardwareTeardownTimeoutException($"Hardware driver hung for more than 30 seconds during {operationName}. Safety abort triggered.");
            try { await operation(); } finally { _hardwareInterlock.Release(); }
        }

        public async Task ExecuteWorkflowAsync(AlignmentWorkflowContext context, CancellationToken token, IProgress<AlignmentProgressReport> progress, Action<ImageSource> updateThumbnail) {
            ReportLog(progress, "Starting alignment sequence...");
            
            var rand = new Random();
            context.ManualSimBiasRA = (rand.NextDouble() > 0.5 ? 1.0 : -1.0) * (0.25 + rand.NextDouble() * 0.25) / 2.0 / 15.0;
            context.ManualSimBiasDec = (rand.NextDouble() > 0.5 ? 1.0 : -1.0) * (0.25 + rand.NextDouble() * 0.25) * 3.0;
            
            await ExecutePreFlightAsync(context, token, progress);
            await ExecuteInitialPositioningAsync(context, token, progress);

            PlateSolveResult result1;
            try {
                result1 = await ExecuteMeasurementLoopAsync(context, token, progress, updateThumbnail, 1);
            } catch (OperationCanceledException) {
                throw;
            } catch (Exception ex) {
                if (await AttemptRescueIfNeeded(ex.Message, token, progress, context)) {
                    ReportLog(progress, "Rough Finder Rescue cycle completed successfully. Sequence halted per operator feedback so user can initialize final high-precision run manually.");
                    throw new OperationCanceledException("Alignment sequence halted for interactive rescue.");
                }
                throw;
            }

            double distToPole = 90.0 - Math.Abs(result1.Coordinates.Dec);
            if (distToPole > 10.0) {
                string failureReason = $"Telescope solved position (Dec {result1.Coordinates.Dec:F2}°) is {distToPole:F1}° away from Celestial Pole (exceeds 10° limit).";
                if (await AttemptRescueIfNeeded(failureReason, token, progress, context)) {
                    ReportLog(progress, "Rough Finder Rescue cycle completed successfully. Sequence halted per operator feedback so user can initialize final high-precision run manually.");
                    throw new OperationCanceledException("Alignment sequence halted for interactive rescue.");
                } else {
                    throw new InvalidOperationException(failureReason);
                }
            }
            context.Coordinates1 = result1.Coordinates;
            context.Angle1 = result1.PositionAngle;
            context.LstMeasurement1 = _telescopeMediator.GetInfo()?.SiderealTime ?? context.Coordinates1.RA;

            await ExecuteRotationAsync(context, token, progress, updateThumbnail);

            var result2 = await ExecuteMeasurementLoopAsync(context, token, progress, updateThumbnail, 2);
            context.Coordinates2 = result2.Coordinates;
            context.Angle2 = result2.PositionAngle;
            context.LstMeasurement2 = _telescopeMediator.GetInfo()?.SiderealTime ?? context.Coordinates2.RA;

            await ExecuteCalculationAndLiveAdjustmentAsync(context, token, progress, updateThumbnail);
        }
        
        private void ReportLog(IProgress<AlignmentProgressReport> progress, string log) {
            progress.Report(new AlignmentProgressReport { LogMessage = log });
        }
        
        private void ReportStatus(IProgress<AlignmentProgressReport> progress, string status, string colorHex) {
            progress.Report(new AlignmentProgressReport { StatusText = status, StatusColorHex = colorHex });
        }

        private async Task ExecutePreFlightAsync(AlignmentWorkflowContext context, CancellationToken token, IProgress<AlignmentProgressReport> progress) {
            ReportLog(progress, "Initiating Phase A (Pre-flight Checks)...");

            if (!(_cameraMediator.GetInfo()?.Connected ?? false)) {
                string err = "Error: Camera is not connected!";
                ReportLog(progress, err);
                throw new InvalidOperationException(err);
            }

            bool isMountConnected = _telescopeMediator.GetInfo()?.Connected ?? false;
            if (!isMountConnected && _settingsManager.Method != RotationMethod.Manual) {
                string err = "Error: Telescope Mount is not connected!";
                ReportLog(progress, err);
                throw new InvalidOperationException(err);
            }

            if (!string.IsNullOrEmpty(_settingsManager.Filter) && _settingsManager.Filter != "(Current)") {
                if (!(_filterWheelMediator?.GetInfo()?.Connected ?? false)) {
                    string err = $"Error: Specific filter '{_settingsManager.Filter}' is selected, but the Filter Wheel is not connected!";
                    ReportLog(progress, err);
                    throw new InvalidOperationException(err);
                }
            }
            
            ReportLog(progress, "Phase A (Pre-flight Checks) completed successfully!");
            await Task.Delay(1000, token);
            
            if (isMountConnected && (_telescopeMediator.GetInfo()?.TrackingEnabled ?? false)) {
                try { _telescopeMediator.SetTrackingEnabled(false); ReportLog(progress, "Disabling telescope tracking."); } catch { }
            }
        }

        private async Task ExecuteInitialPositioningAsync(AlignmentWorkflowContext context, CancellationToken token, IProgress<AlignmentProgressReport> progress) {
            ReportLog(progress, "Initiating Phase B (Initial Positioning)...");
            
            context.IsSimulation = (_cameraMediator.GetInfo()?.Name?.Contains("Simulator", StringComparison.OrdinalIgnoreCase) ?? false) ||
                                   (_telescopeMediator.GetInfo()?.Name?.Contains("Simulator", StringComparison.OrdinalIgnoreCase) ?? false);
            context.CurrentSimulationOffset = 0.0;
            
            var currentPosition = (_telescopeMediator.GetInfo()?.Connected ?? false) ? _telescopeMediator.GetCurrentPosition() : null;
            
            if (currentPosition != null) {
                var info = _telescopeMediator.GetInfo();
                
                if (info != null && info.Connected) {
                    bool isNearPole = Math.Abs(Math.Abs(currentPosition.Dec) - 90.0) < 1.0;
                    
                    // --- Smart Restart Check ---
                    bool isSmartRestart = false;
                    if (context.LastStoppedCoordinates != null) {
                        double raDiffFromStopped = Math.Abs(currentPosition.RA - context.LastStoppedCoordinates.RA);
                        if (raDiffFromStopped > 12.0) raDiffFromStopped = 24.0 - raDiffFromStopped;
                        
                        double decDiffFromStopped = Math.Abs(currentPosition.Dec - context.LastStoppedCoordinates.Dec);
                        
                        // If the RA is within 0.05 hours (0.75 degrees) and DEC is within 0.2 degrees, it's a smart restart!
                        if (raDiffFromStopped < 0.05 && decDiffFromStopped < 0.2) {
                            isSmartRestart = true;
                        }
                    }
                    
                    if (isSmartRestart) {
                        RotationDirection prevDir = context.LastStoppedDirection ?? _settingsManager.Direction;
                        context.ActiveDirection = prevDir == RotationDirection.East ? RotationDirection.West : RotationDirection.East;
                        context.ActivePreRotate = false;
                        context.HasRoughFinderSimTriggered = true;
                        
                        bool isReversed = context.ActiveDirection != _settingsManager.Direction;
                        ReportLog(progress, $"🔄 Smart Restart Detected! Mount position did not change since stop. Active Direction: {context.ActiveDirection} (Reversed: {isReversed}).");
                        progress.Report(new AlignmentProgressReport { IsReversedFlowActive = isReversed });
                    }
                    else {
                        double rotationHours = _settingsManager.RotationAmount / 15.0;

                        // 1. Custom Polar Home Override Check
                        if (_settingsManager.OverrideMountHome) {
                            if (!isNearPole) {
                                string err = $"Declination ({currentPosition.Dec:F2}°) is not close to the Celestial Pole (90°). Custom Polar Home requires starting at the pole.";
                                ReportLog(progress, $"Error: {err}");
                                throw new InvalidOperationException(err);
                            }

                            double raDiff = Math.Abs(currentPosition.RA - _settingsManager.PolarHomeRA);
                            if (raDiff > 12.0) raDiff = 24.0 - raDiff;
                            
                            double decDiff = Math.Abs(currentPosition.Dec - _settingsManager.PolarHomeDec);

                            ReportLog(progress, $"[Custom Home Validation] Current RA: {currentPosition.RA:F2}h, Dec: {currentPosition.Dec:F2}° | Target RA: {_settingsManager.PolarHomeRA:F2}h, Target Dec: {_settingsManager.PolarHomeDec:F2}°");

                            if (raDiff > 0.1 || decDiff > 0.5) {
                                string err = "Telescope is not positioned at the custom Polar Home. Please slew to Polar Home first.";
                                if (_profileService?.ActiveProfile?.AstrometrySettings != null) {
                                    bool isNorthern = _profileService.ActiveProfile.AstrometrySettings.Latitude >= 0;
                                    bool targetIsNorthern = _settingsManager.PolarHomeDec >= 0;
                                    if (isNorthern != targetIsNorthern) {
                                        // Hemisphere changed — auto-relock if mount is near the correct pole
                                        if (isNearPole) {
                                            _settingsManager.PolarHomeRA = currentPosition.RA;
                                            _settingsManager.PolarHomeDec = currentPosition.Dec;
                                            ReportLog(progress, $"[Polar Home] Hemisphere change detected. Auto-relocked Custom Polar Home to RA: {currentPosition.RA:F2}h, Dec: {currentPosition.Dec:F2}°.");
                                            // Re-validate with the new values — position should now match
                                        } else {
                                            err = $"Hemisphere Mismatch: The locked Custom Polar Home is in the {(targetIsNorthern ? "Northern" : "Southern")} Hemisphere ({_settingsManager.PolarHomeDec:F2}°), but your mount is configured for the {(isNorthern ? "Northern" : "Southern")} Hemisphere. Please re-lock your Custom Polar Home for the correct hemisphere.";
                                            ReportLog(progress, $"Error: {err}");
                                            throw new InvalidOperationException(err);
                                        }
                                    } else {
                                        ReportLog(progress, $"Error: {err}");
                                        throw new InvalidOperationException(err);
                                    }
                                } else {
                                    ReportLog(progress, $"Error: {err}");
                                    throw new InvalidOperationException(err);
                                }
                            } else {
                                ReportLog(progress, "Telescope successfully validated at Custom Polar Home Position.");
                            }
                        }
                        // 2. Primary Check: If the mount explicitly reports it is at Home
                        else if (info.AtHome) {
                            ReportLog(progress, "Mount reports it is successfully at the Home Position.");
                            
                            // Enforce starting near the pole (polar alignment requirement)
                            if (!isNearPole) {
                                string err = $"Mount is at Home, but Declination ({currentPosition.Dec:F2}°) is not close to the Celestial Pole (90°).";
                                ReportLog(progress, $"Error: {err} Alignment requires starting at the pole.");
                                
                                bool physicalHomeAwayFromPole = Math.Abs(currentPosition.Dec) < 45.0;
                                
                                if (OnInterventionRequested != null) {
                                    if (physicalHomeAwayFromPole) {
                                        await OnInterventionRequested(new RescuePromptArgs {
                                            Title = "Polar Home Override Required",
                                            Message = "Your mount's native Home position is pointing away from the Celestial Pole.\n\n" +
                                                      "This plugin requires starting near the Celestial Pole. Please enable 'Override Mount Home' in settings, slew your mount to its Polar Home position (Declination near 90°), and click 'Lock Polar Home' to configure a custom starting position.",
                                            IsYesNo = false
                                        });
                                    } else {
                                        await OnInterventionRequested(new RescuePromptArgs {
                                            Title = "Mount Homing Misaligned",
                                            Message = $"Your mount reports it is at the Home position, but its Declination ({currentPosition.Dec:F2}°) is not close enough to the Celestial Pole (90°).\n\n" +
                                                      "Please ensure your mount is properly homed and aligned to its physical index marks, or slew the mount to its true Home position near the pole.",
                                            IsYesNo = false
                                        });
                                    }
                                }
                                
                                if (physicalHomeAwayFromPole) {
                                    throw new InvalidOperationException(err + " Please configure a custom Polar Home.");
                                } else {
                                    throw new InvalidOperationException(err + " Please slew the mount to its true Home position.");
                                }
                            }
                        }
                        // 3. Strict Check for homing-enabled mounts: if it can find home but is NOT at home, fail
                        else if (info.CanFindHome) {
                            string err = "Telescope is not at the Home position. Please use mount controls to FindHome first.";
                            ReportLog(progress, $"Error: {err}");
                            throw new InvalidOperationException(err);
                        }
                        // 4. Fallback Check: For mounts without physical homing sensors, fall back to dual-axis HA math
                        else {
                            double lst = info.SiderealTime;
                            double currentHA = lst - currentPosition.RA;
                            if (currentHA < 0) currentHA += 24.0;
                            if (currentHA >= 24.0) currentHA -= 24.0;

                            bool isNearMeridian = Math.Abs(currentHA) < 0.25 || Math.Abs(currentHA - 24.0) < 0.25;

                            ReportLog(progress, $"[Validation Debug] Dec: {currentPosition.Dec:F2}, RA: {currentPosition.RA:F2}, LST: {lst:F2}, HA: {currentHA:F2}");

                            if (_settingsManager.Method != NirZonshine.NINA.TwoPointPolarAlignment.RotationMethod.Manual) {
                                if (!isNearPole || !isNearMeridian) {
                                    string err = "Telescope is not aligned to the Home Index Mark. Please use mount controls to FindHome first.";
                                    ReportLog(progress, $"Error: {err}");
                                    throw new InvalidOperationException(err);
                                }
                            }
                        }
                    }
                }
            }

            if (!string.IsNullOrEmpty(_settingsManager.Filter) && _settingsManager.Filter != "(Current)") {
                FilterInfo targetFilterInfo = new FilterInfo { Name = _settingsManager.Filter, Position = 0 };
                ReportLog(progress, $"Changing filter to {_settingsManager.Filter}...");
                await _filterWheelMediator.ChangeFilter(targetFilterInfo, token, new Progress<ApplicationStatus>());
            }

            if (context.ActivePreRotate) {
                double offsetDegrees = _settingsManager.RotationAmount / 2.0;
                double offsetHours = offsetDegrees / 15.0;
                ReportStatus(progress, "Slewing (Pre-rotate)", "#FBBF24");
                ReportLog(progress, $"Pre-rotating RA by {offsetDegrees:F1}Â°...");

                if (currentPosition != null) {
                    double targetRA = currentPosition.RA + (context.ActiveDirection == RotationDirection.East ? -offsetHours : offsetHours);
                    if (targetRA < 0) targetRA += 24.0;
                    if (targetRA >= 24.0) targetRA -= 24.0;

                    Coordinates targetCoords = new Coordinates(targetRA, currentPosition.Dec, currentPosition.Epoch, Coordinates.RAType.Hours);
                    await ExecuteHardwareOperationAsync(() => _telescopeMediator.SlewToCoordinatesAsync(targetCoords, token), token, "Slew to Target");
                    try { _telescopeMediator.SetTrackingEnabled(false); } catch { }
                }
            }
        }

        private class SubframeSolveInfo {
            public PlateSolveResult Result { get; set; }
            public double Lst { get; set; }
        }

        private async Task<PlateSolveResult> ExecuteMeasurementLoopAsync(AlignmentWorkflowContext context, CancellationToken token, IProgress<AlignmentProgressReport> progress, Action<ImageSource> updateThumbnail, int measurementIndex) {
            ReportLog(progress, $"Initiating Phase {(measurementIndex == 1 ? "C" : "E")} (Measurement {measurementIndex})...");
            
            int exposuresCount = (int)_settingsManager.ExposuresPerPoint;
            var successfulSolves = new List<SubframeSolveInfo>();
            
            int binVal = 1;
            if (!string.IsNullOrEmpty(_settingsManager.Binning) && _settingsManager.Binning.Length >= 1) {
                int.TryParse(_settingsManager.Binning.Substring(0, 1), out binVal);
            }

            CaptureSequence sequence = new CaptureSequence {
                ExposureTime = _settingsManager.ExposureTime,
                ImageType = "LIGHT",
                Gain = _settingsManager.Gain,
                Binning = new BinningMode((short)binVal, (short)binVal),
                FilterType = new FilterInfo { Name = _settingsManager.Filter },
                Offset = _settingsManager.Offset,
                Enabled = true,
                TotalExposureCount = 1
            };

            var profile = _profileService.ActiveProfile;
            var plateSolveSettings = profile.GetType().GetProperty("PlateSolveSettings")?.GetValue(profile) as IPlateSolveSettings;
            if (plateSolveSettings == null && !context.IsSimulation) throw new InvalidOperationException("Could not retrieve active plate solve settings.");

            IPlateSolver solver = context.IsSimulation ? null : _plateSolverFactory.GetPlateSolver(plateSolveSettings);
            ICaptureSolver captureSolver = context.IsSimulation ? null : _plateSolverFactory.GetCaptureSolver(solver, null, _imagingMediator, _filterWheelMediator);

            for (int subframeIndex = 1; subframeIndex <= exposuresCount; subframeIndex++) {
                token.ThrowIfCancellationRequested();
                if (exposuresCount > 1) {
                    ReportStatus(progress, $"Solving Sub-frame {subframeIndex}/{exposuresCount}...", "#6366F1");
                    ReportLog(progress, $"Capturing sub-frame {subframeIndex} of {exposuresCount} at Station {measurementIndex}...");
                } else {
                    ReportStatus(progress, $"Solving Point {measurementIndex}...", "#6366F1");
                }

                bool subframeSuccess = false;
                for (int attempt = 1; attempt <= _settingsManager.PlateSolveRetries; attempt++) {
                    token.ThrowIfCancellationRequested();
                    if (context.IsSimulation) {
                        await Task.Delay(2000, token);
                        var simPos = _telescopeMediator.GetCurrentPosition() ?? new Coordinates(12.0, 45.0, Epoch.JNOW, Coordinates.RAType.Hours);
                        
                        double injectedRA = simPos.RA + context.ManualSimBiasRA;
                        if (injectedRA < 0) injectedRA += 24.0; if (injectedRA >= 24.0) injectedRA -= 24.0;
                        double injectedDec = Math.Clamp(simPos.Dec + context.ManualSimBiasDec, -89.5, 89.5);
                        
                        if (_settingsManager.EnableOnePointAlignment && !context.HasRoughFinderSimTriggered) {
                            injectedDec = (injectedDec >= 0) ? 78.0 : -78.0;
                            context.HasRoughFinderSimTriggered = true;
                            ReportLog(progress, "[Simulator Injection] Rough Finder enabled. Forcing solved coordinate to 12° from pole to trigger rescue intercept.");
                        }
                        
                        if (measurementIndex == 2 && _settingsManager.Method == RotationMethod.Manual) {
                            double baseRA = context.Coordinates1?.RA ?? simPos.RA;
                            double baseDec = context.Coordinates1?.Dec ?? simPos.Dec;
                            double offHrs = context.CurrentSimulationOffset / 15.0;
                            injectedRA = baseRA + (context.ActiveDirection == RotationDirection.East ? offHrs : -offHrs);
                            if (injectedRA < 0) injectedRA += 24.0; if (injectedRA >= 24.0) injectedRA -= 24.0;
                            injectedDec = baseDec;
                        }

                        // Simulate atmospheric seeing/jitter on coordinates for double/triple mode (sub-arcminute jitter)
                        if (exposuresCount > 1 && subframeIndex > 1) {
                            var rand = new Random();
                            // Inject up to 20 arcsec of random drift/jitter
                            double raJitter = (rand.NextDouble() - 0.5) * 20.0 / 3600.0 / 15.0;
                            double decJitter = (rand.NextDouble() - 0.5) * 20.0 / 3600.0;
                            injectedRA += raJitter;
                            injectedDec += decJitter;
                            if (injectedRA < 0) injectedRA += 24.0; if (injectedRA >= 24.0) injectedRA -= 24.0;
                            injectedDec = Math.Clamp(injectedDec, -89.5, 89.5);
                        }

                        var simRes = new PlateSolveResult {
                            Success = true,
                            Coordinates = new Coordinates(injectedRA, injectedDec, simPos.Epoch, Coordinates.RAType.Hours),
                            PositionAngle = measurementIndex == 1 ? 45.0 : context.Angle1
                        };
                        successfulSolves.Add(new SubframeSolveInfo {
                            Result = simRes,
                            Lst = _telescopeMediator.GetInfo()?.SiderealTime ?? simRes.Coordinates.RA
                        });
                        subframeSuccess = true;
                        break;
                    } else {
                        var currentPosition = (_telescopeMediator.GetInfo()?.Connected ?? false) ? _telescopeMediator.GetCurrentPosition() : null;
                        CaptureSolverParameter solverParam = new CaptureSolverParameter {
                            Attempts = 1,
                            ReattemptDelay = TimeSpan.FromSeconds(2),
                            FocalLength = profile.TelescopeSettings.FocalLength,
                            PixelSize = _cameraMediator.GetInfo()?.PixelSize ?? 0,
                            Binning = binVal,
                            SearchRadius = 15.0,
                            Regions = 5000.0,
                            MaxObjects = 500,
                            Coordinates = measurementIndex == 1 ? currentPosition : context.Coordinates1,
                            BlindFailoverEnabled = true,
                            DisableNotifications = true
                        };

                        var solveProgress = new Progress<PlateSolveProgress>(p => {
                            if (p.Thumbnail != null) updateThumbnail?.Invoke(p.Thumbnail);
                        });
                        var appStatusProg = new Progress<ApplicationStatus>(s => {
                            if (s.Status != null) {
                                bool isBlind = s.Status.Contains("Astrometry", StringComparison.OrdinalIgnoreCase) ||
                                               s.Status.Contains("All Sky", StringComparison.OrdinalIgnoreCase) ||
                                               s.Status.Contains("AllSky", StringComparison.OrdinalIgnoreCase) ||
                                               s.Status.Contains("Blind", StringComparison.OrdinalIgnoreCase);
                                progress.Report(new AlignmentProgressReport { IsBlindSolvingActive = isBlind });
                            }
                        });
                        try {
                            var res = await ExecuteHardwareOperationAsync(() => captureSolver.Solve(sequence, solverParam, solveProgress, appStatusProg, token), token, "Solve Capture");
                            if (res != null && res.Success) {
                                if (res.Coordinates.Epoch == Epoch.J2000) {
                                    ReportLog(progress, "[Precession] Precessing solved coordinates from J2000 to JNOW...");
                                    res = new PlateSolveResult {
                                        Success = res.Success,
                                        Coordinates = res.Coordinates.Transform(Epoch.JNOW),
                                        PositionAngle = res.PositionAngle
                                    };
                                }
                                successfulSolves.Add(new SubframeSolveInfo {
                                    Result = res,
                                    Lst = _telescopeMediator.GetInfo()?.SiderealTime ?? res.Coordinates.RA
                                });
                                subframeSuccess = true;
                                break;
                            }
                        } catch (Exception ex) {
                            ReportLog(progress, $"Internal solve error: {ex.Message}");
                        } finally {
                            progress.Report(new AlignmentProgressReport { IsBlindSolvingActive = false });
                        }
                    }
                    ReportLog(progress, $"Attempt {attempt} for sub-frame {subframeIndex} failed.");
                    await Task.Delay(1000, token);
                }

                if (!subframeSuccess) {
                    ReportLog(progress, $"Warning: Sub-frame {subframeIndex}/{exposuresCount} failed to plate-solve.");
                }
            }

            if (successfulSolves.Count == 0) {
                throw new InvalidOperationException($"Plate solve failed for all sub-frames at measurement point {measurementIndex}.");
            }

            if (successfulSolves.Count == 1) {
                var singleRes = successfulSolves[0].Result;
                if (exposuresCount > 1) {
                    ReportLog(progress, $"[Multi-Frame Sampling] Only 1 successful sub-frame obtained. Falling back to single frame coordinate (Dec: {singleRes.Coordinates.Dec:F4}°, RA: {singleRes.Coordinates.RA:F4}h).");
                }
                return singleRes;
            }

            // Sub-frame LST Drift Normalization to final anchor LST
            double lstAnchor = _telescopeMediator.GetInfo()?.SiderealTime ?? successfulSolves[0].Lst;
            
            var normalizedResults = new List<PlateSolveResult>();
            foreach (var solve in successfulSolves) {
                double deltaLst = lstAnchor - solve.Lst;
                if (deltaLst > 12.0) deltaLst -= 24.0;
                if (deltaLst < -12.0) deltaLst += 24.0;

                double correctedRa = solve.Result.Coordinates.RA + deltaLst;
                if (correctedRa < 0) correctedRa += 24.0;
                if (correctedRa >= 24.0) correctedRa -= 24.0;

                normalizedResults.Add(new PlateSolveResult {
                    Success = true,
                    Coordinates = new Coordinates(correctedRa, solve.Result.Coordinates.Dec, solve.Result.Coordinates.Epoch, Coordinates.RAType.Hours),
                    PositionAngle = solve.Result.PositionAngle
                });
            }

            if (normalizedResults.Count == 2) {
                var r1 = normalizedResults[0];
                var r2 = normalizedResults[1];
                
                double avgDec = (r1.Coordinates.Dec + r2.Coordinates.Dec) / 2.0;
                
                double avgRa = (r1.Coordinates.RA + r2.Coordinates.RA) / 2.0;
                double raDiff = r1.Coordinates.RA - r2.Coordinates.RA;
                if (raDiff > 12.0) avgRa = (r1.Coordinates.RA + r2.Coordinates.RA + 24.0) / 2.0;
                else if (raDiff < -12.0) avgRa = (r1.Coordinates.RA + r2.Coordinates.RA - 24.0) / 2.0;
                if (avgRa >= 24.0) avgRa -= 24.0;
                if (avgRa < 0.0) avgRa += 24.0;
                
                double avgPa = (r1.PositionAngle + r2.PositionAngle) / 2.0;
                double paDiff = r1.PositionAngle - r2.PositionAngle;
                if (paDiff > 180.0) avgPa = (r1.PositionAngle + r2.PositionAngle + 360.0) / 2.0;
                else if (paDiff < -180.0) avgPa = (r1.PositionAngle + r2.PositionAngle - 360.0) / 2.0;
                if (avgPa >= 360.0) avgPa -= 360.0;
                if (avgPa < 0) avgPa += 360.0;

                ReportLog(progress, $"[Multi-Frame Sampling] Double Averaging Success (LST anchor: {lstAnchor:F4}h). Mean coordinate: Dec {avgDec:F4}°, RA {avgRa:F4}h.");
                return new PlateSolveResult {
                    Success = true,
                    Coordinates = new Coordinates(avgRa, avgDec, r1.Coordinates.Epoch, Coordinates.RAType.Hours),
                    PositionAngle = avgPa
                };
            }

            // Triple Mode Outlier Rejection
            var r_1 = normalizedResults[0];
            var r_2 = normalizedResults[1];
            var r_3 = normalizedResults[2];

            Vector3D v1 = Vector3D.FromEquatorial(r_1.Coordinates);
            Vector3D v2 = Vector3D.FromEquatorial(r_2.Coordinates);
            Vector3D v3 = Vector3D.FromEquatorial(r_3.Coordinates);

            double d12 = Math.Acos(Math.Clamp(Vector3D.Dot(v1, v2), -1.0, 1.0));
            double d23 = Math.Acos(Math.Clamp(Vector3D.Dot(v2, v3), -1.0, 1.0));
            double d13 = Math.Acos(Math.Clamp(Vector3D.Dot(v1, v3), -1.0, 1.0));

            PlateSolveResult keepA, keepB;
            string outlierLabel;

            if (d12 <= d23 && d12 <= d13) {
                keepA = r_1;
                keepB = r_2;
                outlierLabel = "Sub-frame 3";
            } else if (d23 <= d12 && d23 <= d13) {
                keepA = r_2;
                keepB = r_3;
                outlierLabel = "Sub-frame 1";
            } else {
                keepA = r_1;
                keepB = r_3;
                outlierLabel = "Sub-frame 2";
            }

            double avgDecFinal = (keepA.Coordinates.Dec + keepB.Coordinates.Dec) / 2.0;
            
            double avgRaFinal = (keepA.Coordinates.RA + keepB.Coordinates.RA) / 2.0;
            double raDiffFinal = keepA.Coordinates.RA - keepB.Coordinates.RA;
            if (raDiffFinal > 12.0) avgRaFinal = (keepA.Coordinates.RA + keepB.Coordinates.RA + 24.0) / 2.0;
            else if (raDiffFinal < -12.0) avgRaFinal = (keepA.Coordinates.RA + keepB.Coordinates.RA - 24.0) / 2.0;
            if (avgRaFinal >= 24.0) avgRaFinal -= 24.0;
            if (avgRaFinal < 0.0) avgRaFinal += 24.0;
            
            double avgPaFinal = (keepA.PositionAngle + keepB.PositionAngle) / 2.0;
            double paDiffFinal = keepA.PositionAngle - keepB.PositionAngle;
            if (paDiffFinal > 180.0) avgPaFinal = (keepA.PositionAngle + keepB.PositionAngle + 360.0) / 2.0;
            else if (paDiffFinal < -180.0) avgPaFinal = (keepA.PositionAngle + keepB.PositionAngle - 360.0) / 2.0;
            if (avgPaFinal >= 360.0) avgPaFinal -= 360.0;
            if (avgPaFinal < 0) avgPaFinal += 360.0;

            // Log details of outlier rejection
            double d12Arcsec = d12 * 180.0 / Math.PI * 3600.0;
            double d23Arcsec = d23 * 180.0 / Math.PI * 3600.0;
            double d13Arcsec = d13 * 180.0 / Math.PI * 3600.0;
            ReportLog(progress, $"[Multi-Frame Sampling] Triple Outlier Rejection Success. Separations: d12={d12Arcsec:F1}\", d23={d23Arcsec:F1}\", d13={d13Arcsec:F1}\". Discarded outlier: {outlierLabel}. LST anchor: {lstAnchor:F4}h. Mean coordinate: Dec {avgDecFinal:F4}°, RA {avgRaFinal:F4}h.");

            return new PlateSolveResult {
                Success = true,
                Coordinates = new Coordinates(avgRaFinal, avgDecFinal, keepA.Coordinates.Epoch, Coordinates.RAType.Hours),
                PositionAngle = avgPaFinal
            };
        }

        private async Task ExecuteRotationAsync(AlignmentWorkflowContext context, CancellationToken token, IProgress<AlignmentProgressReport> progress, Action<ImageSource> updateThumbnail) {
            ReportLog(progress, "Initiating Phase D (Rotation)...");
            if (_settingsManager.Method == RotationMethod.Manual) {
                if (OnManualRotationRequested != null) {
                    var currentPosition = (_telescopeMediator.GetInfo()?.Connected ?? false) ? _telescopeMediator.GetCurrentPosition() : null;
                    var profile = _profileService.ActiveProfile;
                    var ps = profile.GetType().GetProperty("PlateSolveSettings")?.GetValue(profile) as IPlateSolveSettings;
                    IPlateSolver solver = context.IsSimulation ? null : _plateSolverFactory.GetPlateSolver(ps);
                    ICaptureSolver capSolver = context.IsSimulation ? null : _plateSolverFactory.GetCaptureSolver(solver, null, _imagingMediator, _filterWheelMediator);
                    
                    int binVal = 1;
                    if (!string.IsNullOrEmpty(_settingsManager.Binning) && _settingsManager.Binning.Length >= 1) int.TryParse(_settingsManager.Binning.Substring(0, 1), out binVal);
                    CaptureSequence seq = new CaptureSequence {
                        ExposureTime = _settingsManager.ExposureTime, ImageType = "LIGHT", Gain = _settingsManager.Gain,
                        Binning = new BinningMode((short)binVal, (short)binVal), FilterType = new FilterInfo { Name = _settingsManager.Filter },
                        Offset = _settingsManager.Offset, Enabled = true, TotalExposureCount = 1
                    };

                    await OnManualRotationRequested(context, _settingsManager.RotationAmount, context.ActiveDirection, context.Coordinates1, seq, capSolver, context.IsSimulation);
                    context.CurrentSimulationOffset = _settingsManager.RotationAmount; // Simulate manual move
                }
            } else {
                ReportStatus(progress, "Slewing", "#FBBF24");
                var basePos = (_telescopeMediator.GetInfo()?.Connected ?? false) ? _telescopeMediator.GetCurrentPosition() : context.Coordinates1;
                double rotOffsetHours = _settingsManager.RotationAmount / 15.0;
                double targetRA = basePos.RA + (context.ActiveDirection == RotationDirection.East ? rotOffsetHours : -rotOffsetHours);
                if (targetRA < 0) targetRA += 24.0; if (targetRA >= 24.0) targetRA -= 24.0;
                Coordinates targetCoords = new Coordinates(targetRA, basePos.Dec, basePos.Epoch, Coordinates.RAType.Hours);
                
                await ExecuteHardwareOperationAsync(() => _telescopeMediator.SlewToCoordinatesAsync(targetCoords, token), token, "Slew to Rotated Target");
                try { _telescopeMediator.SetTrackingEnabled(false); } catch { }
            }
            await Task.Delay(1000, token);
        }

        private async Task ExecuteCalculationAndLiveAdjustmentAsync(AlignmentWorkflowContext context, CancellationToken token, IProgress<AlignmentProgressReport> progress, Action<ImageSource> updateThumbnail) {
            ReportLog(progress, "Initiating Phase F (Calculation & Live Adjustment)...");
            ReportStatus(progress, "Calculating...", "#6366F1");

            double latitude = GetLatitude();
            
            // 1. Calculate Time-Drift and delta LST
            double deltaLst = context.LstMeasurement2 - context.LstMeasurement1;
            if (deltaLst > 12.0) deltaLst -= 24.0;
            if (deltaLst < -12.0) deltaLst += 24.0;
            double driftSeconds = deltaLst * 3600.0;
            double skyDriftArcmin = Math.Abs(driftSeconds) * 15.0 / 60.0;

            // 2. Perform Time-Drift Correction
            double correctedRa1 = context.Coordinates1.RA + deltaLst;
            if (correctedRa1 < 0) correctedRa1 += 24.0;
            if (correctedRa1 >= 24.0) correctedRa1 -= 24.0;

            Coordinates correctedC1 = new Coordinates(correctedRa1, context.Coordinates1.Dec, context.Coordinates1.Epoch, Coordinates.RAType.Hours);
            ReportLog(progress, $"[LST Correction] Normalized Point 1 RA by {driftSeconds:F1}s ({skyDriftArcmin:F2} arcmin celestial drift) to match LST at Point 2.");

            // 3. Calibrate using normalized Point 1 coordinate
            var calibration = _polarSolver.Calibrate(correctedC1, context.Angle1, context.Coordinates2, context.Angle2, context.LstMeasurement2, latitude);
            
            ReportAlignmentProgress(progress, calibration.InitialPolarAxis, context.Coordinates2.RA, latitude, context.LstMeasurement2);
            progress.Report(new AlignmentProgressReport { HasSuccessfulAlignmentReached = true });

            int binVal = 1;
            if (!string.IsNullOrEmpty(_settingsManager.Binning) && _settingsManager.Binning.Length >= 1) int.TryParse(_settingsManager.Binning.Substring(0, 1), out binVal);
            CaptureSequence seq = new CaptureSequence {
                ExposureTime = _settingsManager.ExposureTime, ImageType = "LIGHT", Gain = _settingsManager.Gain,
                Binning = new BinningMode((short)binVal, (short)binVal), FilterType = new FilterInfo { Name = _settingsManager.Filter },
                Offset = _settingsManager.Offset, Enabled = true, TotalExposureCount = 1
            };
            
            var profile = _profileService.ActiveProfile;
            var plateSolveSettings = profile.GetType().GetProperty("PlateSolveSettings")?.GetValue(profile) as IPlateSolveSettings;
            IPlateSolver solver = context.IsSimulation ? null : _plateSolverFactory.GetPlateSolver(plateSolveSettings);
            ICaptureSolver captureSolver = context.IsSimulation ? null : _plateSolverFactory.GetCaptureSolver(solver, null, _imagingMediator, _filterWheelMediator);

            try {
                while (!token.IsCancellationRequested) {
                    if (context.IsRunningFromSequence && context.SequenceResumeTcs != null && context.SequenceResumeTcs.Task.IsCompleted) {
                        ReportLog(progress, "[Sequence] Resume Sequence requested. Exiting Phase F live adjustment loop successfully.");
                        break;
                    }

                    using (var cts = CancellationTokenSource.CreateLinkedTokenSource(token)) {
                        if (context.IsRunningFromSequence && context.SequenceResumeTcs != null) {
                            var tcs = context.SequenceResumeTcs;
                            // Trigger cancellation of the current loop step when tcs completes
                            _ = tcs.Task.ContinueWith(_ => {
                                try { cts.Cancel(); } catch { }
                            });
                        }

                        try {
                            if (cts.Token.IsCancellationRequested) {
                                if (context.IsRunningFromSequence && context.SequenceResumeTcs != null && context.SequenceResumeTcs.Task.IsCompleted) {
                                    ReportLog(progress, "[Sequence] Resume Sequence requested. Exiting Phase F live adjustment loop successfully.");
                                    break;
                                }
                                break;
                            }

                            if (context.IsSimulation) {
                                await Task.Delay(2000, cts.Token);
                                var simPos = _telescopeMediator.GetCurrentPosition() ?? new Coordinates(12.0, 45.0, Epoch.JNOW, Coordinates.RAType.Hours);
                                double baseRA = context.Coordinates2?.RA ?? simPos.RA;
                                double baseDec = context.Coordinates2?.Dec ?? simPos.Dec;
                                var c2 = new Coordinates(baseRA + (new Random().NextDouble() - 0.5) * 0.015, baseDec + (new Random().NextDouble() - 0.5) * 0.015, Epoch.JNOW, Coordinates.RAType.Hours);
                                
                                var err = _polarSolver.EvaluateLiveError(c2, context.LstMeasurement2, calibration, latitude);
                                ReportAlignmentProgress(progress, err.CalculatedPolarAxis, c2.RA, latitude, context.LstMeasurement2);
                            } else {
                                var liveHintCoords = (_telescopeMediator.GetInfo()?.Connected ?? false) ? _telescopeMediator.GetCurrentPosition() : context.Coordinates2;
                                CaptureSolverParameter solverParam = new CaptureSolverParameter {
                                    Attempts = 1, ReattemptDelay = TimeSpan.FromSeconds(2), FocalLength = profile.TelescopeSettings.FocalLength,
                                    PixelSize = _cameraMediator.GetInfo()?.PixelSize ?? 0, Binning = binVal, SearchRadius = 15.0, Regions = 5000.0,
                                    MaxObjects = 500, Coordinates = liveHintCoords, BlindFailoverEnabled = (_settingsManager.Method == RotationMethod.Manual),
                                    DisableNotifications = true
                                };

                                var solveProgress = new Progress<PlateSolveProgress>(p => { if (p.Thumbnail != null) updateThumbnail?.Invoke(p.Thumbnail); });
                                try {
                                    var liveResult = await ExecuteHardwareOperationAsync(() => captureSolver.Solve(seq, solverParam, solveProgress, new Progress<ApplicationStatus>(), cts.Token), cts.Token, "Live Solve Capture");
                                    if (liveResult != null && liveResult.Success) {
                                        if (liveResult.Coordinates.Epoch == Epoch.J2000) {
                                            liveResult = new PlateSolveResult {
                                                Success = liveResult.Success,
                                                Coordinates = liveResult.Coordinates.Transform(Epoch.JNOW),
                                                PositionAngle = liveResult.PositionAngle
                                            };
                                        }
                                        ReportStatus(progress, "Solved", "#22C55E");
                                        double lstLive = _telescopeMediator.GetInfo()?.SiderealTime ?? liveResult.Coordinates.RA;
                                        var err = _polarSolver.EvaluateLiveError(liveResult.Coordinates, lstLive, calibration, latitude);
                                        ReportAlignmentProgress(progress, err.CalculatedPolarAxis, liveResult.Coordinates.RA, latitude, lstLive);
                                    } else {
                                        ReportStatus(progress, "Could not solve", "#EF4444");
                                    }
                                } catch (Exception ex) when (!(ex is OperationCanceledException)) { }
                            }
                        } catch (OperationCanceledException) {
                            if (context.IsRunningFromSequence && context.SequenceResumeTcs != null && context.SequenceResumeTcs.Task.IsCompleted) {
                                ReportLog(progress, "[Sequence] Resume Sequence requested. Exiting Phase F live adjustment loop successfully.");
                                break;
                            }
                            throw;
                        }
                    }
                    await Task.Delay(100, token);
                }
            } catch (OperationCanceledException) {
                if (context.IsRunningFromSequence && context.SequenceResumeTcs != null && context.SequenceResumeTcs.Task.IsCompleted) {
                    ReportLog(progress, "[Sequence] Resume Sequence requested. Exiting Phase F live adjustment loop successfully.");
                } else {
                    throw;
                }
            }
        }

        private void ReportAlignmentProgress(IProgress<AlignmentProgressReport> progress, Vector3D calculatedPolarAxis, double referenceRA, double latitude, double lst) {
            var error = _polarSolver.CalculateErrorFromAxis(calculatedPolarAxis, referenceRA, lst, latitude);
            double altErr = error.AltitudeErrorArcmin;
            double azErr = error.AzimuthErrorArcmin;
            double total = Math.Sqrt(altErr * altErr + azErr * azErr);

            var report = new AlignmentProgressReport {
                AltitudeError = FormatError(altErr),
                AzimuthError = FormatError(azErr),
                TotalErrorValue = total,
                TotalError = FormatTotalError(total),
                IsAltitudePriority = total >= 0.5 && Math.Abs(altErr) > Math.Abs(azErr) + 0.1,
                IsAzimuthPriority = total >= 0.5 && Math.Abs(azErr) > Math.Abs(altErr) + 0.1
            };

            global::NINA.Core.Utility.Logger.Info($"[2-Point Polar Alignment] Calculated Alignment Errors: Alt {report.AltitudeError}, Az {report.AzimuthError} (Alt: {altErr:F1}', Az: {azErr:F1}'), Total: {report.TotalError} ({total:F1}')");

            if (_settingsManager.AzKnobDirection == AzimuthKnobDirection.LeftRightArrow) {
                if (azErr > 0) report.AzimuthInstruction = "← Move Left";
                else if (azErr < 0) report.AzimuthInstruction = "Move Right →";
                else report.AzimuthInstruction = "Aligned";
            } else if (_settingsManager.AzKnobDirection == AzimuthKnobDirection.Clockwise) {
                if (azErr > 0) report.AzimuthInstruction = "Move Left ↺";
                else if (azErr < 0) report.AzimuthInstruction = "Move Right ↻";
                else report.AzimuthInstruction = "Aligned";
            } else { // AntiClockwise
                if (azErr > 0) report.AzimuthInstruction = "Move Left ↻";
                else if (azErr < 0) report.AzimuthInstruction = "Move Right ↺";
                else report.AzimuthInstruction = "Aligned";
            }

            if (_settingsManager.AltKnobDirection == AltitudeKnobDirection.UpArrow) {
                if (altErr > 0) report.AltitudeInstruction = "Move Down ↓";
                else if (altErr < 0) report.AltitudeInstruction = "Move Up ↑";
                else report.AltitudeInstruction = "Aligned";
            } else if (_settingsManager.AltKnobDirection == AltitudeKnobDirection.Clockwise) {
                if (altErr > 0) report.AltitudeInstruction = "Move Down ↺";
                else if (altErr < 0) report.AltitudeInstruction = "Move Up ↻";
                else report.AltitudeInstruction = "Aligned";
            } else { // AntiClockwise
                if (altErr > 0) report.AltitudeInstruction = "Move Down ↻";
                else if (altErr < 0) report.AltitudeInstruction = "Move Up ↺";
                else report.AltitudeInstruction = "Aligned";
            }

            if (total <= 1.0) { report.TotalErrorRating = "✨ Excellent Alignment"; report.TotalErrorRatingColorHex = "#00FF7F"; }
            else if (total <= 3.0) { report.TotalErrorRating = "🟢 Good Alignment"; report.TotalErrorRatingColorHex = "#98FB98"; }
            else if (total <= 10.0) { report.TotalErrorRating = "🟡 Fair Alignment"; report.TotalErrorRatingColorHex = "#FFD700"; }
            else { report.TotalErrorRating = "🔴 Poor Alignment"; report.TotalErrorRatingColorHex = "#FF4D4D"; }

            progress.Report(report);
        }

        private double GetLatitude() {
            try {
                var info = _telescopeMediator?.GetInfo();
                if (info != null) {
                    var latProp = info.GetType().GetProperty("Latitude") ?? info.GetType().GetProperty("SiteLatitude");
                    if (latProp != null) return Convert.ToDouble(latProp.GetValue(info));
                }
            } catch (Exception ex) {
                global::NINA.Core.Utility.Logger.Warning($"[2-Point Polar Alignment] GetLatitude reflection failed: {ex.Message}");
            }
            // F-4 Fix: Log fallback so it's not silently swallowed
            global::NINA.Core.Utility.Logger.Warning("[2-Point Polar Alignment] Could not retrieve site latitude from mount. Using fallback latitude of 45.0°. Solver results may be inaccurate if actual latitude differs significantly.");
            return 45.0;
        }

        private string FormatTotalError(double errorArcmin) {
            double absMin = Math.Abs(errorArcmin);
            int totalMinutes = (int)Math.Truncate(absMin);
            int seconds = (int)Math.Round((absMin - totalMinutes) * 60.0);
            if (seconds >= 60) { totalMinutes += 1; seconds -= 60; }
            int degrees = totalMinutes / 60;
            int minutes = totalMinutes % 60;
            return degrees > 0 ? $"{degrees:D2}° {minutes:D2}' {seconds:D2}\"" : $"{minutes:D2}' {seconds:D2}\"";
        }

        private string FormatError(double errorArcmin) {
            double absMin = Math.Abs(errorArcmin);
            int totalMinutes = (int)Math.Truncate(absMin);
            int seconds = (int)Math.Round((absMin - totalMinutes) * 60.0);
            if (seconds >= 60) { totalMinutes += 1; seconds -= 60; }
            int degrees = totalMinutes / 60;
            int minutes = totalMinutes % 60;
            string sign = errorArcmin < 0 ? "-" : "+";
            return degrees > 0 ? $"{sign}{degrees:D2}° {minutes:D2}' {seconds:D2}\"" : $"{sign}{minutes:D2}' {seconds:D2}\"";
        }

        public async Task ExecuteManualTrackingAsync(AlignmentWorkflowContext context, double targetDegrees, RotationDirection direction, Coordinates initialCoords, CaptureSequence sequence, ICaptureSolver captureSolver, IProgress<ManualTrackingProgress> progress, CancellationToken trackingToken) {
            int frameCounter = 0;
            var solveProgress = new Progress<PlateSolveProgress>(p => {
                if (p.Thumbnail != null) {
                    progress.Report(new ManualTrackingProgress { Thumbnail = p.Thumbnail });
                }
            });
            var appProgress = new Progress<ApplicationStatus>();

            int binVal = 1;
            if (!string.IsNullOrEmpty(_settingsManager.Binning) && _settingsManager.Binning.Length >= 1) {
                int.TryParse(_settingsManager.Binning.Substring(0, 1), out binVal);
            }

            var profile = _profileService.ActiveProfile;
            CaptureSolverParameter solverParam = new CaptureSolverParameter {
                Attempts = 1, ReattemptDelay = TimeSpan.FromSeconds(1),
                FocalLength = profile.TelescopeSettings.FocalLength,
                PixelSize = _cameraMediator.GetInfo()?.PixelSize ?? 0,
                Binning = binVal, Coordinates = initialCoords, BlindFailoverEnabled = true,
                DisableNotifications = true, SearchRadius = 15.0, Regions = 5000.0, MaxObjects = 500
            };

            await Task.Delay(1000, trackingToken);

            while (!trackingToken.IsCancellationRequested) {
                try {
                    frameCounter++;
                    progress.Report(new ManualTrackingProgress { StatusText = $"Capturing tracking image #{frameCounter}..." });

                    PlateSolveResult solveResult = null;

                    if (context.IsSimulation) {
                        await Task.Delay(1500, trackingToken);
                        double offHrs = context.CurrentSimulationOffset / 15.0;
                        double currentSimRA = initialCoords.RA + (direction == RotationDirection.East ? offHrs : -offHrs);
                        if (currentSimRA < 0) currentSimRA += 24.0;
                        if (currentSimRA >= 24.0) currentSimRA -= 24.0;

                        solveResult = new PlateSolveResult {
                            Success = true,
                            Coordinates = new Coordinates(currentSimRA, initialCoords.Dec, initialCoords.Epoch, Coordinates.RAType.Hours)
                        };
                    } else {
                        solveResult = await ExecuteHardwareOperationAsync(() => captureSolver.Solve(sequence, solverParam, solveProgress, appProgress, trackingToken), trackingToken, "Solve Capture");
                    }

                    if (solveResult != null && solveResult.Success && !trackingToken.IsCancellationRequested) {
                        var liveCoords = solveResult.Coordinates;
                        double lat1 = initialCoords.Dec * Math.PI / 180.0;
                        double lat2 = liveCoords.Dec * Math.PI / 180.0;
                        double dLon = (liveCoords.RA - initialCoords.RA) * 15.0 * Math.PI / 180.0;

                        double cosDistance = Math.Sin(lat1) * Math.Sin(lat2) + Math.Cos(lat1) * Math.Cos(lat2) * Math.Cos(dLon);
                        cosDistance = Math.Clamp(cosDistance, -1.0, 1.0);
                        double distRadians = Math.Acos(cosDistance);

                        double cosDec = Math.Cos(initialCoords.Dec * Math.PI / 180.0);
                        double rotDeltaDegrees = 0.0;

                        if (Math.Abs(cosDec) > 0.01) {
                            double sinRotHalf = Math.Sin(distRadians / 2.0) / cosDec;
                            sinRotHalf = Math.Clamp(sinRotHalf, -1.0, 1.0);
                            rotDeltaDegrees = 2.0 * Math.Asin(sinRotHalf) * 180.0 / Math.PI;
                        } else {
                            double simpleDiff = Math.Abs(liveCoords.RA - initialCoords.RA) * 15.0;
                            if (simpleDiff > 180.0) simpleDiff = 360.0 - simpleDiff;
                            rotDeltaDegrees = simpleDiff;
                        }

                        double diffToTarget = Math.Abs(rotDeltaDegrees - targetDegrees);
                        bool isLocked = diffToTarget < 1.0;

                        progress.Report(new ManualTrackingProgress {
                            CurrentDegrees = rotDeltaDegrees,
                            TargetDegrees = targetDegrees,
                            StatusText = isLocked ? "✨ Target angle reached! Ready to lock." : "Tracking lock active. Last solve successful.",
                            IsLocked = isLocked
                        });
                    } else {
                        progress.Report(new ManualTrackingProgress { StatusText = "Tracking update skipped: Plate solve failed. Continuing..." });
                    }

                    await Task.Delay(800, trackingToken);
                } catch (OperationCanceledException) {
                    break;
                } catch (Exception) {
                    progress.Report(new ManualTrackingProgress { StatusText = "Loop warning: Attempting reconnect..." });
                    await Task.Delay(1500, trackingToken);
                }
            }
        }

        private async Task<bool> AttemptRescueIfNeeded(string failureReason, CancellationToken token, IProgress<AlignmentProgressReport> progress, AlignmentWorkflowContext context) {
            if (!_settingsManager.EnableOnePointAlignment) return false;
            ReportLog(progress, $"[Intercept] Polar Alignment condition fail: {failureReason}");
            
            bool agree = false;
            if (OnInterventionRequested != null) {
                agree = await OnInterventionRequested(new RescuePromptArgs {
                    Title = "Launch Rough Finder Rescue?",
                    Message = $"Standard 2-Point alignment requirements not met: {failureReason}\n\n" +
                              "Would you like to initialize the interactive Rough Finder Rescue Mode?\n\n" +
                              "The application will perform a recurring tracking loop to guide you within 5 degrees of the celestial pole.",
                    IsYesNo = true
                });
            }
            
            if (agree) {
                await RunRoughFinderEngineAsync(context, token, progress);
                return true;
            }
            return false;
        }

        private async Task RunRoughFinderEngineAsync(AlignmentWorkflowContext context, CancellationToken rescueToken, IProgress<AlignmentProgressReport> progress) {
            progress.Report(new AlignmentProgressReport { IsReversedFlowActive = true });
            ReportStatus(progress, "Rescue Engine Tracking", "#6366F1");
            ReportLog(progress, "Initializing Rough Finder Rescue Engine Loop...");
            Notification.ShowSuccess("Rough Finder Rescue Engaged. Watch live telemetry to reach center target zone.");
            
            int binVal = 1;
            if (!string.IsNullOrEmpty(_settingsManager.Binning) && _settingsManager.Binning.Length >= 1) {
                int.TryParse(_settingsManager.Binning.Substring(0, 1), out binVal);
            }

            CaptureSequence seq = new CaptureSequence {
                ExposureTime = _settingsManager.ExposureTime,
                ImageType = "LIGHT",
                Gain = _settingsManager.Gain,
                Binning = new BinningMode((short)binVal, (short)binVal),
                FilterType = new FilterInfo { Name = _settingsManager.Filter },
                Offset = _settingsManager.Offset,
                Enabled = true,
                TotalExposureCount = 1
            };
            
            var profile = _profileService.ActiveProfile;
            var ps = profile.GetType().GetProperty("PlateSolveSettings")?.GetValue(profile) as IPlateSolveSettings;
            IPlateSolver solver = context.IsSimulation ? null : _plateSolverFactory.GetPlateSolver(ps);
            IPlateSolver blindSolver = context.IsSimulation ? null : _plateSolverFactory.GetBlindSolver(ps);
            ICaptureSolver captureSolver = context.IsSimulation ? null : _plateSolverFactory.GetCaptureSolver(solver, blindSolver, _imagingMediator, _filterWheelMediator);

            int simStep = 0;
            while (!rescueToken.IsCancellationRequested) {
                ReportLog(progress, "Rescue Loop: Capturing wide-net frame...");
                PlateSolveResult res = null;
                
                if (context.IsSimulation) {
                    await Task.Delay(1500, rescueToken);
                    simStep++;
                    double lat = GetLatitude();
                    bool isN = lat >= 0;
                    double sDec = isN ? Math.Min(89.95, 78.0 + simStep * 2.5) : Math.Max(-89.95, -78.0 - simStep * 2.5); // Simulates converging to the active pole
                    var p = _telescopeMediator.GetCurrentPosition() ?? new Coordinates(12.0, sDec, Epoch.JNOW, Coordinates.RAType.Hours);
                    res = new PlateSolveResult {
                        Success = true,
                        Coordinates = new Coordinates(p.RA, sDec, Epoch.JNOW, Coordinates.RAType.Hours)
                    };
                } else {
                    CaptureSolverParameter solverParam = new CaptureSolverParameter {
                        Attempts = 1,
                        ReattemptDelay = TimeSpan.FromSeconds(2),
                        FocalLength = profile.TelescopeSettings.FocalLength,
                        PixelSize = _cameraMediator.GetInfo()?.PixelSize ?? 0,
                        Binning = binVal,
                        SearchRadius = 30.0,
                        Regions = 5000.0,
                        MaxObjects = 500,
                        Coordinates = _telescopeMediator.GetCurrentPosition(),
                        BlindFailoverEnabled = true,
                        DisableNotifications = true
                    };
                    
                    var prog = new Progress<PlateSolveProgress>(p => {
                        // thumbnail ignored in rescue loop as it operates on raw solver
                    });
                    var appStatusProg = new Progress<ApplicationStatus>(s => {
                        if (s.Status != null) {
                            bool isBlind = s.Status.Contains("Astrometry", StringComparison.OrdinalIgnoreCase) || 
                                           s.Status.Contains("All Sky", StringComparison.OrdinalIgnoreCase) || 
                                           s.Status.Contains("AllSky", StringComparison.OrdinalIgnoreCase) ||
                                           s.Status.Contains("Blind", StringComparison.OrdinalIgnoreCase);
                            progress.Report(new AlignmentProgressReport { IsBlindSolvingActive = isBlind });
                        }
                    });
                    
                    try {
                        res = await ExecuteHardwareOperationAsync(() => captureSolver.Solve(seq, solverParam, prog, appStatusProg, rescueToken), rescueToken, "Solve Capture");
                        if (res != null && res.Success && res.Coordinates.Epoch == Epoch.J2000) {
                            res = new PlateSolveResult {
                                Success = res.Success,
                                Coordinates = res.Coordinates.Transform(Epoch.JNOW),
                                PositionAngle = res.PositionAngle
                            };
                        }
                    } catch { } finally {
                        progress.Report(new AlignmentProgressReport { IsBlindSolvingActive = false });
                    }
                }

                if (res != null && res.Success) {
                    var currentCoords = res.Coordinates;
                    double distToPole = 90.0 - Math.Abs(currentCoords.Dec);
                    ReportLog(progress, $"Rescue Position Lock: Dec {currentCoords.DecString} ({distToPole:F2}° from pole)");

                    Vector3D mockAxis = Vector3D.FromEquatorial(currentCoords);
                    double lat = GetLatitude();
                    if (lat >= 0 && mockAxis.Z < 0) mockAxis = new Vector3D(-mockAxis.X, -mockAxis.Y, -mockAxis.Z);
                    else if (lat < 0 && mockAxis.Z > 0) mockAxis = new Vector3D(-mockAxis.X, -mockAxis.Y, -mockAxis.Z);

                    ReportAlignmentProgress(progress, mockAxis, currentCoords.RA, lat, currentCoords.RA);

                    if (distToPole < 5.0) {
                        ReportLog(progress, "★ TARGET ZONE ACQUIRED! Rescued into the < 5.0° radius region.");
                        if (OnInterventionRequested != null) {
                            await OnInterventionRequested(new RescuePromptArgs {
                                Title = "Target Zone Acquired",
                                Message = "Rescue successful! The telescope is now within 5 degrees of the celestial pole.\n\n" +
                                          "Please press the standard START ALIGNMENT button again to begin the final high-precision measurement sequence.",
                                IsYesNo = false
                            });
                        }
                        break;
                    }
                } else {
                    ReportLog(progress, "[Warning] Rescue Solver failed. Adjust scope manually toward true pole and re-check alignment.");
                }
                await Task.Delay(1000, rescueToken);
            }
        }

    }
}

