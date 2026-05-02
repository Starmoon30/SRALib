using System.Collections.Generic;
using UnityEngine;
using Verse.Sound;
using RimWorld;
using Verse.AI;
using Verse;

//工作组件
namespace SRA
{
    [StaticConstructorOnStartup]
    public class CompPulseElectrode : ThingComp
    {
        public CompProperties_PulseElectrode Props => (CompProperties_PulseElectrode)props;
        public float curTurretAngle;
        public LocalTargetInfo forcedTarget = LocalTargetInfo.Invalid;
        public Thing currentTarget = null;
        public bool isArmed = true;
        private int cooldownTicksLeft = 0;
        private int searchTickLeft = 0;
        private bool initialized = false;
        private int lastValidTargetTick = 0;
        private int currentPostKillDelay = 0;
        private List<ActiveArcMesh> activeArcs = new List<ActiveArcMesh>();
        private Material arcMaterial;
        private Material turretMat;
        private Mesh idleArcMesh;
        private Vector3 idleArcStartPos;
        private int lastIdleArcTick = -999;
        private CompPowerTrader powerComp;
        private CompBreakdownable breakdownComp;
        private static List<KeyValuePair<Thing, float>> targetDistanceCache = new List<KeyValuePair<Thing, float>>();
        public override void PostSpawnSetup(bool respawningAfterLoad)
        {
            base.PostSpawnSetup(respawningAfterLoad);
            powerComp = parent.GetComp<CompPowerTrader>();
            breakdownComp = parent.GetComp<CompBreakdownable>();
            LongEventHandler.ExecuteWhenFinished(() =>
            {
                if (!string.IsNullOrEmpty(Props.lightningMatPath))
                {
                    arcMaterial = MaterialPool.MatFrom(Props.lightningMatPath, ShaderDatabase.MoteGlow);
                }
                if (!string.IsNullOrEmpty(Props.turretTexPath))
                {
                    turretMat = MaterialPool.MatFrom(Props.turretTexPath);
                }
            });
            if (!initialized)
            {
                curTurretAngle = BaseAngle;
                cooldownTicksLeft = Rand.Range(0, 60);
                initialized = true;
            }
        }
        public override void PostDestroy(DestroyMode mode, Map previousMap)
        {
            base.PostDestroy(mode, previousMap);
            ClearAllMeshes();
        }
        private void ClearAllMeshes()
        {
            if (idleArcMesh != null)
            {
                Object.Destroy(idleArcMesh);
                idleArcMesh = null;
            }
            foreach (var arc in activeArcs)
            {
                if (arc.mesh != null) Object.Destroy(arc.mesh);
            }
            activeArcs.Clear();
        }
        public override void PostExposeData()
        {
            base.PostExposeData();
            Scribe_Values.Look(ref initialized, "initialized", false);
            Scribe_Values.Look(ref isArmed, "isArmed", true);
            Scribe_Values.Look(ref curTurretAngle, "curTurretAngle", 0f);
            Scribe_Values.Look(ref cooldownTicksLeft, "cooldownTicksLeft", 0);
            Scribe_Values.Look(ref lastValidTargetTick, "lastValidTargetTick", 0);
            Scribe_Values.Look(ref currentPostKillDelay, "currentPostKillDelay", 0);
            Scribe_TargetInfo.Look(ref forcedTarget, "forcedTarget", LocalTargetInfo.Invalid);
        }
        private bool IsPoweredAndFunctional => (powerComp == null || powerComp.PowerOn) && (breakdownComp == null || !breakdownComp.BrokenDown);
        public float BaseAngle => parent.Rotation.AsAngle + Props.baseRestAngle;
        public Vector3 GetAbsolutePosition()
        {
            return parent.DrawPos + new Vector3(Props.turretOffset.x, 0, Props.turretOffset.y);
        }
        public Vector3 GetArcOriginPosition()
        {
            Vector3 basePos = GetAbsolutePosition();
            Quaternion rot = Quaternion.AngleAxis(curTurretAngle, Vector3.up);
            return basePos + (rot * new Vector3(Props.arcStartOffset.x, 0, Props.arcStartOffset.y));
        }
        public void ResetTarget()
        {
            currentTarget = null;
            forcedTarget = LocalTargetInfo.Invalid;
            currentPostKillDelay = 0;
        }
        public void SetForcedTarget(LocalTargetInfo targ)
        {
            forcedTarget = targ;
            currentTarget = targ.Thing;
            currentPostKillDelay = 0;
        }
        public override void CompTick()
        {
            base.CompTick();
            if (!parent.Spawned || Props == null) return;
            UpdateVisualArcs();
            if (!isArmed || !IsPoweredAndFunctional)
            {
                ResetTarget();
                RotateTowards(BaseAngle);
                return;
            }
            if (cooldownTicksLeft > 0)
            {
                cooldownTicksLeft--;
                return;
            }
            UpdateTargeting();
            if (currentTarget != null || (forcedTarget.IsValid && !forcedTarget.HasThing))
            {
                Vector3 targetPos = currentTarget != null ? currentTarget.DrawPos : forcedTarget.Cell.ToVector3Shifted();
                float targetAngle = (targetPos - GetAbsolutePosition()).Yto0().AngleFlat();
                RotateTowards(targetAngle);
                lastValidTargetTick = Find.TickManager.TicksGame;
                if (Mathf.Abs(Mathf.DeltaAngle(curTurretAngle, targetAngle)) <= 5f)
                {
                    FireAt(currentTarget != null ? new LocalTargetInfo(currentTarget) : forcedTarget);
                    cooldownTicksLeft = Props.cooldownTicks;
                }
            }
            else
            {
                if (currentPostKillDelay <= 0 && Find.TickManager.TicksGame - lastValidTargetTick > 120)
                {
                    RotateTowards(BaseAngle);
                }
            }
        }
        private void UpdateTargeting()
        {
            if (forcedTarget.IsValid)
            {
                bool invalid = false;
                if (forcedTarget.HasThing)
                {
                    Thing t = forcedTarget.Thing;
                    invalid = t.Destroyed || !t.Spawned || (t is Pawn p && p.Dead) || !IsTargetValid(t.Position);
                    if (!invalid && Props.requireLineOfSight && !GenSight.LineOfSight(parent.Position, t.Position, parent.Map, true))
                    {
                        invalid = true;
                    }
                }
                else
                {
                    invalid = !IsTargetValid(forcedTarget.Cell);
                    if (!invalid && Props.requireLineOfSight && !GenSight.LineOfSight(parent.Position, forcedTarget.Cell, parent.Map, true))
                    {
                        invalid = true;
                    }
                }
                if (invalid)
                {
                    if (currentTarget != null) currentPostKillDelay = Props.postKillDelayTicks;
                    ResetTarget();
                }
                else currentTarget = forcedTarget.Thing;
                return;
            }
            if (currentTarget != null)
            {
                bool invalid = currentTarget.Destroyed || !currentTarget.Spawned || !IsTargetValid(currentTarget.Position) || (currentTarget is Pawn p && (p.Dead || p.Downed));
                if (!invalid && Props.requireLineOfSight && !GenSight.LineOfSight(parent.Position, currentTarget.Position, parent.Map, true))
                {
                    invalid = true;
                }
                if (invalid)
                {
                    currentPostKillDelay = Props.postKillDelayTicks;
                    currentTarget = null;
                }
            }
            if (currentTarget == null)
            {
                if (currentPostKillDelay > 0)
                {
                    currentPostKillDelay--;
                    return;
                }
                if (--searchTickLeft <= 0)
                {
                    searchTickLeft = 15;
                    currentTarget = FindBestTarget();
                }
            }
        }
        private bool IsTargetValid(IntVec3 cell)
        {
            float distSq = cell.DistanceToSquared(parent.Position);
            return distSq <= Props.range * Props.range && distSq >= Props.minRange * Props.minRange && !cell.Roofed(parent.Map);
        }
        private Thing FindBestTarget()
        {
            if (parent.Faction == null) return null;
            float rangeSq = Props.range * Props.range;
            float minRangeSq = Props.minRange * Props.minRange;
            targetDistanceCache.Clear();
            foreach (IAttackTarget target in parent.Map.attackTargetsCache.TargetsHostileToFaction(parent.Faction))
            {
                Thing t = target.Thing;
                if (t is Pawn p && (p.Dead || p.Downed)) continue;
                if (t.Position.Fogged(parent.Map) || t.Position.Roofed(parent.Map)) continue;
                float distSq = t.Position.DistanceToSquared(parent.Position);
                if (distSq <= rangeSq && distSq >= minRangeSq)
                {
                    targetDistanceCache.Add(new KeyValuePair<Thing, float>(t, distSq));
                }
            }
            if (targetDistanceCache.Count == 0) return null;
            targetDistanceCache.Sort((a, b) => a.Value.CompareTo(b.Value));
            foreach (var kvp in targetDistanceCache)
            {
                if (!Props.requireLineOfSight || GenSight.LineOfSight(parent.Position, kvp.Key.Position, parent.Map, true))
                {
                    return kvp.Key;
                }
            }
            return null;
        }
        private void RotateTowards(float angle)
        {
            float delta = Mathf.DeltaAngle(curTurretAngle, angle);
            curTurretAngle += Mathf.Sign(delta) * Mathf.Min(Mathf.Abs(delta), Props.turnSpeed);
        }
        private void FireAt(LocalTargetInfo target)
        {
            Map map = parent.Map;
            if (map == null) return;
            Vector3 originPos = GetArcOriginPosition();
            Vector3 endPos = target.HasThing ? target.Thing.DrawPos : target.Cell.ToVector3Shifted();
            IntVec3 centerCell = target.HasThing ? target.Thing.Position : target.Cell;
            DamageDef damDef = Props.damageDef ?? DamageDefOf.Flame;
            float armorPen = Props.armorPenetration >= 0f ? Props.armorPenetration : damDef.defaultArmorPenetration;
            List<Thing> ignored = new List<Thing>();
            if (target.HasThing && target.Thing.Spawned)
            {
                Thing t = target.Thing;
                Pawn victim = t as Pawn;
                ignored.Add(t);
                if (Props.empDamageAmount > 0) t.TakeDamage(new DamageInfo(DamageDefOf.EMP, Props.empDamageAmount, -1, -1, parent));
                if (t.Spawned)
                {
                    DamageWorker.DamageResult res = t.TakeDamage(new DamageInfo(damDef, Props.damageAmount, armorPen, -1, parent));
                    if (victim != null && !victim.Dead && victim.RaceProps.IsFlesh && res != null && res.hediffs != null)
                    {
                        foreach (var h in res.hediffs)
                        {
                            if (h is Hediff_Injury injury)
                            {
                                injury.TryGetComp<HediffComp_GetsPermanent>()?.SetPainCategory(PainCategory.HighPain);
                            }
                        }
                    }
                }
                if (victim != null && victim.Dead && victim.Corpse != null)
                {
                    if (Props.dessicateCorpse)
                    {
                        CompRottable rotComp = victim.Corpse.GetComp<CompRottable>();
                        if (rotComp != null) rotComp.RotProgress = 1000000f;
                    }
                    if (Props.igniteCorpseSize > 0f) victim.Corpse.TryAttachFire(Props.igniteCorpseSize, parent);
                    ignored.Add(victim.Corpse);
                }
            }
            GenExplosion.DoExplosion(centerCell, map, Props.explosionRadius, damDef, parent, (int)Props.damageAmount, armorPen, weapon: parent.def, ignoredThings: ignored);
            if (Props.empDamageAmount > 0)
            {
                GenExplosion.DoExplosion(centerCell, map, Props.explosionRadius, DamageDefOf.EMP, parent, (int)Props.empDamageAmount, weapon: parent.def, ignoredThings: ignored);
            }
            float effectRadius = Props.explosionRadius > 0f ? Props.explosionRadius : 1.9f;
            IEnumerable<IntVec3> cells = GenRadial.RadialCellsAround(centerCell, effectRadius, true);
            foreach (IntVec3 c in cells)
            {
                if (!c.InBounds(map)) continue;
                List<Thing> list = c.GetThingList(map);
                for (int i = list.Count - 1; i >= 0; i--)
                {
                    Thing t = list[i];
                    if (t is Pawn p && !p.Dead)
                    {
                        if (Props.organDamages != null)
                        {
                            foreach (var rule in Props.organDamages)
                            {
                                if (rule.tag == null || rule.damageAmount <= 0) continue;
                                foreach (var part in p.RaceProps.body.GetPartsWithTag(rule.tag))
                                {
                                    ApplyDirectOrganDamage(p, part, rule.damageDef ?? DamageDefOf.Burn, rule.damageAmount);
                                }
                            }
                        }
                    }
                    else if (t is Corpse corpse && !corpse.Destroyed && !ignored.Contains(corpse))
                    {
                        if (Props.igniteCorpseSize > 0f)
                        {
                            if (corpse.Position.InBounds(map)) FireUtility.TryStartFireIn(corpse.Position, map, Props.igniteCorpseSize, parent);
                            corpse.TryAttachFire(Props.igniteCorpseSize, parent);
                        }
                    }
                }
            }
            Mesh dummy = null;
            try { WeatherEvent_LightningStrike.DoStrike(centerCell, map, ref dummy); } catch { }
            Vector3 railDir = Quaternion.AngleAxis(curTurretAngle, Vector3.up) * Vector3.forward;
            Vector3 railPerp = new Vector3(-railDir.z, 0, railDir.x);
            float spacing = Props.arcSpacing / 2f;
            Mesh m1 = PulseArcMeshMaker.GenerateArcMesh(originPos + railPerp * spacing, endPos, Props.perturbAmp, Props.perturbFreq, Props.arcThickness);
            Mesh m2 = PulseArcMeshMaker.GenerateArcMesh(originPos - railPerp * spacing, endPos, Props.perturbAmp, Props.perturbFreq, Props.arcThickness);
            activeArcs.Add(new ActiveArcMesh(m1, originPos + railPerp * spacing));
            activeArcs.Add(new ActiveArcMesh(m2, originPos - railPerp * spacing));
            Props.fireSound?.PlayOneShot(new TargetInfo(parent.Position, map));
        }
        private void ApplyDirectOrganDamage(Pawn pawn, BodyPartRecord part, DamageDef def, float amount)
        {
            if (pawn.health.hediffSet.PartIsMissing(part)) return;
            bool isSolid = part.def.IsSolid(part, pawn.health.hediffSet.hediffs);
            HediffDef hDef = (isSolid ? def.hediffSolid : def.hediff) ?? DamageDefOf.Burn.hediff;
            if (hDef == null) return;
            Hediff_Injury injury = (Hediff_Injury)HediffMaker.MakeHediff(hDef, pawn, part);
            injury.Severity = amount;
            pawn.health.AddHediff(injury, part, null);
            if (pawn.RaceProps.IsFlesh)
            {
                var cp = injury.TryGetComp<HediffComp_GetsPermanent>();
                if (cp != null)
                {
                    cp.IsPermanent = true;
                    cp.SetPainCategory(PainCategory.HighPain);
                }
            }
        }
        private void UpdateVisualArcs()
        {
            if (activeArcs == null) activeArcs = new List<ActiveArcMesh>();
            for (int i = activeArcs.Count - 1; i >= 0; i--)
            {
                activeArcs[i].ageTicks++;
                if (activeArcs[i].ageTicks > Props.arcDurationTicks)
                {
                    if (activeArcs[i].mesh != null) Object.Destroy(activeArcs[i].mesh);
                    activeArcs.RemoveAt(i);
                }
            }
            if (isArmed && IsPoweredAndFunctional && Props.drawIdleArc && Find.TickManager.TicksGame - lastIdleArcTick > 3)
            {
                Vector3 origin = GetArcOriginPosition();
                Vector3 railDir = Quaternion.AngleAxis(curTurretAngle, Vector3.up) * Vector3.forward;
                Vector3 railPerp = new Vector3(-railDir.z, 0, railDir.x);
                float slideOffset = Mathf.Sin(Find.TickManager.TicksGame * 0.05f) * (Props.arcRailLength / 2f);
                Vector3 slideVec = railDir * slideOffset;
                Vector3 startPos = origin + railPerp * (Props.arcSpacing / 2f) + slideVec;
                Vector3 endPos = origin - railPerp * (Props.arcSpacing / 2f) + slideVec;
                PulseArcMeshMaker.UpdateArcMesh(ref idleArcMesh, startPos, endPos, Props.idleArcAmp, Props.idleArcFreq, Props.idleArcThickness);
                idleArcStartPos = startPos;
                lastIdleArcTick = Find.TickManager.TicksGame;
            }
        }
        public override void PostDraw()
        {
            base.PostDraw();
            if (!parent.Spawned || Props == null) return;
            Vector3 basePos = GetAbsolutePosition();
            if (turretMat != null)
            {
                basePos.y = parent.DrawPos.y + 0.041f;
                Graphics.DrawMesh(MeshPool.plane10, Matrix4x4.TRS(basePos, Quaternion.AngleAxis(curTurretAngle - 90f, Vector3.up), new Vector3(Props.turretDrawSize, 1, Props.turretDrawSize)), turretMat, 0);
            }
            float arcAltitude = parent.DrawPos.y + 0.08f;
            if (isArmed && IsPoweredAndFunctional && idleArcMesh != null && arcMaterial != null && !parent.Position.Roofed(parent.Map))
            {
                Vector3 arcRenderPos = idleArcStartPos;
                arcRenderPos.y = arcAltitude;
                Graphics.DrawMesh(idleArcMesh, arcRenderPos, Quaternion.identity, FadedMaterialPool.FadedVersionOf(arcMaterial, 0.6f), 0);
            }
            if (activeArcs != null)
            {
                for (int i = 0; i < activeArcs.Count; i++)
                {
                    var arc = activeArcs[i];
                    float alpha = 1f - (float)arc.ageTicks / Props.arcDurationTicks;
                    Vector3 arcRenderPos = arc.renderStartPos;
                    arcRenderPos.y = arcAltitude + 0.01f + (i * 0.001f);
                    Graphics.DrawMesh(arc.mesh, arcRenderPos, Quaternion.identity, FadedMaterialPool.FadedVersionOf(arcMaterial, alpha), 0);
                }
            }
        }
        public override IEnumerable<Gizmo> CompGetGizmosExtra()
        {
            foreach (Gizmo g in base.CompGetGizmosExtra()) yield return g;
            if (parent.Faction == Faction.OfPlayer) yield return new Gizmo_PulseElectrodeController(this);
        }
    }
}