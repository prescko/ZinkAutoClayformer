using HarmonyLib;
using Vintagestory.API.Common;
using Vintagestory.GameContent;

namespace ZinkModAutoClay;

[HarmonyPatch(typeof(BlockEntityClayForm), "OnCopyLayer")]
public class ModifyBlockEntityClayFormFromClient
{
    public static bool Prefix(ref BlockEntityClayForm __instance, ref bool __result, int layer)
    {
        try
        {
            if (layer < 0 || layer > 15)
            {
                __result = false;
                return false;
            }

            bool changed = false;
            int remaining = 4;
            bool[,,] voxels = ((LayeredVoxelRecipe)(object)__instance.SelectedRecipe).Voxels;

            for (int i = 0; i < 16; i++)
            {
                for (int j = 0; j < 16; j++)
                {
                    if (voxels[i, layer, j] != __instance.Voxels[i, layer, j])
                    {
                        remaining--;
                        __instance.Voxels[i, layer, j] = voxels[i, layer, j];
                        __instance.AvailableVoxels += (!voxels[i, layer, j]) ? 1 : -1;
                        changed = true;
                    }

                    if (remaining == 0)
                    {
                        __result = changed;
                        return false;
                    }
                }
            }

            __result = changed;
            return false;
        }
        catch
        {
            ((BlockEntity)__instance).Api.Logger.Chat("(Auto Layer Clay Forming) Error_2: Something went wrong! Try Again...");
        }

        return true;
    }
}
