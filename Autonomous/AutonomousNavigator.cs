using Ariadne.Autonomous.Models;
using Ariadne.Navmesh;
using Ariadne.Navigation;
using Dalamud.Plugin.Services;
using System;
using System.Numerics;

namespace Ariadne.Autonomous;

/// <summary>
/// Navigation states for autonomous dungeon exploration.
/// </summary>
public enum AutonomousState
{
    /// <summary>Not active.</summary>
    Idle,

    /// <summary>Initializing spatial analysis.</summary>
    Initializing,

    /// <summary>Moving toward unexplored areas.</summary>
    Exploring,

    /// <summary>Moving toward a detected objective.</summary>
    Approaching,

    /// <summary>Paused - combat in progress.</summary>
    InCombat,

    /// <summary>Waiting for progression (gate blocked).</summary>
    WaitingForProgression,

    /// <summary>Paused for cutscene.</summary>
    WatchingCutscene,

    /// <summary>Dungeon complete.</summary>
    Complete,

    /// <summary>Unrecoverable error.</summary>
    Error
}

/// <summary>
/// Autonomous dungeon navigator that explores and clears without predefined waypoints.
/// </summary>
public class AutonomousNavigator : IDisposable
{
    private readonly NavigationService _navigation;
    private readonly NavmeshManager _navmeshManager;
    private readonly CombatMonitor _combatMonitor;
    private readonly ObjectiveDetector _objectiveDetector;
    private readonly SpatialAnalyzer _spatialAnalyzer;
    private readonly Configuration _config;

    private AutonomousState _previousState;
    private DateTime _lastStateChange;
    private DateTime _lastPositionCheck;
    private Vector3 _lastPosition;
    private float _stuckTime;
    private int _retryCount;

    /// <summary>
    /// Current navigation state.
    /// </summary>
    public AutonomousState State { get; private set; } = AutonomousState.Idle;

    /// <summary>
    /// Human-readable status message.
    /// </summary>
    public string StatusMessage { get; private set; } = "Idle";

    /// <summary>
    /// Current objective being pursued.
    /// </summary>
    public DungeonObjective? CurrentObjective => _objectiveDetector.CurrentObjective;

    /// <summary>
    /// Number of detected enemies.
    /// </summary>
    public int EnemyCount => _combatMonitor.NearbyEnemies.Count;

    /// <summary>
    /// Whether currently in combat.
    /// </summary>
    public bool IsInCombat => _combatMonitor.IsInCombat;

    /// <summary>
    /// Number of unexplored areas.
    /// </summary>
    public int UnexploredAreaCount => _spatialAnalyzer.GetExplorationFrontier().Count;

    private const int MaxRetries = 3;
    private const float StuckTimeoutSeconds = 5f;
    private const float StuckTolerance = 0.1f;

    public AutonomousNavigator(
        NavigationService navigation,
        NavmeshManager navmeshManager,
        CombatMonitor combatMonitor,
        ObjectiveDetector objectiveDetector,
        SpatialAnalyzer spatialAnalyzer,
        Configuration config)
    {
        _navigation = navigation;
        _navmeshManager = navmeshManager;
        _combatMonitor = combatMonitor;
        _objectiveDetector = objectiveDetector;
        _spatialAnalyzer = spatialAnalyzer;
        _config = config;
    }

    public void Dispose()
    {
        Stop();
    }

    /// <summary>
    /// Start autonomous navigation.
    /// </summary>
    public void Start()
    {
        if (State != AutonomousState.Idle)
        {
            Services.Log.Warning("[Autonomous] Already running");
            return;
        }

        Services.Log.Info("[Autonomous] Starting autonomous navigation");
        _stuckTime = 0;
        _retryCount = 0;
        _lastPositionCheck = DateTime.Now;
        _lastPosition = GetPlayerPosition();

        TransitionTo(AutonomousState.Initializing);
    }

    /// <summary>
    /// Stop autonomous navigation.
    /// </summary>
    public void Stop()
    {
        if (State == AutonomousState.Idle)
            return;

        Services.Log.Info("[Autonomous] Stopping autonomous navigation");
        _navigation.Stop();
        TransitionTo(AutonomousState.Idle);
    }

    /// <summary>
    /// Update navigation state. Called every frame.
    /// </summary>
    public void Update(IFramework framework)
    {
        if (State == AutonomousState.Idle || State == AutonomousState.Complete || State == AutonomousState.Error)
            return;

        // Update monitors
        _combatMonitor.Update();

        // Check for zone change
        var currentTerritory = Services.ClientState.TerritoryType;
        if (_lastTerritory != 0 && _lastTerritory != currentTerritory)
        {
            Services.Log.Info("[Autonomous] Zone changed, stopping");
            Stop();
            return;
        }
        _lastTerritory = currentTerritory;

        // Handle cutscene
        if (_combatMonitor.IsCutsceneActive && State != AutonomousState.WatchingCutscene)
        {
            _previousState = State;
            TransitionTo(AutonomousState.WatchingCutscene);
            return;
        }

        // State machine update
        switch (State)
        {
            case AutonomousState.Initializing:
                UpdateInitializing();
                break;
            case AutonomousState.Exploring:
            case AutonomousState.Approaching:
                UpdateNavigating();
                break;
            case AutonomousState.InCombat:
                UpdateInCombat();
                break;
            case AutonomousState.WaitingForProgression:
                UpdateWaitingForProgression();
                break;
            case AutonomousState.WatchingCutscene:
                UpdateWatchingCutscene();
                break;
        }
    }

    private uint _lastTerritory;

    private void UpdateInitializing()
    {
        // Wait for navmesh
        if (!_navigation.IsReady)
        {
            var progress = _navmeshManager.LoadProgress;
            if (progress >= 0)
            {
                StatusMessage = $"Building navmesh: {progress:P0}";
            }
            else
            {
                StatusMessage = "Waiting for navmesh...";
            }
            return;
        }

        // Initialize spatial analyzer
        var playerPos = GetPlayerPosition();
        _spatialAnalyzer.Initialize(playerPos);

        if (!_spatialAnalyzer.IsReady)
        {
            TransitionTo(AutonomousState.Error);
            StatusMessage = "Failed to initialize spatial analyzer";
            return;
        }

        Services.Log.Info("[Autonomous] Initialization complete, starting exploration");
        DecideNextAction();
    }

    private void UpdateNavigating()
    {
        var playerPos = GetPlayerPosition();

        // Update spatial state
        _spatialAnalyzer.Update(playerPos);

        // Check for combat
        if (_combatMonitor.IsInCombat)
        {
            _navigation.Stop();
            TransitionTo(AutonomousState.InCombat);
            return;
        }

        // Update objectives
        _objectiveDetector.Update(playerPos, _spatialAnalyzer);

        // Check if current objective is complete
        if (_objectiveDetector.IsObjectiveReached(playerPos) || _objectiveDetector.IsObjectiveComplete())
        {
            // Mark position as explored if this was an exploration objective
            if (CurrentObjective?.Type == ObjectiveType.Explore)
            {
                _spatialAnalyzer.MarkPositionExplored(CurrentObjective.Position);
            }

            Services.Log.Debug($"[Autonomous] Objective complete: {CurrentObjective?.Description}");
            DecideNextAction();
            return;
        }

        // Check if movement stopped unexpectedly
        if (!_navigation.IsMoveInProgress)
        {
            // Try to re-navigate to objective
            if (CurrentObjective != null)
            {
                _retryCount++;
                if (_retryCount >= MaxRetries)
                {
                    Services.Log.Warning("[Autonomous] Too many retries, checking for stuck condition");
                    // Maybe blocked - wait for progression
                    TransitionTo(AutonomousState.WaitingForProgression);
                    return;
                }

                NavigateToObjective(CurrentObjective);
            }
            else
            {
                DecideNextAction();
            }
            return;
        }

        // Stuck detection
        CheckStuck(playerPos);

        // Update status
        if (CurrentObjective != null)
        {
            var distance = Vector3.Distance(playerPos, CurrentObjective.Position);
            StatusMessage = $"{CurrentObjective.Description} ({distance:F0}m)";
        }
    }

    private void UpdateInCombat()
    {
        StatusMessage = $"In combat ({_combatMonitor.NearbyEnemies.Count} enemies)";

        // Wait for combat to end
        if (!_combatMonitor.IsInCombat)
        {
            Services.Log.Debug("[Autonomous] Combat ended, resuming navigation");
            _retryCount = 0;
            DecideNextAction();
        }
    }

    private void UpdateWaitingForProgression()
    {
        StatusMessage = "Waiting for progression...";

        // Check if we can now reach new areas
        var playerPos = GetPlayerPosition();
        _spatialAnalyzer.Update(playerPos);
        _objectiveDetector.Update(playerPos, _spatialAnalyzer);

        // If we have new objectives or exploration targets, resume
        if (CurrentObjective != null || _spatialAnalyzer.GetExplorationFrontier().Count > 0)
        {
            Services.Log.Info("[Autonomous] Progression detected, resuming");
            _retryCount = 0;
            DecideNextAction();
            return;
        }

        // Check if we're at the exit
        if (_spatialAnalyzer.IsNearExit(playerPos))
        {
            TransitionTo(AutonomousState.Complete);
            StatusMessage = "Dungeon complete!";
        }
    }

    private void UpdateWatchingCutscene()
    {
        StatusMessage = "Watching cutscene...";

        if (!_combatMonitor.IsCutsceneActive)
        {
            Services.Log.Debug("[Autonomous] Cutscene ended, resuming");
            // Return to previous state or decide next action
            if (_previousState == AutonomousState.Exploring || _previousState == AutonomousState.Approaching)
            {
                DecideNextAction();
            }
            else
            {
                TransitionTo(_previousState);
            }
        }
    }

    private void DecideNextAction()
    {
        var playerPos = GetPlayerPosition();

        // Update detectors
        _objectiveDetector.Update(playerPos, _spatialAnalyzer);

        var objective = _objectiveDetector.CurrentObjective;

        if (objective != null)
        {
            NavigateToObjective(objective);

            var newState = objective.Type switch
            {
                ObjectiveType.EnemyGroup or ObjectiveType.Boss => AutonomousState.Approaching,
                ObjectiveType.Explore => AutonomousState.Exploring,
                ObjectiveType.Exit => AutonomousState.Approaching,
                _ => AutonomousState.Exploring
            };

            TransitionTo(newState);
        }
        else
        {
            // No objectives and no exploration targets
            if (_spatialAnalyzer.IsNearExit(playerPos))
            {
                TransitionTo(AutonomousState.Complete);
                StatusMessage = "Dungeon complete!";
            }
            else
            {
                // Might be blocked
                TransitionTo(AutonomousState.WaitingForProgression);
            }
        }
    }

    private void NavigateToObjective(DungeonObjective objective)
    {
        var started = _navigation.PathfindAndMoveTo(objective.Position, fly: false);

        if (!started)
        {
            Services.Log.Warning($"[Autonomous] Failed to pathfind to {objective.Description}");
            _retryCount++;
        }
        else
        {
            Services.Log.Debug($"[Autonomous] Navigating to {objective.Description}");
            _stuckTime = 0;
        }
    }

    private void CheckStuck(Vector3 currentPos)
    {
        var now = DateTime.Now;
        var deltaTime = (float)(now - _lastPositionCheck).TotalSeconds;
        _lastPositionCheck = now;

        var distance = Vector3.Distance(currentPos, _lastPosition);
        _lastPosition = currentPos;

        if (distance < StuckTolerance)
        {
            _stuckTime += deltaTime;

            if (_stuckTime >= StuckTimeoutSeconds)
            {
                Services.Log.Warning("[Autonomous] Stuck detected!");
                _navigation.Stop();
                _retryCount++;

                if (_retryCount >= MaxRetries)
                {
                    TransitionTo(AutonomousState.WaitingForProgression);
                }
                else
                {
                    // Re-try navigation
                    DecideNextAction();
                }
            }
        }
        else
        {
            _stuckTime = 0;
        }
    }

    private void TransitionTo(AutonomousState newState)
    {
        if (State != newState)
        {
            Services.Log.Debug($"[Autonomous] State: {State} -> {newState}");
            State = newState;
            _lastStateChange = DateTime.Now;
        }
    }

    private Vector3 GetPlayerPosition()
    {
        var player = Services.ObjectTable.LocalPlayer;
        return player?.Position ?? Vector3.Zero;
    }
}
