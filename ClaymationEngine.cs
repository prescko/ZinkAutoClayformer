using System;
using System.Collections.Generic;
using System.Linq;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Config;
using Vintagestory.API.MathTools;
using Vintagestory.GameContent;

namespace ClayFormer;

public class ClaymationEngine
{
	private ICoreClientAPI capi;

	private BlockEntityClayForm clayForm;

	private long timerId;

	private bool isActive = false;

	private int lastKnownToolMode = -1;

	private Queue<ClayAction> actionQueue;

	private List<ClayAction> executedActions;

	private int currentLayer = -1;

	private string currentRecipeCode;

	private const int TICK_INTERVAL_MS = 50;

	private const int MAX_ACTIONS_PER_TICK = 2;

	private bool isCorrectingLayer = false;

	public ClaymationEngine(ICoreClientAPI api, BlockEntityClayForm form)
	{
		capi = api;
		clayForm = form;
		actionQueue = new Queue<ClayAction>();
		executedActions = new List<ClayAction>();
	}

	public void Start()
	{
		if (isActive)
		{
			return;
		}
		isActive = true;
		lastKnownToolMode = -1;
		currentLayer = -1;
		actionQueue.Clear();
		executedActions.Clear();
		currentRecipeCode = GetRecipeHash(clayForm.SelectedRecipe);
		if (RecipeCache.TryGetRecipe(currentRecipeCode, out List<ClayAction> actions))
		{
			foreach (ClayAction item in actions)
			{
				actionQueue.Enqueue(item);
			}
			capi.ShowChatMessage(Lang.Get("clayformer:msg-started", Array.Empty<object>()) + " (cached)");
		}
		else
		{
			capi.ShowChatMessage(Lang.Get("clayformer:msg-started", Array.Empty<object>()) + " (calculating...)");
		}
		timerId = ((IWorldAccessor)capi.World).RegisterGameTickListener((Action<float>)OnGameTick, 50, 0);
	}

	private string GetRecipeHash(ClayFormingRecipe recipe)
	{
		if (recipe == null || ((LayeredVoxelRecipe)(object)recipe).Voxels == null)
		{
			return "unknown";
		}
		int num = 17;
		for (int i = 0; i < 16; i++)
		{
			for (int j = 0; j < 16; j++)
			{
				for (int k = 0; k < 16; k++)
				{
					if (((LayeredVoxelRecipe)(object)recipe).Voxels[j, i, k])
					{
						num = num * 31 + (j * 256 + i * 16 + k);
					}
				}
			}
		}
		return num.ToString();
	}

	public void Stop()
	{
		if (isActive)
		{
			isActive = false;
			((IWorldAccessor)capi.World).UnregisterGameTickListener(timerId);
			actionQueue.Clear();
			executedActions.Clear();
			ClayFormerMod.UnregisterEngine(((BlockEntity)clayForm).Pos);
		}
	}

	private void OnGameTick(float dt)
	{
		//IL_0062: Unknown result type (might be due to invalid IL or missing references)
		//IL_006c: Expected O, but got Unknown
		BlockEntity blockEntity = ((IWorldAccessor)capi.World).BlockAccessor.GetBlockEntity(((BlockEntity)clayForm).Pos);
		if (blockEntity == null || !(blockEntity is BlockEntityClayForm))
		{
			capi.ShowChatMessage(Lang.Get("clayformer:msg-success", Array.Empty<object>()));
			Stop();
			return;
		}
		clayForm = (BlockEntityClayForm)blockEntity;
		if (clayForm.SelectedRecipe == null)
		{
			return;
		}
		ItemSlot activeHotbarSlot = ((IPlayer)capi.World.Player).InventoryManager.ActiveHotbarSlot;
		if (activeHotbarSlot.Empty || !((RegistryObject)activeHotbarSlot.Itemstack.Collectible).Code.Path.Contains("clay"))
		{
			return;
		}
		if (actionQueue.Count == 0)
		{
			if (isCorrectingLayer)
			{
				isCorrectingLayer = false;
				if (!IsLayerComplete(currentLayer))
				{
					PlanLayerCorrection(currentLayer);
					isCorrectingLayer = true;
					return;
				}
			}
			int nextUnfinishedLayer = GetNextUnfinishedLayer();
			if (nextUnfinishedLayer == -1)
			{
				if (!RecipeCache.TryGetRecipe(currentRecipeCode, out List<ClayAction> _) && executedActions.Count > 0)
				{
					RecipeCache.SaveRecipe(currentRecipeCode, executedActions);
				}
				capi.ShowChatMessage(Lang.Get("clayformer:msg-success", Array.Empty<object>()));
				Stop();
				return;
			}
			if (nextUnfinishedLayer != currentLayer)
			{
				currentLayer = nextUnfinishedLayer;
				PlanLayer(currentLayer);
			}
		}
		int num = 0;
		while (actionQueue.Count > 0 && num < 2)
		{
			ClayAction clayAction = actionQueue.Dequeue();
			bool flag = clayForm.Voxels[clayAction.Position.X, clayAction.Position.Y, clayAction.Position.Z];
			bool flag2 = ((LayeredVoxelRecipe)(object)clayForm.SelectedRecipe).Voxels[clayAction.Position.X, clayAction.Position.Y, clayAction.Position.Z];
			if (flag != flag2)
			{
				ExecuteAction(clayAction);
				executedActions.Add(clayAction);
				num++;
			}
		}
	}

	private bool IsLayerComplete(int layer)
	{
		for (int i = 0; i < 16; i++)
		{
			for (int j = 0; j < 16; j++)
			{
				bool flag = clayForm.Voxels[i, layer, j];
				bool flag2 = ((LayeredVoxelRecipe)(object)clayForm.SelectedRecipe).Voxels[i, layer, j];
				if (flag != flag2)
				{
					return false;
				}
			}
		}
		return true;
	}

	private void PlanLayerCorrection(int layer)
	{
		Dictionary<Vec3i, bool> layerChanges = GetLayerChanges(layer);
		foreach (KeyValuePair<Vec3i, bool> item in layerChanges)
		{
			actionQueue.Enqueue(new ClayAction(item.Key, 0, !item.Value));
		}
	}

	private void PlanLayer(int layer)
	{
		Dictionary<Vec3i, bool> layerChanges = GetLayerChanges(layer);
		if (layerChanges.Count != 0)
		{
			List<Vec3i> positions = (from c in layerChanges
				where c.Value
				select c.Key).ToList();
			List<Vec3i> positions2 = (from c in layerChanges
				where !c.Value
				select c.Key).ToList();
			PlanRemovalActions(positions2);
			PlanAdditionActions(positions);
			isCorrectingLayer = true;
		}
	}

	private void PlanRemovalActions(List<Vec3i> positions)
	{
		if (positions.Count == 0)
		{
			return;
		}
		HashSet<Vec3i> hashSet = new HashSet<Vec3i>(positions);
		while (hashSet.Count > 0)
		{
			Vec3i pos = Vec3i.Zero;
			int mode = 0;
			List<Vec3i> list = null;
			float num = 0f;
			foreach (Vec3i item in hashSet)
			{
				(List<Vec3i>, float) tuple = EvaluateTool3x3ForRemoval(item, hashSet);
				if (tuple.Item2 > num)
				{
					pos = item;
					mode = 2;
					(list, num) = tuple;
				}
				(List<Vec3i>, float) tuple3 = EvaluateTool2x2ForRemoval(item, hashSet);
				if (tuple3.Item2 > num)
				{
					pos = item;
					mode = 1;
					(list, num) = tuple3;
				}
			}
			if (num > 0.3f && list != null && list.Count > 1)
			{
				actionQueue.Enqueue(new ClayAction(pos, mode, removing: true));
				foreach (Vec3i item2 in list)
				{
					hashSet.Remove(item2);
				}
			}
			else
			{
				Vec3i val = hashSet.First();
				actionQueue.Enqueue(new ClayAction(val, 0, removing: true));
				hashSet.Remove(val);
			}
		}
	}

	private void PlanAdditionActions(List<Vec3i> positions)
	{
		if (positions.Count == 0)
		{
			return;
		}
		HashSet<Vec3i> hashSet = new HashSet<Vec3i>(positions);
		if (currentLayer > 0)
		{
			TryLayerCopyForAdditions(hashSet);
		}
		while (hashSet.Count > 0)
		{
			Vec3i pos = Vec3i.Zero;
			int mode = 0;
			List<Vec3i> list = null;
			float num = 0f;
			foreach (Vec3i item in hashSet)
			{
				(List<Vec3i>, float) tuple = EvaluateTool3x3ForAddition(item, hashSet);
				if (tuple.Item2 > num)
				{
					pos = item;
					mode = 2;
					(list, num) = tuple;
				}
				(List<Vec3i>, float) tuple3 = EvaluateTool2x2ForAddition(item, hashSet);
				if (tuple3.Item2 > num)
				{
					pos = item;
					mode = 1;
					(list, num) = tuple3;
				}
			}
			if (num > 0.3f && list != null && list.Count > 1)
			{
				actionQueue.Enqueue(new ClayAction(pos, mode, removing: false));
				foreach (Vec3i item2 in list)
				{
					hashSet.Remove(item2);
				}
			}
			else
			{
				Vec3i val = hashSet.First();
				actionQueue.Enqueue(new ClayAction(val, 0, removing: false));
				hashSet.Remove(val);
			}
		}
	}

	private (List<Vec3i> covered, float efficiency) EvaluateTool3x3ForAddition(Vec3i center, HashSet<Vec3i> targets)
	{
		//IL_0031: Unknown result type (might be due to invalid IL or missing references)
		//IL_0038: Expected O, but got Unknown
		List<Vec3i> list = new List<Vec3i>();
		int num = 0;
		for (int i = -1; i <= 1; i++)
		{
			for (int j = -1; j <= 1; j++)
			{
				Vec3i val = new Vec3i(center.X + i, center.Y, center.Z + j);
				if (val.X < 0 || val.X >= 16 || val.Z < 0 || val.Z >= 16)
				{
					continue;
				}
				if (targets.Contains(val))
				{
					list.Add(val);
					continue;
				}
				bool flag = clayForm.Voxels[val.X, val.Y, val.Z];
				bool flag2 = ((LayeredVoxelRecipe)(object)clayForm.SelectedRecipe).Voxels[val.X, val.Y, val.Z];
				if (!flag && !flag2)
				{
					num++;
				}
			}
		}
		float item = (float)(list.Count - num * 2) / 9f;
		if (num > list.Count)
		{
			item = 0f;
		}
		return (covered: list, efficiency: item);
	}

	private (List<Vec3i> covered, float efficiency) EvaluateTool2x2ForAddition(Vec3i topRight, HashSet<Vec3i> targets)
	{
		//IL_0031: Unknown result type (might be due to invalid IL or missing references)
		//IL_0038: Expected O, but got Unknown
		List<Vec3i> list = new List<Vec3i>();
		int num = 0;
		for (int i = -1; i <= 0; i++)
		{
			for (int j = -1; j <= 0; j++)
			{
				Vec3i val = new Vec3i(topRight.X + i, topRight.Y, topRight.Z + j);
				if (val.X < 0 || val.X >= 16 || val.Z < 0 || val.Z >= 16)
				{
					continue;
				}
				if (targets.Contains(val))
				{
					list.Add(val);
					continue;
				}
				bool flag = clayForm.Voxels[val.X, val.Y, val.Z];
				bool flag2 = ((LayeredVoxelRecipe)(object)clayForm.SelectedRecipe).Voxels[val.X, val.Y, val.Z];
				if (!flag && !flag2)
				{
					num++;
				}
			}
		}
		float item = (float)(list.Count - num * 2) / 4f;
		if (num > list.Count)
		{
			item = 0f;
		}
		return (covered: list, efficiency: item);
	}

	private (List<Vec3i> covered, float efficiency) EvaluateTool3x3ForRemoval(Vec3i center, HashSet<Vec3i> targets)
	{
		//IL_0031: Unknown result type (might be due to invalid IL or missing references)
		//IL_0038: Expected O, but got Unknown
		List<Vec3i> list = new List<Vec3i>();
		int num = 0;
		for (int i = -1; i <= 1; i++)
		{
			for (int j = -1; j <= 1; j++)
			{
				Vec3i val = new Vec3i(center.X + i, center.Y, center.Z + j);
				if (val.X < 0 || val.X >= 16 || val.Z < 0 || val.Z >= 16)
				{
					continue;
				}
				if (targets.Contains(val))
				{
					list.Add(val);
					continue;
				}
				bool flag = clayForm.Voxels[val.X, val.Y, val.Z];
				bool flag2 = ((LayeredVoxelRecipe)(object)clayForm.SelectedRecipe).Voxels[val.X, val.Y, val.Z];
				if (flag && flag2)
				{
					num++;
				}
			}
		}
		float item = (float)(list.Count - num * 2) / 9f;
		if (num > 0)
		{
			item = 0f;
		}
		return (covered: list, efficiency: item);
	}

	private (List<Vec3i> covered, float efficiency) EvaluateTool2x2ForRemoval(Vec3i topRight, HashSet<Vec3i> targets)
	{
		//IL_0031: Unknown result type (might be due to invalid IL or missing references)
		//IL_0038: Expected O, but got Unknown
		List<Vec3i> list = new List<Vec3i>();
		int num = 0;
		for (int i = -1; i <= 0; i++)
		{
			for (int j = -1; j <= 0; j++)
			{
				Vec3i val = new Vec3i(topRight.X + i, topRight.Y, topRight.Z + j);
				if (val.X < 0 || val.X >= 16 || val.Z < 0 || val.Z >= 16)
				{
					continue;
				}
				if (targets.Contains(val))
				{
					list.Add(val);
					continue;
				}
				bool flag = clayForm.Voxels[val.X, val.Y, val.Z];
				bool flag2 = ((LayeredVoxelRecipe)(object)clayForm.SelectedRecipe).Voxels[val.X, val.Y, val.Z];
				if (flag && flag2)
				{
					num++;
				}
			}
		}
		float item = (float)(list.Count - num * 2) / 4f;
		if (num > 0)
		{
			item = 0f;
		}
		return (covered: list, efficiency: item);
	}

	private void TryLayerCopyForAdditions(HashSet<Vec3i> remaining)
	{
		//IL_00af: Unknown result type (might be due to invalid IL or missing references)
		//IL_00b6: Expected O, but got Unknown
		if (currentLayer == 0)
		{
			return;
		}
		List<Vec3i> list = remaining.ToList();
		HashSet<Vec3i> hashSet = new HashSet<Vec3i>();
		foreach (Vec3i item2 in list)
		{
			if (hashSet.Contains(item2))
			{
				continue;
			}
			List<Vec3i> list2 = new List<Vec3i>();
			int num = 0;
			for (int i = -1; i <= 0; i++)
			{
				for (int j = -1; j <= 0; j++)
				{
					int num2 = item2.X + i;
					int num3 = item2.Z + j;
					if (num2 >= 0 && num2 < 16 && num3 >= 0 && num3 < 16)
					{
						Vec3i item = new Vec3i(num2, currentLayer, num3);
						bool flag = clayForm.Voxels[num2, currentLayer - 1, num3];
						bool flag2 = ((LayeredVoxelRecipe)(object)clayForm.SelectedRecipe).Voxels[num2, currentLayer, num3];
						if (flag && flag2 && remaining.Contains(item))
						{
							num++;
							list2.Add(item);
						}
						else if (!flag && flag2)
						{
							num = -100;
							break;
						}
					}
				}
				if (num < 0)
				{
					break;
				}
			}
			if (num < 3 || list2.Count < 3)
			{
				continue;
			}
			actionQueue.Enqueue(new ClayAction(item2, 3, removing: false));
			foreach (Vec3i item3 in list2)
			{
				remaining.Remove(item3);
				hashSet.Add(item3);
			}
		}
	}

	private Dictionary<Vec3i, bool> GetLayerChanges(int layer)
	{
		//IL_0052: Unknown result type (might be due to invalid IL or missing references)
		//IL_005e: Expected O, but got Unknown
		Dictionary<Vec3i, bool> dictionary = new Dictionary<Vec3i, bool>();
		for (int i = 0; i < 16; i++)
		{
			for (int j = 0; j < 16; j++)
			{
				bool flag = clayForm.Voxels[i, layer, j];
				bool flag2 = ((LayeredVoxelRecipe)(object)clayForm.SelectedRecipe).Voxels[i, layer, j];
				if (flag != flag2)
				{
					dictionary[new Vec3i(i, layer, j)] = flag2;
				}
			}
		}
		return dictionary;
	}

	private int GetNextUnfinishedLayer()
	{
		for (int i = 0; i < 16; i++)
		{
			for (int j = 0; j < 16; j++)
			{
				for (int k = 0; k < 16; k++)
				{
					if (clayForm.Voxels[j, i, k] != ((LayeredVoxelRecipe)(object)clayForm.SelectedRecipe).Voxels[j, i, k])
					{
						return i;
					}
				}
			}
		}
		return -1;
	}

	private void ExecuteAction(ClayAction action)
	{
		if (lastKnownToolMode != action.ToolMode)
		{
			SetToolMode(action.ToolMode);
		}
		BlockFacing uP = BlockFacing.UP;
		clayForm.OnUseOver((IPlayer)(object)capi.World.Player, action.Position, uP, action.IsRemoving);
	}

	private void SetToolMode(int mode)
	{
		//IL_00a1: Unknown result type (might be due to invalid IL or missing references)
		//IL_00a6: Unknown result type (might be due to invalid IL or missing references)
		//IL_00bd: Expected O, but got Unknown
		ItemSlot activeHotbarSlot = ((IPlayer)capi.World.Player).InventoryManager.ActiveHotbarSlot;
		if (activeHotbarSlot.Empty)
		{
			return;
		}
		int num = activeHotbarSlot.Itemstack.Attributes.GetInt("toolMode", -1);
		if (num == mode)
		{
			lastKnownToolMode = mode;
			return;
		}
		activeHotbarSlot.Itemstack.Attributes.SetInt("toolMode", mode);
		if (activeHotbarSlot.Itemstack.Collectible != null)
		{
			activeHotbarSlot.Itemstack.Collectible.SetToolMode(activeHotbarSlot, (IPlayer)(object)capi.World.Player, new BlockSelection
			{
				Position = ((BlockEntity)clayForm).Pos
			}, mode);
		}
		Packet_Client packet_Client = new Packet_Client
		{
			Id = 27,
			ToolMode = new Packet_ToolMode
			{
				Mode = mode,
				X = ((BlockEntity)clayForm).Pos.X,
				Y = ((BlockEntity)clayForm).Pos.Y,
				Z = ((BlockEntity)clayForm).Pos.Z
			}
		};
		capi.Network.SendPacketClient((object)packet_Client);
		activeHotbarSlot.MarkDirty();
		lastKnownToolMode = mode;
	}
}
