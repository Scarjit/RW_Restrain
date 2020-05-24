using System.Collections.Generic;
using RimWorld;
using Verse;
using Verse.AI;

namespace Restrain
{
    public class WorkGiver_Restrain : WorkGiver_Scanner
    {
        /// <summary>
        /// Only return pawns, that are in own faction and snapped out.
        /// </summary>
        /// <param name="pawn"></param>
        /// <param name="forced"></param>
        /// <returns></returns>
        public override bool ShouldSkip(Pawn pawn, bool forced = false)
        {
            List<Pawn> pawnList = pawn.Map.mapPawns.SpawnedPawnsInFaction(pawn.Faction);
            foreach (Pawn ppawn in pawnList)
            {
                if (ppawn.InMentalState || ppawn.InAggroMentalState)
                {
                    return false;
                }
            }
            return true;
        }

        /// <summary>
        /// Always allow to restrain
        /// </summary>
        /// <param name="pawn"></param>
        /// <returns></returns>
        public override Danger MaxPathDanger(Pawn pawn)
        {
            return Danger.Deadly;
        }

        /// <summary>
        /// Only check WorkGiver conditions on Free Colonists
        /// </summary>
        /// <param name="pawn"></param>
        /// <returns></returns>
        public override IEnumerable<Thing> PotentialWorkThingsGlobal(Pawn pawn)
        {
            return pawn.Map.mapPawns.FreeColonistsSpawned;
        }

        public override Job JobOnThing(Pawn pawn, Thing t, bool forced = false)
        {
            Pawn flippedOut = t as Pawn;
            Building_Bed bed = RestUtility.FindBedFor(flippedOut, flippedOut, true, false, false);
            if (bed == null)
            {
                return null;
            }

            Job job = JobMaker.MakeJob(Restrain_JobDefOf.Restrain, (LocalTargetInfo) flippedOut,
                (LocalTargetInfo) bed);
            job.count = 1;
            return job;
        }
    }
}