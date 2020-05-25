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
        private const TargetIndex JobTargetPawnIndex = TargetIndex.A;
        private const TargetIndex BedIndex = TargetIndex.B;
        protected Pawn JobTarget => (Pawn) job.GetTarget(TargetIndex.A).Thing;
        protected Building_Bed DropBed => (Building_Bed) job.GetTarget(TargetIndex.B).Thing;

        /// <summary>
        /// Returns if at least one bed is reserve-able;
        /// </summary>
        /// <param name="errorOnFailed"></param>
        /// <returns></returns>
        public override bool TryMakePreToilReservations(bool errorOnFailed)
        {
            if (pawn.Reserve(JobTarget, job, 1, -1, null, errorOnFailed))
            {
                return pawn.Reserve(DropBed, job, DropBed.SleepingSlotsCount, 0, null, errorOnFailed);
            }

            return false;
        }

        /// <summary>
        /// Yields all steps of restraining a person.
        /// </summary>
        /// <returns></returns>
        protected override IEnumerable<Toil> MakeNewToils()
        {
            Hediff restrainHediff = HediffMaker.MakeHediff(KnockItOffHediffDefOf.Restrain, JobTarget);
            Hediff annoyedHediff = HediffMaker.MakeHediff(KnockItOffHediffDefOf.Annoyed, JobTarget);


            SkillRecord meleeSkillRecordJT = JobTarget.skills.GetSkill(SkillDefOf.Melee);
            SkillRecord meleeSkillRecordPawn = pawn.skills.GetSkill(SkillDefOf.Melee);

            bool imprisonCheck = SkillDiffCheck(meleeSkillRecordJT, meleeSkillRecordPawn);

            //If JobTarget is not violent, try to just talk him into calming down.
            if (!imprisonCheck && !JobTarget.InAggroMentalState)
            {
                SkillRecord socialSkillRecordJT = JobTarget.skills.GetSkill(SkillDefOf.Social);
                SkillRecord socialSkillRecordPawn = pawn.skills.GetSkill(SkillDefOf.Social);

                imprisonCheck = SkillDiffCheck(socialSkillRecordJT, socialSkillRecordPawn);
            }

            // Stop the Job is the Pawn or Bed is destroyed or the Bed is no longer available for Prisoners.
            this.FailOnDestroyedOrNull(JobTargetPawnIndex);
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

            yield return Toils_Bed.ClaimBedIfNonMedical(BedIndex, JobTargetPawnIndex);

            //Un-claim bed once toils are done
            AddFinishAction(delegate
            {
                if (job.def.makeTargetPrisoner &&
                    JobTarget.Position != RestUtility.GetBedSleepingSlotPosFor(JobTarget, DropBed))
                {
                    JobTarget.ownership.UnclaimBed();
                }
            });

            //Goto berserk pawn
            Toil gotoTakee = new Toil
            {
                initAction = delegate { pawn.pather.StartPath(JobTarget, PathEndMode.Touch); },
                defaultCompleteMode = ToilCompleteMode.PatherArrival
            };
            yield return gotoTakee;

            //Restrain pawn, by removing his ability to move or handle equipment.
            //Also makes the pawn annoyed.
            Toil toilSleep = new Toil
            {
                initAction = delegate
                {
                    if (imprisonCheck)
                    {
                        Messages.Message($"Successfully restrained {JobTarget.Name}", MessageTypeDefOf.PositiveEvent);
                        JobTarget.health.AddHediff(restrainHediff, null, null);
                        JobTarget.health.AddHediff(annoyedHediff);
                    }
                    else
                    {
                        Messages.Message($"Failed to restrain {JobTarget.Name}", MessageTypeDefOf.NegativeEvent);
                        pawn.jobs.EndCurrentJob(JobCondition.InterruptForced);
                    }
                }
            };
            yield return toilSleep;

            //Imprison pawn
            Toil toil = new Toil
            {
                initAction = delegate
                {
                    if (imprisonCheck)
                    {
                        CheckedMakePrisoner();
                    }
                }
            };
            yield return toil;
            
            //Haul & Carry pawn to prison cell
            Toil toil2 = Toils_Haul.StartCarryThing(JobTargetPawnIndex).FailOnNonMedicalBedNotOwned(BedIndex, JobTargetPawnIndex);
            yield return toil2;

            yield return Toils_Goto.GotoThing(BedIndex, PathEndMode.Touch);

            //Imprison pawn again (the first one sometimes fails)
            Toil toil3 = new Toil
            {
                initAction = delegate
                {
                    if (imprisonCheck)
                    {
                        CheckedMakePrisoner();
                        if (JobTarget.playerSettings == null)
                        {
                            JobTarget.playerSettings = new Pawn_PlayerSettings(JobTarget);
                        }
                    }
                }
            };

            yield return toil3;
            yield return Toils_Reserve.Release(BedIndex);

            //Put Prisoner in bed & remove restrained
            Toil toil4 = new Toil
            {
                initAction = delegate
                {
                    if (imprisonCheck)
                    {
                        IntVec3 pIntVec3 = DropBed.Position;
                        pawn.carryTracker.TryDropCarriedThing(pIntVec3, ThingPlaceMode.Direct, out Thing _);
                        if (!DropBed.Destroyed && (DropBed.OwnersForReading.Contains(JobTarget) ||
                                                   DropBed.Medical && DropBed.AnyUnoccupiedSleepingSlot ||
                                                   JobTarget.ownership == null))
                        {
                            JobTarget.jobs.Notify_TuckedIntoBed(DropBed);
                            JobTarget.mindState.Notify_TuckedIntoBed();
                        }

                        if (JobTarget.IsPrisonerOfColony)
                        {
                            LessonAutoActivator.TeachOpportunity(ConceptDefOf.PrisonerTab, JobTarget,
                                OpportunityType.GoodToKnow);
                        }

                        JobTarget.health.RemoveHediff(restrainHediff);
                    }
                },
                defaultCompleteMode = ToilCompleteMode.Instant
            };

            yield return toil4;
        }

        private bool SkillDiffCheck(SkillRecord jt, SkillRecord pawn)
        {
            Random r = new Random();
            //If Pawn is unable to do violence, he will not attempt it
            if (!pawn.TotallyDisabled)
            {
                //If JobTarget is unable to do violence, he will be overpowered
                if (jt.TotallyDisabled)
                {
                    return true;
                }

                int skillDiff = (jt.Level - pawn.Level);
                int skillDiffPositiv = skillDiff < 0 ? skillDiff * -1 : skillDiff;
                int dice = r.Next(0, 21 + skillDiffPositiv);

                //Pawn > JobTarget
                if (skillDiff < 0)
                {
                    if (dice >= pawn.Level)
                    {
                        return true;
                    }
                }
                //Pawn < JobTarget
                else if (skillDiff > 0)
                {
                    if (dice < pawn.Level)
                    {
                        return true;
                    }
                }
                else
                {
                    if (r.NextDouble() <= 0.5)
                    {
                        return true;
                    }
                }
            }

            return false;
        }
        //Makes pawn a prisoner
        private void CheckedMakePrisoner()
        {
            if (job.def.makeTargetPrisoner)
            {
                JobTarget.guest.CapturedBy(Faction.OfPlayer, pawn);
                GenGuest.RemoveHealthyPrisonerReleasedThoughts(JobTarget);
                JobTarget.GetLord()?.Notify_PawnAttemptArrested(JobTarget);
                GenClamor.DoClamor(JobTarget, 10f, ClamorDefOf.Harm);
                QuestUtility.SendQuestTargetSignals(JobTarget.questTags, "Arrested", JobTarget.Named("SUBJECT"));

                JobTarget.guest.Released = false;
                JobTarget.guest.interactionMode = PrisonerInteractionModeDefOf.AttemptRecruit;
                JobTarget.guest.resistance = 0;
            }
        }
    }
}