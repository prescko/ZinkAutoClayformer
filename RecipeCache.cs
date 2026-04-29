using System.Collections.Generic;

namespace ClayFormer;

public static class RecipeCache
{
	private static Dictionary<string, List<ClayAction>> cache = new Dictionary<string, List<ClayAction>>();

	public static bool TryGetRecipe(string recipeCode, out List<ClayAction> actions)
	{
		return cache.TryGetValue(recipeCode, out actions);
	}

	public static void SaveRecipe(string recipeCode, List<ClayAction> actions)
	{
		cache[recipeCode] = new List<ClayAction>(actions);
	}

	public static void Clear()
	{
		cache.Clear();
	}
}
