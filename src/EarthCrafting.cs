using System;
using System.Linq;
using Vintagestory.API.Common;
using Vintagestory.API.Server;

namespace EarthMap
{
    /// <summary>
    /// Decision 0077 (EA-0068, EA-0069): Earth's crafting rules, saved per world (<see cref="EarthParams.CraftingRules"/>).
    /// Earth's extra grid recipes for its geology live in <c>earth:recipes/grid/refractory.json</c> and load like any recipe
    /// file; a world whose rules are 0 (vanilla rock with vanilla ores, every world made before, a non-Earth world) has them
    /// taken out again at WorldReady, before the server builds the asset packet its clients and their handbook get. They
    /// only add options: vanilla's own recipes stay in every world. The volcanic-ash mortar (barrel) needs no switch, since
    /// tephra exists only in ground-column worlds.
    /// </summary>
    public static class EarthCrafting
    {
        /// <summary>The crafting rules new worlds get where Earth decides where bauxite lies.</summary>
        public const int RulesVersion = EarthParams.CraftingRulesVersion;
        /// <summary>The recipe files the rules switch (asset paths in Earth's domain, as the recipe loader names recipes).</summary>
        public static readonly string[] Files = { "recipes/grid/refractory.json" };

        /// <summary>The rules for a new world: on with ground rules 2 and later (bauxite only in warm terra rossa and laterite,
        /// decision 0076) or with Earth's ore maps (they place bauxite by its real districts).</summary>
        public static int RulesFor(int groundColumns, bool regionalOres) => groundColumns >= 2 || regionalOres ? RulesVersion : 0;

        public static bool IsEarthCrafting(GridRecipe r)
            => r?.Name != null && r.Name.Domain == "earth" && Array.IndexOf(Files, r.Name.Path) >= 0;

        /// <summary>Takes Earth's crafting recipes out of a world whose rules are off. Idempotent.</summary>
        public static int Apply(ICoreServerAPI api, EarthParams p, ILogger log)
        {
            try
            {
                var list = api.World.GridRecipes;
                int on = p?.Enabled == true ? p.CraftingRules : 0;
                int n = list.Count(IsEarthCrafting);
                if (on > 0)
                {
                    log.Notification("Earth crafting rules v{0}: {1} grid recipes for Earth's geology (grog, fire clay, refractory bricks without bauxite).", on, n);
                    return 0;
                }
                // The crafting grid looks recipes up through the world's index by ingredient (FastSearchRecipesByIngredient),
                // built from the same recipe objects when the recipes loaded: disable them and take them out of it too, or the
                // server still crafts them although its list and its clients no longer have them (seen on the bench, 14 Nov).
                foreach (var r in list.Where(IsEarthCrafting)) r.Enabled = false;
                foreach (var entry in api.World.FastSearchRecipesByIngredient) entry.Value?.RemoveAll(r => r is GridRecipe g && IsEarthCrafting(g));
                int removed = list.RemoveAll(IsEarthCrafting);
                log.Notification("Earth crafting rules: off in this world (vanilla's recipes only); {0} of Earth's grid recipes left out.", removed);
                return removed;
            }
            catch (Exception e)
            {
                log.Warning("Earth crafting rules: could not sort out Earth's recipes ({0}).", e.Message);
                return 0;
            }
        }

        /// <summary>Earth's grid recipes the crafting grid can still find: enabled and in the world's index by ingredient.</summary>
        public static int Findable(ICoreServerAPI api)
            => api.World.FastSearchRecipesByIngredient.SelectMany(e => e.Value ?? new System.Collections.Generic.List<IRecipeBase>())
                .OfType<GridRecipe>().Where(r => IsEarthCrafting(r) && r.Enabled).Distinct().Count();

        /// <summary>Earth's crafting recipes in the world, and how many of them resolved their output (self-test).</summary>
        public static (int count, int resolved) Census(ICoreServerAPI api)
        {
            var mine = api.World.GridRecipes.Where(IsEarthCrafting).ToList();
            // (the recipe loader registers only recipes that resolved; this checks the outputs again, as the client will)
            return (mine.Count, mine.Count(r => r.Output?.ResolvedItemStack != null && r.ResolvedIngredients != null));
        }
    }
}
