using Vintagestory.API.MathTools;

namespace ClayFormer;

public struct ClayAction(Vec3i pos, int mode, bool removing)
{
	public Vec3i Position = pos;

	public int ToolMode = mode;

	public bool IsRemoving = removing;
}
