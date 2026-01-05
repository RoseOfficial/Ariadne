using Dalamud.Game.ClientState.Conditions;
using FFXIVClientStructs.FFXIV.Client.LayoutEngine;
using System;
using System.IO;
using System.Linq;
using System.Numerics;
using System.Threading;
using System.Threading.Tasks;
using System.Collections.Generic;

namespace Ariadne.Navmesh;

/// <summary>
/// Manages navmesh loading, building, and caching for the current zone.
/// </summary>
public sealed class NavmeshManager : IDisposable
{
    public string CurrentKey { get; private set; } = "";
    public NavmeshData? Navmesh { get; private set; }
    public NavmeshQuery? Query { get; private set; }
    public event Action<NavmeshData?, NavmeshQuery?>? OnNavmeshChanged;

    private volatile float _loadProgress = -1;
    public float LoadProgress => _loadProgress;
    public bool IsLoading => _loadProgress >= 0;

    private CancellationTokenSource? _currentCTS;
    private Task _currentTask = Task.CompletedTask;
    private readonly DirectoryInfo _cacheDir;
    private readonly int _customVersion;

    public NavmeshManager(DirectoryInfo cacheDir, int customVersion = 1)
    {
        _cacheDir = cacheDir;
        _customVersion = customVersion;
        cacheDir.Create();
    }

    public void Dispose()
    {
        Services.Log.Debug("[NavmeshManager] Disposing");
        ClearState();
    }

    /// <summary>
    /// Check if zone changed and reload navmesh if needed.
    /// </summary>
    public void Update()
    {
        var curKey = GetCurrentKey();
        if (curKey != CurrentKey)
        {
            Services.Log.Debug($"[NavmeshManager] Zone changed from '{CurrentKey}' to '{curKey}'");
            CurrentKey = curKey;
            Reload(true);
        }
    }

    /// <summary>
    /// Force reload the navmesh for current zone.
    /// </summary>
    public void Reload(bool allowLoadFromCache)
    {
        ClearState();
        if (CurrentKey.Length == 0)
            return;

        var cts = _currentCTS = new();
        _currentTask = Task.Run(async () =>
        {
            try
            {
                _loadProgress = 0;

                // Wait for cutscene to end
                while (InCutscene)
                {
                    await Task.Delay(100, cts.Token);
                }

                // Build scene definition on main thread
                SceneDefinition? scene = null;
                string? cacheKey = null;
                await Services.Framework.RunOnFrameworkThread(() =>
                {
                    scene = new SceneDefinition();
                    scene.FillFromActiveLayout();
                    cacheKey = GetCacheKey(scene);
                });

                if (scene == null || cacheKey == null)
                    return;

                Services.Log.Debug($"[NavmeshManager] Building navmesh for '{cacheKey}'");

                // Build or load navmesh
                var navmesh = await Task.Run(() => BuildNavmesh(scene, cacheKey, allowLoadFromCache, cts.Token), cts.Token);

                Services.Log.Debug($"[NavmeshManager] Navmesh loaded: '{cacheKey}'");
                Navmesh = navmesh;
                Query = new NavmeshQuery(navmesh);
                OnNavmeshChanged?.Invoke(Navmesh, Query);
            }
            catch (OperationCanceledException)
            {
                Services.Log.Debug("[NavmeshManager] Load cancelled");
            }
            catch (Exception ex)
            {
                Services.Log.Error($"[NavmeshManager] Load failed: {ex}");
            }
            finally
            {
                _loadProgress = -1;
            }
        }, cts.Token);
    }

    /// <summary>
    /// Query a path between two points.
    /// </summary>
    public List<Vector3> QueryPath(Vector3 from, Vector3 to, CancellationToken cancel = default)
    {
        if (Query == null)
        {
            Services.Log.Error("[NavmeshManager] Cannot query path - navmesh not loaded");
            return [];
        }
        return Query.FindPath(from, to, cancel: cancel);
    }

    private static bool InCutscene =>
        Services.Condition[ConditionFlag.WatchingCutscene] ||
        Services.Condition[ConditionFlag.OccupiedInCutSceneEvent];

    private unsafe string GetCurrentKey()
    {
        var layout = LayoutWorld.Instance()->ActiveLayout;
        if (layout == null || layout->InitState != 7 || layout->FestivalStatus is > 0 and < 5)
            return "";

        var filter = LayoutUtils.FindFilter(layout);
        var filterKey = filter != null ? filter->Key : 0;
        var terrRow = Services.LuminaRow<Lumina.Excel.Sheets.TerritoryType>(
            filter != null ? filter->TerritoryTypeId : layout->TerritoryTypeId);

        return $"{terrRow?.Bg}//{filterKey:X}//{LayoutUtils.FestivalsString(layout->ActiveFestivals)}";
    }

    private static unsafe string GetCacheKey(SceneDefinition scene)
    {
        var layout = LayoutWorld.Instance()->ActiveLayout;
        var filter = LayoutUtils.FindFilter(layout);
        var filterKey = filter != null ? filter->Key : 0;
        var terrRow = Services.LuminaRow<Lumina.Excel.Sheets.TerritoryType>(
            filter != null ? filter->TerritoryTypeId : layout->TerritoryTypeId);

        var bg = terrRow?.Bg.ToString().Replace('/', '_') ?? "unknown";
        var festivals = string.Join(".", scene.FestivalLayers.Select(id => id.ToString("X")));
        return $"{bg}__{filterKey:X}__{festivals}";
    }

    private void ClearState()
    {
        if (_currentCTS == null)
            return;

        var cts = _currentCTS;
        _currentCTS = null;
        cts.Cancel();

        try
        {
            _currentTask.Wait(1000);
        }
        catch { }

        cts.Dispose();
        Query = null;
        Navmesh = null;
        OnNavmeshChanged?.Invoke(null, null);
    }

    private NavmeshData BuildNavmesh(SceneDefinition scene, string cacheKey, bool allowLoadFromCache, CancellationToken cancel)
    {
        var cache = new FileInfo($"{_cacheDir.FullName}/{cacheKey}.navmesh");

        // Try loading from cache
        if (allowLoadFromCache && cache.Exists)
        {
            try
            {
                Services.Log.Debug($"[NavmeshManager] Loading cache: {cache.FullName}");
                using var stream = cache.OpenRead();
                using var reader = new BinaryReader(stream);
                return NavmeshData.Deserialize(reader, _customVersion);
            }
            catch (Exception ex)
            {
                Services.Log.Debug($"[NavmeshManager] Cache load failed: {ex.Message}");
            }
        }

        cancel.ThrowIfCancellationRequested();

        // Build from scratch
        var builder = new NavmeshBuilder(scene);
        var totalTiles = builder.NumTilesX * builder.NumTilesZ;
        var builtTiles = 0;

        for (int z = 0; z < builder.NumTilesZ; z++)
        {
            for (int x = 0; x < builder.NumTilesX; x++)
            {
                builder.BuildTile(x, z);
                builtTiles++;
                _loadProgress = (float)builtTiles / totalTiles;
                cancel.ThrowIfCancellationRequested();
            }
        }

        // Save to cache
        try
        {
            Services.Log.Debug($"[NavmeshManager] Saving cache: {cache.FullName}");
            using var stream = cache.Open(FileMode.Create, FileAccess.Write, FileShare.None);
            using var writer = new BinaryWriter(stream);
            builder.NavmeshData.Serialize(writer);
        }
        catch (Exception ex)
        {
            Services.Log.Error($"[NavmeshManager] Failed to save cache: {ex.Message}");
        }

        return builder.NavmeshData;
    }
}
