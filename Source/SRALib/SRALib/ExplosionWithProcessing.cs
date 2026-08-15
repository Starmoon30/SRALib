using System;
using System.Collections.Generic;
using RimWorld;
using UnityEngine;
using Verse;
using Verse.Sound;

namespace SRA
{
    public abstract class ExplosionNotifyEffect : IExposable
    {
        // 仅处理爆炸实际影响格内的目标。当前效果在逐格爆炸结算阶段执行，而不是在原版 Notify_Explosion 扫描阶段执行。
        public bool onlyAffectedCells = true;

        // 跳过 ignoredThings 中的目标，保持与爆炸伤害过滤一致。
        public bool skipIgnoredThings = true;

        public virtual void Apply(ExplosionWithProcessing explosion, Thing thing)
        {
        }

        public virtual void ExposeData()
        {
            Scribe_Values.Look(ref onlyAffectedCells, "onlyAffectedCells", true);
            Scribe_Values.Look(ref skipIgnoredThings, "skipIgnoredThings", true);
        }

        protected bool CanApplyTo(ExplosionWithProcessing explosion, Thing thing)
        {
            if (explosion == null || thing == null || !thing.Spawned)
            {
                return false;
            }

            if (skipIgnoredThings && explosion.IsIgnoredForProcessing(thing))
            {
                return false;
            }

            return !onlyAffectedCells || explosion.WillAffectCell(thing.Position);
        }
    }

    public class ExplosionNotifyEffect_Hediff : ExplosionNotifyEffect
    {
        // 直接指定要施加的 hediff；留空时用 damageDef 或爆炸 DamageDef 推导伤口 hediff。
        public HediffDef hediffDef;

        // 用于推导伤口类型的 DamageDef；留空时使用爆炸自身的 DamageDef。
        public DamageDef damageDef;

        // 通过原版 BodyPartTagDef 指定器官来源，例如 ConsciousnessSource、BloodPumpingSource。
        public List<BodyPartTagDef> capacitySourceTags = new List<BodyPartTagDef>();

        // 通过能力名映射到原版能力来源标签，例如 Consciousness、BloodPumping。
        public List<PawnCapacityDef> capacities = new List<PawnCapacityDef>();

        // 无器官过滤时作为全身 hediff 施加；如果最终是伤口，则回退到核心部位。
        public bool applyToWholeBody = true;

        // 对所有匹配器官施加；关闭时按覆盖率加权随机选一个。
        public bool applyToAllMatchingParts = false;

        // 没有匹配器官时是否跳过；关闭后会回退为全身/核心部位。
        public bool skipPawnWhenNoTargetPart = true;

        // 是否允许已死亡 Pawn 接收 hediff。
        public bool applyToDeadPawns = false;

        // 固定严重度；未启用 severityFromExplosionDamage 时使用。
        public float fixedSeverity = 1f;

        // 启用后严重度 = 爆炸在目标格的伤害值 * severityPerDamage + severityOffset。
        public bool severityFromExplosionDamage = false;

        // 爆炸伤害到 hediff 严重度的倍率。
        public float severityPerDamage = 1f;

        // 使用爆炸伤害计算严重度时的额外偏移。
        public float severityOffset = 0f;

        // 固定持续 tick；小于 0 时不主动设置持续时间。
        public int fixedDurationTicks = -1;

        // 启用后持续时间 = 爆炸在目标格的伤害值 * durationTicksPerDamage + durationTicksOffset。
        public bool durationFromExplosionDamage = false;

        // 爆炸伤害到持续 tick 的倍率。
        public float durationTicksPerDamage = 0f;

        // 使用爆炸伤害计算持续时间时的额外偏移。
        public int durationTicksOffset = 0;

        // 伤口是否可摧毁身体部位。
        public bool destroysBodyParts = true;

        public override void Apply(ExplosionWithProcessing explosion, Thing thing)
        {
            if (!CanApplyTo(explosion, thing))
            {
                return;
            }

            Pawn pawn = thing as Pawn;
            if (pawn == null || pawn.health == null || pawn.RaceProps?.body == null)
            {
                return;
            }

            if (pawn.Dead && !applyToDeadPawns)
            {
                return;
            }

            int damageAmount = explosion.GetDamageAmountAt(thing.Position);
            float severity = ResolveSeverity(damageAmount);
            if (severity <= 0f)
            {
                return;
            }

            int durationTicks = ResolveDurationTicks(damageAmount);
            DirectHediffApplicationUtility.Apply(new DirectHediffApplicationRequest
            {
                pawn = pawn,
                hediffDef = hediffDef,
                damageDef = damageDef ?? explosion.damType,
                severity = severity,
                durationTicks = durationTicks,
                capacitySourceTags = capacitySourceTags,
                capacities = capacities,
                applyToWholeBody = applyToWholeBody,
                applyToAllMatchingParts = applyToAllMatchingParts,
                skipPawnWhenNoTargetPart = skipPawnWhenNoTargetPart,
                applyToDeadPawns = applyToDeadPawns,
                destroysBodyParts = destroysBodyParts,
                sourceDef = explosion.weapon,
                sourceLabel = explosion.weapon?.label ?? "",
                recordDamageResult = false
            });
        }

        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_Defs.Look(ref hediffDef, "hediffDef");
            Scribe_Defs.Look(ref damageDef, "damageDef");
            Scribe_Collections.Look(ref capacitySourceTags, "capacitySourceTags", LookMode.Def);
            Scribe_Collections.Look(ref capacities, "capacities", LookMode.Def);
            Scribe_Values.Look(ref applyToWholeBody, "applyToWholeBody", true);
            Scribe_Values.Look(ref applyToAllMatchingParts, "applyToAllMatchingParts", false);
            Scribe_Values.Look(ref skipPawnWhenNoTargetPart, "skipPawnWhenNoTargetPart", true);
            Scribe_Values.Look(ref applyToDeadPawns, "applyToDeadPawns", false);
            Scribe_Values.Look(ref fixedSeverity, "fixedSeverity", 1f);
            Scribe_Values.Look(ref severityFromExplosionDamage, "severityFromExplosionDamage", false);
            Scribe_Values.Look(ref severityPerDamage, "severityPerDamage", 1f);
            Scribe_Values.Look(ref severityOffset, "severityOffset", 0f);
            Scribe_Values.Look(ref fixedDurationTicks, "fixedDurationTicks", -1);
            Scribe_Values.Look(ref durationFromExplosionDamage, "durationFromExplosionDamage", false);
            Scribe_Values.Look(ref durationTicksPerDamage, "durationTicksPerDamage", 0f);
            Scribe_Values.Look(ref durationTicksOffset, "durationTicksOffset", 0);
            Scribe_Values.Look(ref destroysBodyParts, "destroysBodyParts", true);
            if (Scribe.mode == LoadSaveMode.PostLoadInit)
            {
                if (capacitySourceTags == null)
                {
                    capacitySourceTags = new List<BodyPartTagDef>();
                }
                if (capacities == null)
                {
                    capacities = new List<PawnCapacityDef>();
                }
            }
        }

        private float ResolveSeverity(int explosionDamage)
        {
            if (!severityFromExplosionDamage)
            {
                return fixedSeverity;
            }

            return explosionDamage * severityPerDamage + severityOffset;
        }

        private int ResolveDurationTicks(int explosionDamage)
        {
            if (!durationFromExplosionDamage)
            {
                return fixedDurationTicks;
            }

            return Mathf.RoundToInt(explosionDamage * durationTicksPerDamage) + durationTicksOffset;
        }
    }

    public class ExplosionWithProcessing : Explosion
    {
        public List<ExplosionNotifyEffect> preNotifyEffects = new List<ExplosionNotifyEffect>();
        public List<ExplosionNotifyEffect> postNotifyEffects = new List<ExplosionNotifyEffect>();

        private int processingStartTick;
        private List<IntVec3> processingCellsToAffect;
        private List<Thing> processingDamagedThings;
        private List<Thing> processingIgnoredThings;
        private List<Thing> processingPreEffectedThings;
        private List<Thing> processingPostEffectedThings;
        private HashSet<IntVec3> processingAddedCellsAffectedOnlyByDamage;
        private HashSet<IntVec3> processingAffectedCells;
        private static readonly HashSet<IntVec3> TmpCells = new HashSet<IntVec3>();
        private static readonly List<Thing> TmpThings = new List<Thing>();

        public bool HasNotifyEffects => !preNotifyEffects.NullOrEmpty() || !postNotifyEffects.NullOrEmpty();

        public bool WillAffectCell(IntVec3 cell)
        {
            return processingAffectedCells != null && processingAffectedCells.Contains(cell);
        }

        public bool IsIgnoredForProcessing(Thing thing)
        {
            return processingIgnoredThings != null && processingIgnoredThings.Contains(thing);
        }

        public override void SpawnSetup(Map map, bool respawningAfterLoad)
        {
            base.SpawnSetup(map, respawningAfterLoad);
            if (!respawningAfterLoad && !base.BeingTransportedOnGravship)
            {
                processingCellsToAffect = SimplePool<List<IntVec3>>.Get();
                processingCellsToAffect.Clear();
                processingDamagedThings = SimplePool<List<Thing>>.Get();
                processingDamagedThings.Clear();
                processingPreEffectedThings = SimplePool<List<Thing>>.Get();
                processingPreEffectedThings.Clear();
                processingPostEffectedThings = SimplePool<List<Thing>>.Get();
                processingPostEffectedThings.Clear();
                processingAddedCellsAffectedOnlyByDamage = SimplePool<HashSet<IntVec3>>.Get();
                processingAddedCellsAffectedOnlyByDamage.Clear();
                processingAffectedCells = SimplePool<HashSet<IntVec3>>.Get();
                processingAffectedCells.Clear();
            }
        }

        public override void DeSpawn(DestroyMode mode = DestroyMode.Vanish)
        {
            base.DeSpawn(mode);
            ReturnProcessingCollections();
        }

        public override void StartExplosion(SoundDef explosionSound, List<Thing> ignoredThings)
        {
            if (!base.Spawned)
            {
                Log.Error("Called StartExplosion() on unspawned thing.");
                return;
            }

            EnsureProcessingCollections();
            processingStartTick = Find.TickManager.TicksGame;
            processingIgnoredThings = ignoredThings;
            processingCellsToAffect.Clear();
            processingDamagedThings.Clear();
            processingPreEffectedThings.Clear();
            processingPostEffectedThings.Clear();
            processingAddedCellsAffectedOnlyByDamage.Clear();
            processingAffectedCells.Clear();

            if (!overrideCells.NullOrEmpty())
            {
                processingCellsToAffect.AddRange(overrideCells);
            }
            else
            {
                processingCellsToAffect.AddRange(damType.Worker.ExplosionCellsToHit(this));
            }

            if (applyDamageToExplosionCellsNeighbors)
            {
                AddCellsNeighbors(processingCellsToAffect);
            }

            damType.Worker.ExplosionStart(this, processingCellsToAffect);
            for (int i = 0; i < processingCellsToAffect.Count; i++)
            {
                processingAffectedCells.Add(processingCellsToAffect[i]);
            }

            PlayExplosionSound(explosionSound);
            if (doVisualEffects)
            {
                FleckMaker.WaterSplash(base.Position.ToVector3Shifted(), base.Map, radius * 6f, 20f);
            }

            processingCellsToAffect.Sort((IntVec3 a, IntVec3 b) => GetCellAffectTick(b).CompareTo(GetCellAffectTick(a)));
            NotifyNearbyThingsWithProcessing();
            TrySpawnSingleThing(preExplosionSpawnSingleThingDef);
        }

        protected override void Tick()
        {
            int ticksGame = Find.TickManager.TicksGame;
            int index = processingCellsToAffect.Count - 1;
            while (index >= 0 && ticksGame >= GetCellAffectTick(processingCellsToAffect[index]))
            {
                try
                {
                    AffectCell(processingCellsToAffect[index]);
                }
                catch (Exception ex)
                {
                    Log.Error("Explosion could not affect cell " + processingCellsToAffect[index] + ": " + ex);
                }

                processingCellsToAffect.RemoveAt(index);
                index--;
            }

            if (processingCellsToAffect.Count == 0)
            {
                ExplosionEnded();
                Destroy(DestroyMode.Vanish);
            }
        }

        protected override void ExplosionEnded()
        {
            TrySpawnSingleThing(postExplosionSpawnSingleThingDef);
        }

        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_Collections.Look(ref preNotifyEffects, "preNotifyEffects", LookMode.Deep);
            Scribe_Collections.Look(ref postNotifyEffects, "postNotifyEffects", LookMode.Deep);
            Scribe_Values.Look(ref processingStartTick, "processingStartTick");
            Scribe_Collections.Look(ref processingCellsToAffect, "processingCellsToAffect", LookMode.Value);
            Scribe_Collections.Look(ref processingDamagedThings, "processingDamagedThings", LookMode.Reference);
            Scribe_Collections.Look(ref processingIgnoredThings, "processingIgnoredThings", LookMode.Reference);
            Scribe_Collections.Look(ref processingPreEffectedThings, "processingPreEffectedThings", LookMode.Reference);
            Scribe_Collections.Look(ref processingPostEffectedThings, "processingPostEffectedThings", LookMode.Reference);
            Scribe_Collections.Look(ref processingAddedCellsAffectedOnlyByDamage, "processingAddedCellsAffectedOnlyByDamage", LookMode.Value);
            Scribe_Collections.Look(ref processingAffectedCells, "processingAffectedCells", LookMode.Value);
            if (Scribe.mode == LoadSaveMode.PostLoadInit)
            {
                if (preNotifyEffects == null)
                {
                    preNotifyEffects = new List<ExplosionNotifyEffect>();
                }
                if (postNotifyEffects == null)
                {
                    postNotifyEffects = new List<ExplosionNotifyEffect>();
                }
                if (processingCellsToAffect == null)
                {
                    processingCellsToAffect = new List<IntVec3>();
                }
                if (processingDamagedThings == null)
                {
                    processingDamagedThings = new List<Thing>();
                }
                if (processingPreEffectedThings == null)
                {
                    processingPreEffectedThings = new List<Thing>();
                }
                if (processingPostEffectedThings == null)
                {
                    processingPostEffectedThings = new List<Thing>();
                }
                processingIgnoredThings?.RemoveAll((Thing x) => x == null);
                processingDamagedThings.RemoveAll((Thing x) => x == null);
                processingPreEffectedThings.RemoveAll((Thing x) => x == null);
                processingPostEffectedThings.RemoveAll((Thing x) => x == null);
                if (processingAddedCellsAffectedOnlyByDamage == null)
                {
                    processingAddedCellsAffectedOnlyByDamage = new HashSet<IntVec3>();
                }
                if (processingAffectedCells == null)
                {
                    processingAffectedCells = new HashSet<IntVec3>(processingCellsToAffect);
                }
            }
        }

        private void EnsureProcessingCollections()
        {
            if (processingCellsToAffect == null)
            {
                processingCellsToAffect = SimplePool<List<IntVec3>>.Get();
            }
            if (processingDamagedThings == null)
            {
                processingDamagedThings = SimplePool<List<Thing>>.Get();
            }
            if (processingPreEffectedThings == null)
            {
                processingPreEffectedThings = SimplePool<List<Thing>>.Get();
            }
            if (processingPostEffectedThings == null)
            {
                processingPostEffectedThings = SimplePool<List<Thing>>.Get();
            }
            if (processingAddedCellsAffectedOnlyByDamage == null)
            {
                processingAddedCellsAffectedOnlyByDamage = SimplePool<HashSet<IntVec3>>.Get();
            }
            if (processingAffectedCells == null)
            {
                processingAffectedCells = SimplePool<HashSet<IntVec3>>.Get();
            }
        }

        private void ReturnProcessingCollections()
        {
            if (processingCellsToAffect != null)
            {
                processingCellsToAffect.Clear();
                SimplePool<List<IntVec3>>.Return(processingCellsToAffect);
                processingCellsToAffect = null;
            }

            if (processingDamagedThings != null)
            {
                processingDamagedThings.Clear();
                SimplePool<List<Thing>>.Return(processingDamagedThings);
                processingDamagedThings = null;
            }

            if (processingPreEffectedThings != null)
            {
                processingPreEffectedThings.Clear();
                SimplePool<List<Thing>>.Return(processingPreEffectedThings);
                processingPreEffectedThings = null;
            }

            if (processingPostEffectedThings != null)
            {
                processingPostEffectedThings.Clear();
                SimplePool<List<Thing>>.Return(processingPostEffectedThings);
                processingPostEffectedThings = null;
            }

            if (processingAddedCellsAffectedOnlyByDamage != null)
            {
                processingAddedCellsAffectedOnlyByDamage.Clear();
                SimplePool<HashSet<IntVec3>>.Return(processingAddedCellsAffectedOnlyByDamage);
                processingAddedCellsAffectedOnlyByDamage = null;
            }

            if (processingAffectedCells != null)
            {
                processingAffectedCells.Clear();
                SimplePool<HashSet<IntVec3>>.Return(processingAffectedCells);
                processingAffectedCells = null;
            }
        }

        private int GetCellAffectTick(IntVec3 cell)
        {
            return processingStartTick + (int)((cell - base.Position).LengthHorizontal * 1.5f / propagationSpeed);
        }

        private void NotifyNearbyThingsWithProcessing()
        {
            RegionTraverser.BreadthFirstTraverse(base.Position, base.Map, (Region from, Region to) => true, delegate (Region region)
            {
                List<Thing> allThings = region.ListerThings.AllThings;
                for (int i = allThings.Count - 1; i >= 0; i--)
                {
                    Thing thing = allThings[i];
                    if (!thing.Spawned)
                    {
                        continue;
                    }

                    thing.Notify_Explosion(this);
                }

                return false;
            }, 25, RegionType.Set_Passable);
        }

        private void ApplyEffectsToThing(List<ExplosionNotifyEffect> effects, Thing thing, List<Thing> processedThings)
        {
            if (effects.NullOrEmpty())
            {
                return;
            }

            if (processedThings != null)
            {
                if (processedThings.Contains(thing))
                {
                    return;
                }

                processedThings.Add(thing);
            }

            for (int i = 0; i < effects.Count; i++)
            {
                ExplosionNotifyEffect effect = effects[i];
                if (effect == null)
                {
                    continue;
                }

                try
                {
                    effect.Apply(this, thing);
                }
                catch (Exception ex)
                {
                    Log.Error("Explosion notify effect failed on " + thing.ToStringSafe() + ": " + ex);
                }
            }
        }

        private void ApplyPreEffectsToCell(IntVec3 cell)
        {
            if (preNotifyEffects.NullOrEmpty())
            {
                return;
            }

            GetThingsToAffectCell(cell, TmpThings);
            for (int i = 0; i < TmpThings.Count; i++)
            {
                ApplyEffectsToThing(preNotifyEffects, TmpThings[i], processingPreEffectedThings);
            }
            TmpThings.Clear();
        }

        private void ApplyPostEffectsToNewDamagedThings(int damagedThingsStartIndex)
        {
            if (postNotifyEffects.NullOrEmpty())
            {
                return;
            }

            for (int i = damagedThingsStartIndex; i < processingDamagedThings.Count; i++)
            {
                ApplyEffectsToThing(postNotifyEffects, processingDamagedThings[i], processingPostEffectedThings);
            }
        }

        private void GetThingsToAffectCell(IntVec3 cell, List<Thing> outThings)
        {
            outThings.Clear();
            float maxFullFillAltitude = float.MinValue;
            List<Thing> things = base.Map.thingGrid.ThingsListAt(cell);
            for (int i = 0; i < things.Count; i++)
            {
                Thing thing = things[i];
                if (thing.def.category == ThingCategory.Mote || thing.def.category == ThingCategory.Ethereal)
                {
                    continue;
                }

                if (thing.def.Fillage == FillCategory.Full && thing.def.Altitude > maxFullFillAltitude)
                {
                    maxFullFillAltitude = thing.def.Altitude;
                }
            }

            for (int i = 0; i < things.Count; i++)
            {
                Thing thing = things[i];
                if (thing.def.category != ThingCategory.Mote && thing.def.category != ThingCategory.Ethereal && thing.def.Altitude >= maxFullFillAltitude)
                {
                    outThings.Add(thing);
                }
            }
        }

        private void AffectCell(IntVec3 cell)
        {
            if (!cell.InBounds(base.Map))
            {
                return;
            }

            if (excludeRadius > 0f && cell.DistanceToSquared(base.Position) < excludeRadius * excludeRadius)
            {
                return;
            }

            TerrainDef terrain = cell.GetTerrain(base.Map);
            bool onlyDamage = ShouldCellBeAffectedOnlyByDamage(cell);
            if (!onlyDamage && Rand.Chance(preExplosionSpawnChance) && cell.Walkable(base.Map))
            {
                TrySpawnExplosionThing(preExplosionSpawnThingDef, cell, preExplosionSpawnThingCount);
            }

            ApplyPreEffectsToCell(cell);
            int damagedThingsStartIndex = processingDamagedThings.Count;
            damType.Worker.ExplosionAffectCell(this, cell, processingDamagedThings, processingIgnoredThings, !onlyDamage);
            ApplyPostEffectsToNewDamagedThings(damagedThingsStartIndex);
            if (!onlyDamage)
            {
                if (Rand.Chance(postExplosionSpawnChance) && cell.Walkable(base.Map))
                {
                    ThingDef thingDef = terrain.IsWater ? postExplosionSpawnThingDefWater ?? postExplosionSpawnThingDef : postExplosionSpawnThingDef;
                    TrySpawnExplosionThing(thingDef, cell, postExplosionSpawnThingCount);
                }

                if (postExplosionGasType != null)
                {
                    float gasRadius = postExplosionGasRadiusOverride ?? radius;
                    if (cell.DistanceToSquared(base.Position) <= gasRadius * gasRadius)
                    {
                        GasUtility.AddGas(cell, base.Map, postExplosionGasType.Value, postExplosionGasAmount);
                    }
                }
            }

            float fireChance = chanceToStartFire;
            if (damageFalloff)
            {
                fireChance *= Mathf.Lerp(1f, 0.2f, cell.DistanceTo(base.Position) / radius);
            }

            if (Rand.Chance(fireChance))
            {
                FireUtility.TryStartFireIn(cell, base.Map, Rand.Range(0.1f, 0.925f), instigator, flammabilityChanceCurve);
            }

            if (terrain.temporary && terrain.tempTerrain != null && terrain.tempTerrain.removedByExplosions)
            {
                base.Map.terrainGrid.RemoveTempTerrain(cell, false, false);
            }
        }

        private void TrySpawnSingleThing(ThingDef thingDef)
        {
            if (thingDef == null)
            {
                return;
            }

            CellRect cellRect = base.Position.RectAbout(thingDef.Size);
            bool invalidTerrain = false;
            if (thingDef.terrainAffordanceNeeded != null)
            {
                foreach (IntVec3 cell in cellRect)
                {
                    if (!cell.GetAffordances(base.Map).Contains(thingDef.terrainAffordanceNeeded))
                    {
                        invalidTerrain = true;
                        break;
                    }
                }
            }

            if (!invalidTerrain)
            {
                TrySpawnExplosionThing(thingDef, base.Position, 1);
            }
        }

        private void TrySpawnExplosionThing(ThingDef thingDef, IntVec3 cell, int count)
        {
            if (thingDef == null)
            {
                return;
            }

            if (thingDef.IsFilth)
            {
                FilthMaker.TryMakeFilth(cell, base.Map, thingDef, count, FilthSourceFlags.None, true);
                return;
            }

            if (GenSpawn.TrySpawn(thingDef, cell, base.Map, out Thing thing, WipeMode.Vanish, true))
            {
                thing.stackCount = count;
                thing.TryGetComp<CompReleaseGas>()?.StartRelease();
            }
        }

        private void PlayExplosionSound(SoundDef explosionSound)
        {
            if (!doSoundEffects)
            {
                return;
            }

            bool hasExplicitSound = Prefs.DevMode ? explosionSound != null : !explosionSound.NullOrUndefined();
            if (hasExplicitSound)
            {
                explosionSound.PlayOneShot(new TargetInfo(base.Position, base.Map, false));
                return;
            }

            damType.soundExplosion.PlayOneShot(new TargetInfo(base.Position, base.Map, false));
        }

        private void AddCellsNeighbors(List<IntVec3> cells)
        {
            TmpCells.Clear();
            processingAddedCellsAffectedOnlyByDamage.Clear();
            for (int i = 0; i < cells.Count; i++)
            {
                TmpCells.Add(cells[i]);
            }

            for (int i = 0; i < cells.Count; i++)
            {
                if (!cells[i].Walkable(base.Map))
                {
                    continue;
                }

                for (int j = 0; j < GenAdj.AdjacentCells.Length; j++)
                {
                    IntVec3 adjacent = cells[i] + GenAdj.AdjacentCells[j];
                    if (adjacent.InBounds(base.Map) && TmpCells.Add(adjacent))
                    {
                        processingAddedCellsAffectedOnlyByDamage.Add(adjacent);
                    }
                }
            }

            cells.Clear();
            foreach (IntVec3 cell in TmpCells)
            {
                cells.Add(cell);
            }
            TmpCells.Clear();
        }

        private bool ShouldCellBeAffectedOnlyByDamage(IntVec3 cell)
        {
            return applyDamageToExplosionCellsNeighbors && processingAddedCellsAffectedOnlyByDamage.Contains(cell);
        }
    }

    public static class ExplosionWithProcessingUtility
    {
        public static void DoExplosion(
            IntVec3 center,
            Map map,
            float radius,
            DamageDef damType,
            Thing instigator,
            int damAmount = -1,
            float armorPenetration = -1f,
            SoundDef explosionSound = null,
            ThingDef weapon = null,
            ThingDef projectile = null,
            Thing intendedTarget = null,
            ThingDef postExplosionSpawnThingDef = null,
            float postExplosionSpawnChance = 0f,
            int postExplosionSpawnThingCount = 1,
            GasType? postExplosionGasType = null,
            float? postExplosionGasRadiusOverride = null,
            int postExplosionGasAmount = 255,
            bool applyDamageToExplosionCellsNeighbors = false,
            ThingDef preExplosionSpawnThingDef = null,
            float preExplosionSpawnChance = 0f,
            int preExplosionSpawnThingCount = 1,
            float chanceToStartFire = 0f,
            bool damageFalloff = false,
            float? direction = null,
            List<Thing> ignoredThings = null,
            FloatRange? affectedAngle = null,
            bool doVisualEffects = true,
            float propagationSpeed = 1f,
            float excludeRadius = 0f,
            bool doSoundEffects = true,
            ThingDef postExplosionSpawnThingDefWater = null,
            float screenShakeFactor = 1f,
            SimpleCurve flammabilityChanceCurve = null,
            List<IntVec3> overrideCells = null,
            ThingDef postExplosionSpawnSingleThingDef = null,
            ThingDef preExplosionSpawnSingleThingDef = null,
            List<ExplosionNotifyEffect> preNotifyEffects = null,
            List<ExplosionNotifyEffect> postNotifyEffects = null)
        {
            if (map == null)
            {
                Log.Warning("Tried to do explosion in a null map.");
                return;
            }

            if (damType == null)
            {
                Log.ErrorOnce("Attempted to trigger an explosion without damage def", 91094883);
                return;
            }

            if (damAmount < 0)
            {
                damAmount = damType.defaultDamage;
                armorPenetration = damType.defaultArmorPenetration;
                if (damAmount < 0)
                {
                    Log.ErrorOnce("Attempted to trigger an explosion without defined damage", 91094882);
                    damAmount = 1;
                }
            }

            if (armorPenetration < 0f)
            {
                armorPenetration = damAmount * 0.015f;
            }

            ExplosionWithProcessing explosion = MakeExplosion();
            GenSpawn.Spawn(explosion, center, map, WipeMode.Vanish);
            if (!explosion.Spawned)
            {
                return;
            }

            CalculateNeededLOSToCells(center, map, direction, out IntVec3? needLOSToCell1, out IntVec3? needLOSToCell2);
            explosion.radius = radius;
            explosion.damType = damType;
            explosion.instigator = instigator;
            explosion.damAmount = damAmount;
            explosion.armorPenetration = armorPenetration;
            explosion.weapon = weapon;
            explosion.projectile = projectile;
            explosion.intendedTarget = intendedTarget;
            explosion.preExplosionSpawnThingDef = preExplosionSpawnThingDef;
            explosion.preExplosionSpawnChance = preExplosionSpawnChance;
            explosion.preExplosionSpawnThingCount = preExplosionSpawnThingCount;
            explosion.postExplosionSpawnThingDef = postExplosionSpawnThingDef;
            explosion.postExplosionSpawnThingDefWater = postExplosionSpawnThingDefWater;
            explosion.postExplosionSpawnChance = postExplosionSpawnChance;
            explosion.postExplosionSpawnThingCount = postExplosionSpawnThingCount;
            explosion.postExplosionGasType = postExplosionGasType;
            explosion.postExplosionGasRadiusOverride = postExplosionGasRadiusOverride;
            explosion.postExplosionGasAmount = postExplosionGasAmount;
            explosion.applyDamageToExplosionCellsNeighbors = applyDamageToExplosionCellsNeighbors;
            explosion.chanceToStartFire = chanceToStartFire;
            explosion.damageFalloff = damageFalloff;
            explosion.needLOSToCell1 = needLOSToCell1;
            explosion.needLOSToCell2 = needLOSToCell2;
            explosion.excludeRadius = excludeRadius;
            explosion.affectedAngle = affectedAngle;
            explosion.doSoundEffects = doSoundEffects;
            explosion.screenShakeFactor = screenShakeFactor;
            explosion.flammabilityChanceCurve = flammabilityChanceCurve;
            explosion.doVisualEffects = doVisualEffects;
            explosion.propagationSpeed = propagationSpeed;
            explosion.overrideCells = overrideCells;
            explosion.postExplosionSpawnSingleThingDef = postExplosionSpawnSingleThingDef;
            explosion.preExplosionSpawnSingleThingDef = preExplosionSpawnSingleThingDef;
            explosion.preNotifyEffects = preNotifyEffects ?? new List<ExplosionNotifyEffect>();
            explosion.postNotifyEffects = postNotifyEffects ?? new List<ExplosionNotifyEffect>();
            explosion.StartExplosion(explosionSound, ignoredThings);
        }

        private static ExplosionWithProcessing MakeExplosion()
        {
            ThingDef explosionDef = SRALib_DefOf.SRA_ExplosionWithProcessing ?? ThingDefOf.Explosion;
            ExplosionWithProcessing explosion = ThingMaker.MakeThing(explosionDef) as ExplosionWithProcessing;
            if (explosion != null)
            {
                return explosion;
            }

            Log.ErrorOnce("SRA_ExplosionWithProcessing does not use SRA.ExplosionWithProcessing as thingClass.", 91094884);
            explosion = new ExplosionWithProcessing();
            explosion.def = ThingDefOf.Explosion;
            explosion.SetStuffDirect(null);
            explosion.PostMake();
            explosion.PostPostMake();
            return explosion;
        }

        private static void CalculateNeededLOSToCells(IntVec3 position, Map map, float? direction, out IntVec3? needLOSToCell1, out IntVec3? needLOSToCell2)
        {
            needLOSToCell1 = null;
            needLOSToCell2 = null;
            if (direction == null || position.CanBeSeenOverFast(map))
            {
                return;
            }

            float value = GenMath.PositiveMod(direction.Value, 360f);
            IntVec3 north = position;
            north.z++;
            IntVec3 south = position;
            south.z--;
            IntVec3 west = position;
            west.x--;
            IntVec3 east = position;
            east.x++;

            if (value < 90f)
            {
                TryAssignLosCell(west, map, ref needLOSToCell1);
                TryAssignLosCell(north, map, ref needLOSToCell2);
            }
            else if (value < 180f)
            {
                TryAssignLosCell(north, map, ref needLOSToCell1);
                TryAssignLosCell(east, map, ref needLOSToCell2);
            }
            else if (value < 270f)
            {
                TryAssignLosCell(east, map, ref needLOSToCell1);
                TryAssignLosCell(south, map, ref needLOSToCell2);
            }
            else
            {
                TryAssignLosCell(south, map, ref needLOSToCell1);
                TryAssignLosCell(west, map, ref needLOSToCell2);
            }
        }

        private static void TryAssignLosCell(IntVec3 cell, Map map, ref IntVec3? target)
        {
            if (cell.InBounds(map) && cell.CanBeSeenOverFast(map))
            {
                target = cell;
            }
        }
    }
}
