using System;
using System.Collections.Generic;
using RimWorld;
using Verse;
using Verse.AI;
using Verse.AI.Group;

namespace Restrain
{
    public class JobDriver_Restrain : JobDriver
    {
        public override bool TryMakePreToilReservations(bool errorOnFailed)
        {
            if (pawn.Reserve(Takee, job, 1, -1, null, errorOnFailed))
            {
                return pawn.Reserve(DropBed, job, DropBed.SleepingSlotsCount, 0, null, errorOnFailed);
            }

            return false;
        }

        private const TargetIndex TakeeIndex = TargetIndex.A;
        private const TargetIndex BedIndex = TargetIndex.B;
        protected Pawn Takee => (Pawn)job.GetTarget(TargetIndex.A).Thing;
        protected Building_Bed DropBed => (Building_Bed) job.GetTarget(TargetIndex.B).Thing;
        protected override IEnumerable<Toil> MakeNewToils()
        {
            Hediff restrainHediff = HediffMaker.MakeHediff(KnockItOffHediffDefOf.Restrain, Takee);

            this.FailOnDestroyedOrNull(TakeeIndex);
            this.FailOnDestroyedOrNull(BedIndex);
            this.FailOn(delegate
            {
                if (job.def.makeTargetPrisoner)
                {
                    if (!DropBed.ForPrisoners)
                    {
                        return true;
                    }
                }

                return false;
            });

            yield return Toils_Bed.ClaimBedIfNonMedical(BedIndex, TakeeIndex);

            AddFinishAction(delegate
            {
                if (job.def.makeTargetPrisoner && Takee.Position != RestUtility.GetBedSleepingSlotPosFor(Takee, DropBed))
                {
                    Takee.ownership.UnclaimBed();
                }
            });


            Toil gotoTakee = new Toil()
            {
                initAction = delegate
                {
                    pawn.pather.StartPath(Takee, PathEndMode.Touch);
                },
                defaultCompleteMode = ToilCompleteMode.PatherArrival
            };
            yield return gotoTakee;

            Toil toilSleep = new Toil()
            {
                initAction = delegate
                {
                    Takee.health.AddHediff(restrainHediff, null, null);
                }
            };
            yield return toilSleep;

            Toil toil = new Toil
            {
                initAction = delegate
                {
                    if (job.def.makeTargetPrisoner)
                    {
                        CheckedMakePrisoner();
                    }
                }
            };
            yield return toil;

            Toil toil2 = Toils_Haul.StartCarryThing(TakeeIndex).FailOnNonMedicalBedNotOwned(BedIndex, TakeeIndex);
            yield return toil2;

            yield return Toils_Goto.GotoThing(BedIndex, PathEndMode.Touch);

            Toil toil3 = new Toil
            {
                initAction = delegate
                {
                    CheckedMakePrisoner();
                    if (Takee.playerSettings == null)
                    {
                        Takee.playerSettings = new Pawn_PlayerSettings(Takee);
                    }
                }
            };

            yield return toil3;
            yield return Toils_Reserve.Release(BedIndex);

            Toil toil4 = new Toil
            {
                initAction = delegate
                {
                    IntVec3 pIntVec3 = DropBed.Position;
                    pawn.carryTracker.TryDropCarriedThing(pIntVec3, ThingPlaceMode.Direct, out Thing _);
                    if (!DropBed.Destroyed && (DropBed.OwnersForReading.Contains(Takee) ||
                                               (DropBed.Medical && DropBed.AnyUnoccupiedSleepingSlot) ||
                                               Takee.ownership == null))
                    {
                        Takee.jobs.Notify_TuckedIntoBed(DropBed);
                        Takee.mindState.Notify_TuckedIntoBed();
                    }

                    if (Takee.IsPrisonerOfColony)
                    {
                        LessonAutoActivator.TeachOpportunity(ConceptDefOf.PrisonerTab, Takee,
                            OpportunityType.GoodToKnow);
                    }

                    Hediff annoyedHediff = HediffMaker.MakeHediff(KnockItOffHediffDefOf.Annoyed, Takee);
                    Takee.health.RemoveHediff(restrainHediff);
                    Takee.health.AddHediff(annoyedHediff);
                },
                defaultCompleteMode = ToilCompleteMode.Instant
            };

            yield return toil4;
        }

        private void CheckedMakePrisoner()
        {
            if (job.def.makeTargetPrisoner)
            {
                Takee.guest.CapturedBy(Faction.OfPlayer, pawn);
                GenGuest.RemoveHealthyPrisonerReleasedThoughts(Takee);
                Takee.GetLord()?.Notify_PawnAttemptArrested(Takee);
                GenClamor.DoClamor(Takee, 10f, ClamorDefOf.Harm);
                QuestUtility.SendQuestTargetSignals(Takee.questTags, "Arrested", Takee.Named("SUBJECT"));

                Takee.guest.Released = false;
                Takee.guest.interactionMode = PrisonerInteractionModeDefOf.AttemptRecruit;
                Takee.guest.resistance = 0;
            }
        }
    }
}
