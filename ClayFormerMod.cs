using System.Collections.Generic;
using HarmonyLib;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;
using Vintagestory.GameContent;

namespace ClayFormer;

public class ClayFormerMod : ModSystem
{
    private Harmony harmony = null!;
    private static ICoreClientAPI? staticCapi;
    private static Dictionary<BlockPos, ClaymationEngine> activeEngines = new();

    // Polling state
    private long pollingTimerId;
    private Dictionary<BlockPos, ClayFormingRecipe?> lastKnownRecipes = new();

    public override void StartClientSide(ICoreClientAPI api)
    {
        staticCapi = api;
        harmony = new Harmony("com.rekimchuk13.clayformer");
        harmony.PatchAll();

        // Poll for recipe changes every 200ms instead of patching an unstable method name
        pollingTimerId = ((IWorldAccessor)api.World).RegisterGameTickListener(OnPollTick, 200, 0);

        api.Event.LeftWorld += OnLeftWorld;
    }

    private void OnPollTick(float dt)
    {
        if (staticCapi == null) return;

        var world = (IWorldAccessor)staticCapi.World;
        var player = staticCapi.World.Player;
        if (player == null) return;

        var playerPos = player.Entity?.Pos?.AsBlockPos;
        if (playerPos == null) return;

        int radius = 6;
        for (int dx = -radius; dx <= radius; dx++)
        for (int dy = -2; dy <= 2; dy++)
        for (int dz = -radius; dz <= radius; dz++)
        {
            var pos = playerPos.AddCopy(dx, dy, dz);
            var be = world.BlockAccessor.GetBlockEntity(pos);
            if (be is not BlockEntityClayForm clayForm) continue;

            var currentRecipe = clayForm.SelectedRecipe;

            lastKnownRecipes.TryGetValue(pos, out var previousRecipe);

            bool recipeChanged = !ReferenceEquals(currentRecipe, previousRecipe);

            if (recipeChanged)
            {
                lastKnownRecipes[pos] = currentRecipe;

                UnregisterEngine(pos);

                if (currentRecipe != null)
                {
                    var engine = new ClaymationEngine(staticCapi, clayForm);
                    RegisterEngine(pos, engine);
                    engine.Start();
                }
            }
        }

        // Clean up stale entries for block entities no longer in world
        var toRemove = new List<BlockPos>();
        foreach (var pos in lastKnownRecipes.Keys)
        {
            var be = world.BlockAccessor.GetBlockEntity(pos);
            if (be is not BlockEntityClayForm)
                toRemove.Add(pos);
        }
        foreach (var pos in toRemove)
        {
            lastKnownRecipes.Remove(pos);
            UnregisterEngine(pos);
        }
    }

    private void OnLeftWorld()
    {
        foreach (ClaymationEngine value in activeEngines.Values)
            value?.Stop();
        activeEngines.Clear();
        lastKnownRecipes.Clear();
    }

    public override void Dispose()
    {
        if (staticCapi != null && pollingTimerId != 0)
            ((IWorldAccessor)staticCapi.World).UnregisterGameTickListener(pollingTimerId);

        foreach (ClaymationEngine value in activeEngines.Values)
            value?.Stop();
        activeEngines.Clear();
        lastKnownRecipes.Clear();

        harmony?.UnpatchAll("com.rekimchuk13.clayformer");
        staticCapi = null;
        ((ModSystem)this).Dispose();
    }

    public static void RegisterEngine(BlockPos pos, ClaymationEngine engine)
    {
        if (activeEngines.ContainsKey(pos))
            activeEngines[pos]?.Stop();
        activeEngines[pos] = engine;
    }

    public static void UnregisterEngine(BlockPos pos)
    {
        if (activeEngines.TryGetValue(pos, out ClaymationEngine? engine))
        {
            engine?.Stop();
            activeEngines.Remove(pos);
        }
    }
}
