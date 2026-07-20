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
        private const string AdvancedMiningResearchDefName = "MinesAutomated_ResearchProjectDef_minecraft";

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

        // Mines 2.0 code-generates its "Mine for ..." recipes gated behind its own research
        // (MinesAutomated_ResearchProjectDef_minecraft, relabeled "Advanced Mining"). Re-point specific
        // resources to more fitting Medieval Overhaul research so they unlock where players expect:
        //   coal / salt  (DankPyon_Coal/Salt)     -> DankPyon_Mining   (basic; matches MO's own ungated
        //                                             mine-shaft coal/salt bills, available at Mining)
        //   raw mithril  (DankPyon_PlasteelOre)   -> DankPyon_Plasteel (MO's "Mithril" research, which
        //                                             also gates smelting raw mithril into ingots)
        // Applied whether or not the recipe is bridged to the mine shaft. Done in code because Mines 2.0
        // builds these recipes at startup and blocks XML PatchOperations on them.
        private static readonly Dictionary<string, string> ResearchByProduct = new Dictionary<string, string>
        {
            { "DankPyon_Coal", "DankPyon_Mining" },
            { "DankPyon_Salt", "DankPyon_Mining" },
            { "DankPyon_PlasteelOre", "DankPyon_Plasteel" },
        };

        // Remove the Mines 2.0 ability to mine these resources entirely: detach the recipe from every
        // building and clear its research unlock, so it can't be crafted anywhere and doesn't show on
        // any research card. Eltex (Vanilla Psycasts Expanded) shouldn't be auto-mineable - it's meant
        // to come from eltex ore veins / meteors, not a mine-shaft bill.
        private static readonly HashSet<string> RemovedProducts = new HashSet<string>
        {
            "VPE_Eltex",
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
            int reGated = 0;
            int removed = 0;
            foreach (RecipeDef recipe in DefDatabase<RecipeDef>.AllDefsListForReading)
            {
                if (recipe.defName == null || !recipe.defName.StartsWith(MinesRecipePrefix))
                {
                    continue;
                }

                // Fully remove the ability to mine certain resources (e.g. eltex): detach the recipe
                // from every building and clear its research unlock so it's inert.
                if (Produces(recipe, RemovedProducts))
                {
                    recipe.recipeUsers = new List<ThingDef>();
                    recipe.researchPrerequisite = null;
                    recipe.researchPrerequisites?.Clear();
                    removed++;
                    continue;
                }

                // Move mapped resources (coal/salt -> Mining, raw mithril -> Mithril) off the
                // "Advanced Mining" research, regardless of whether this recipe is bridged below.
                reGated += RepointResearch(recipe);

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

            Log.Message($"[MO Advanced Mining] Added {added} Mines 2.0 recipe(s) to {MineShaftDefName}, skipped {skipped} duplicate(s), re-gated {reGated} recipe(s) to fitting research, removed {removed} recipe(s).");
        }

        // If this recipe produces a mapped resource, repoint its research prerequisite (from Mines 2.0's
        // "Advanced Mining") to the mapped Medieval Overhaul research. Returns 1 if changed, else 0.
        private static int RepointResearch(RecipeDef recipe)
        {
            if (recipe.products == null)
            {
                return 0;
            }

            foreach (ThingDefCountClass product in recipe.products)
            {
                if (product.thingDef == null
                    || !ResearchByProduct.TryGetValue(product.thingDef.defName, out string researchDefName))
                {
                    continue;
                }

                ResearchProjectDef research = DefDatabase<ResearchProjectDef>.GetNamedSilentFail(researchDefName);
                if (research == null)
                {
                    continue;
                }

                recipe.researchPrerequisite = research;
                recipe.researchPrerequisites?.RemoveAll(
                    r => r == null || r.defName == AdvancedMiningResearchDefName);
                return 1;
            }

            return 0;
        }

        private static bool ProducesExcludedResource(RecipeDef recipe)
        {
            return Produces(recipe, ExcludedProducts);
        }

        private static bool Produces(RecipeDef recipe, HashSet<string> products)
        {
            if (recipe.products == null)
            {
                return false;
            }

            foreach (ThingDefCountClass product in recipe.products)
            {
                if (product.thingDef != null && products.Contains(product.thingDef.defName))
                {
                    return true;
                }
            }

            return false;
        }
    }
}
