using HarmonyLib;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;
using Vintagestory.GameContent;

namespace ClayFormer;

[HarmonyPatch(typeof(BlockEntityClayForm), "OnBlockRemoved")]
public class ClayFormRemovedPatch
{
	private static void Prefix(BlockEntityClayForm __instance)
	{
		BlockPos? pos = ((BlockEntity)__instance)?.Pos;
		if (pos != null)
		{
			ClayFormerMod.UnregisterEngine(pos);
		}
	}
}
