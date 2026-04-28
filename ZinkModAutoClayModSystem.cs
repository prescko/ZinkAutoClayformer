using HarmonyLib;
using Vintagestory.API.Client;
using Vintagestory.API.Common;

namespace ZinkModAutoClay;

public class ZinkModAutoClayModSystem : ModSystem
{
    private Harmony harmony = null!;

    private static bool running = false;
    private static int lastLayer = 0;
    private static int quantity = 4;

    public static bool Running
    {
        get => running;
        set => running = value;
    }

    public static int LastLayer
    {
        get => lastLayer;
        set => lastLayer = value;
    }

    public static int Quantity
    {
        get => quantity;
        set => quantity = value;
    }

    public override bool ShouldLoad(EnumAppSide forSide)
    {
        return forSide == EnumAppSide.Client;
    }

    public override void StartClientSide(ICoreClientAPI api)
    {
        harmony = new Harmony(Mod.Info.ModID);
        harmony.PatchAll();
    }

    public override void Dispose()
    {
        harmony?.UnpatchAll(Mod.Info.ModID);
    }
}
