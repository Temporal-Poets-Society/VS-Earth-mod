using System;

namespace EarthMap.Core
{
    /// <summary>
    /// The companions' terrain plan of a world's model (pads and expansions, EarthWorldGen.TerrainPlanning), published once per
    /// model. Vanilla sets a world's generators up again for /wgen regen (WgenCommands.RegenChunks calls TriggerInitWorldGen),
    /// but a model's plan is sealed after its first set-up: a companion registering its sites again would find planning
    /// closed (and stop the server), and the columns already generated were made with the plan as it is.
    /// </summary>
    public static class CompanionPlan
    {
        /// <summary>
        /// Runs <paramref name="planning"/> (the companions register their pads and expansions) and seals the model's plan,
        /// true; false, without running it, when the model's plan is sealed already (a later set-up of the same world).
        /// </summary>
        public static bool PublishOnce(TerrainModel model, Action<TerrainModel> planning)
        {
            if (model == null) throw new ArgumentNullException(nameof(model));
            if (model.PlanSealed) return false;
            planning?.Invoke(model);
            model.SealTerrainReservations();
            return true;
        }
    }
}
