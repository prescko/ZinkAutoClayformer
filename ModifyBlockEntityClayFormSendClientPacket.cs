using HarmonyLib;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;
using Vintagestory.Client.NoObf;
using Vintagestory.GameContent;

namespace ZinkModAutoClay;

[HarmonyPatch(typeof(BlockEntityClayForm), "SendUseOverPacket")]
public class ModifyBlockEntityClayFormSendClientPacket
{
    private const int PacketId = 27;
    private const int SingleMode = 0;
    private const int TargetMode = 3;

    public static bool Prefix(
        ref BlockEntityClayForm __instance,
        ref IPlayer byPlayer,
        ref Vec3i voxelPos,
        ref BlockFacing facing,
        bool mouseMode)
    {
        try
        {
            ItemSlot activeSlot = byPlayer.InventoryManager.ActiveHotbarSlot;

            if (mouseMode || activeSlot.Itemstack == null || !__instance.CanWorkCurrent || ZinkModAutoClayModSystem.Running)
                return true;

            ICoreClientAPI capi = (ICoreClientAPI)((BlockEntity)__instance).Api;

            int toolMode = activeSlot.Itemstack.Collectible.GetToolMode(activeSlot, byPlayer, new BlockSelection
            {
                Position = ((BlockEntity)__instance).Pos
            });

            if (toolMode == TargetMode && !ZinkModAutoClayModSystem.Running)
            {
                IClientNetworkAPI network = capi.Network;
                SetServerToolMode(network, SingleMode);

                ZinkModAutoClayModSystem.Running = true;
                ZinkModAutoClayModSystem.LastLayer = 0;

                int startLayer = ZinkModAutoClayModSystem.LastLayer;
                int remaining = ZinkModAutoClayModSystem.Quantity;
                bool[,,] voxels = ((LayeredVoxelRecipe)(object)__instance.SelectedRecipe).Voxels;

                for (int i = startLayer; i < 16; i++)
                {
                    for (int j = 0; j < 16; j++)
                    {
                        for (int k = 0; k < 16; k++)
                        {
                            if (voxels[j, i, k] != __instance.Voxels[j, i, k])
                            {
                                remaining--;
                                ZinkModAutoClayModSystem.LastLayer = i;
                                __instance.SendUseOverPacket(byPlayer, new Vec3i(j, i, k), facing, !voxels[j, i, k]);

                                if (remaining == 0)
                                {
                                    ZinkModAutoClayModSystem.Running = false;
                                    SetServerToolMode(network, toolMode);
                                    return false;
                                }
                            }
                        }
                    }
                }

                ZinkModAutoClayModSystem.Running = false;
                SetServerToolMode(network, toolMode);
            }
        }
        catch
        {
            ZinkModAutoClayModSystem.LastLayer = 0;
            ZinkModAutoClayModSystem.Running = false;
            ((BlockEntity)__instance).Api.Logger.Chat("(Auto Layer Clay Forming) Error_1: Something went wrong! Try Again...");
        }

        return true;
    }

    private static void SetServerToolMode(IClientNetworkAPI network, int toolMode)
    {
        network.SendPacketClient(new Packet_Client
        {
            Id = PacketId,
            ToolMode = new Packet_ToolMode
            {
                Mode = toolMode
            }
        });
    }
}
