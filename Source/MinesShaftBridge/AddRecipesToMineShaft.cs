using System.Collections.Generic;
using System.Reflection;
using RimWorld;
using Verse;

namespace MinesShaftBridge
{
    // Mines 2.0 builds its mining recipes in C# at startup (see CreateRecipeDefs), hardcoding
    // recipeUsers to MinesAutomated_ThingDef_Mine, and it blocks XML PatchOperations on them.
    // So we add Medieval Overhaul's mine shaft as an extra user in code, after those recipes
    // have been generated. Our mod loads after Mines 2.0, so this static constructor runs after
    // theirs and the recipes already exist in the DefDatabase.
    [StaticConstructorOnStartup]
    public static class AddRecipesToMineShaft
    {
        private const string MineShaftDefName = "DankPyon_MineShaft";
        private const string MinesRecipePrefix = "MinesAutomated_RecipeDef_";

        // Medieval Overhaul's mine shaft already has native bills for these resources, so we skip
        // bridging the Mines 2.0 "Mine for ..." duplicates. Matched by produced thingDef so it
        // catches whichever rock yields them. (The duplicates stay attached only to the hidden
        // Mines 2.0 building, so the player never sees them.)
        private static readonly HashSet<string> ExcludedProducts = new HashSet<string>
        {
            "DankPyon_Coal",
            "DankPyon_Salt",
            "DankPyon_IronOre",
            "DankPyon_GoldOre",
            "DankPyon_SilverOre",
        };

        private static readonly FieldInfo AllRecipesCachedField =
            typeof(ThingDef).GetField("allRecipesCached", BindingFlags.Instance | BindingFlags.NonPublic);

        static AddRecipesToMineShaft()
        {
            ThingDef mineShaft = DefDatabase<ThingDef>.GetNamedSilentFail(MineShaftDefName);
            if (mineShaft == null)
            {
                // Medieval Overhaul not loaded; nothing to do.
                return;
            }

            int added = 0;
            int skipped = 0;
            foreach (RecipeDef recipe in DefDatabase<RecipeDef>.AllDefsListForReading)
            {
                if (recipe.defName == null || !recipe.defName.StartsWith(MinesRecipePrefix))
                {
                    continue;
                }

                if (ProducesExcludedResource(recipe))
                {
                    skipped++;
                    continue;
                }

                if (recipe.recipeUsers == null)
                {
                    recipe.recipeUsers = new List<ThingDef>();
                }

                if (!recipe.recipeUsers.Contains(mineShaft))
                {
                    recipe.recipeUsers.Add(mineShaft);
                    added++;
                }
            }

            if (added > 0)
            {
                // AllRecipes is cached on first access; clear it so the mine shaft rebuilds its
                // bill list including the recipes we just attached.
                AllRecipesCachedField?.SetValue(mineShaft, null);
            }

            Log.Message($"[MO Advanced Mining] Added {added} Mines 2.0 recipe(s) to {MineShaftDefName}, skipped {skipped} duplicate(s).");
        }

        private static bool ProducesExcludedResource(RecipeDef recipe)
        {
            if (recipe.products == null)
            {
                return false;
            }

            foreach (ThingDefCountClass product in recipe.products)
            {
                if (product.thingDef != null && ExcludedProducts.Contains(product.thingDef.defName))
                {
                    return true;
                }
            }

            return false;
        }
    }
}
