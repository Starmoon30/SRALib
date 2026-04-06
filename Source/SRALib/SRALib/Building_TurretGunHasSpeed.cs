using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using RimWorld;
using UnityEngine;
using Verse;
using Verse.AI;
using Verse.Sound;

namespace SRA
{
    public class ModExt_HasSpeedTurret : DefModExtension
    {
        public float speed = 1f;
        public bool noautoattack = false;
    }
    public class Building_TurretGunHasSpeed : Building_Turret
    {
        protected int burstCooldownTicksLeft;

        protected int burstWarmupTicksLeft;

        protected LocalTargetInfo currentTargetInt = LocalTargetInfo.Invalid;

        private bool holdFire;

        private bool burstActivated;

        public Thing gun;

        protected TurretTop top;

        protected CompPowerTrader powerComp;

        protected CompCanBeDormant dormantComp;

        protected CompInitiatable initiatableComp;

        protected CompMannable mannableComp;

        protected CompInteractable interactableComp;

        public CompRefuelable refuelableComp;

        protected Effecter progressBarEffecter;

        protected CompMechPowerCell powerCellComp;

        protected CompHackable hackableComp;

        public float curAngle;

        private const int TryStartShootSomethingIntervalTicks = 15;

        //public static Material ForcedTargetLineMat = MaterialPool.MatFrom(GenDraw.LineTexPath, ShaderDatabase.Transparent, new Color(1f, 0.5f, 0.5f));

        public bool Active
        {
            get
            {
                if ((powerComp == null || powerComp.PowerOn) && (dormantComp == null || dormantComp.Awake) && (initiatableComp == null || initiatableComp.Initiated) && (interactableComp == null || burstActivated) && (powerCellComp == null || !powerCellComp.depleted))
                {
                    if (hackableComp != null)
                    {
                        return !hackableComp.IsHacked;
                    }

                    return true;
                }

                return false;
            }
        }

        public CompEquippable GunCompEq => gun.TryGetComp<CompEquippable>();

        public override LocalTargetInfo CurrentTarget => currentTargetInt;

        private bool WarmingUp => burstWarmupTicksLeft > 0;

        public override Verb AttackVerb => GunCompEq.PrimaryVerb;

        public bool IsMannable => mannableComp != null;

        private bool PlayerControlled
        {
            get
            {
                if ((base.Faction == Faction.OfPlayer || MannedByColonist) && !MannedByNonColonist)
                {
                    return !IsActivable;
                }

                return false;
            }
        }

        protected virtual bool CanSetForcedTarget
        {
            get
            {
                return true;
            }
        }

        private bool CanToggleHoldFire => PlayerControlled;

        private bool IsMortar => def.building.IsMortar;

        private bool IsMortarOrProjectileFliesOverhead
        {
            get
            {
                if (!AttackVerb.ProjectileFliesOverhead())
                {
                    return IsMortar;
                }

                return true;
            }
        }

        private bool IsActivable => interactableComp != null;

        protected virtual bool HideForceTargetGizmo => false;

        public TurretTop Top => top;

        public ModExt_HasSpeedTurret speedTurretExt => def.GetModExtension<ModExt_HasSpeedTurret>();

        public float rotateSpeed => speedTurretExt?.speed ?? 1f;

        public bool noautoattack => speedTurretExt?.noautoattack ?? false;

        public Vector3 turretOrientation => Vector3.forward.RotatedBy(curAngle);

        public float deltaAngle
        {
            get
            {
                if (!currentTargetInt.IsValid)
                {
                    return 0f;
                }

                return Vector3.SignedAngle(turretOrientation, (currentTargetInt.CenterVector3 - DrawPos).Yto0(), Vector3.up);
            }
        }

        private bool CanExtractShell
        {
            get
            {
                if (!PlayerControlled)
                {
                    return false;
                }

                return gun.TryGetComp<CompChangeableProjectile>()?.Loaded ?? false;
            }
        }

        private bool MannedByColonist
        {
            get
            {
                if (mannableComp != null && mannableComp.ManningPawn != null)
                {
                    return mannableComp.ManningPawn.Faction == Faction.OfPlayer;
                }

                return false;
            }
        }

        private bool MannedByNonColonist
        {
            get
            {
                if (mannableComp != null && mannableComp.ManningPawn != null)
                {
                    return mannableComp.ManningPawn.Faction != Faction.OfPlayer;
                }

                return false;
            }
        }

        public Building_TurretGunHasSpeed()
        {
            top = new TurretTop(this);
        }

        public override void PostMake()
        {
            base.PostMake();
            burstCooldownTicksLeft = def.building.turretInitialCooldownTime.SecondsToTicks();
            MakeGun();
        }

        public override void SpawnSetup(Map map, bool respawningAfterLoad)
        {
            base.SpawnSetup(map, respawningAfterLoad);
            dormantComp = GetComp<CompCanBeDormant>();
            initiatableComp = GetComp<CompInitiatable>();
            powerComp = GetComp<CompPowerTrader>();
            mannableComp = GetComp<CompMannable>();
            interactableComp = GetComp<CompInteractable>();
            refuelableComp = GetComp<CompRefuelable>();
            powerCellComp = GetComp<CompMechPowerCell>();
            hackableComp = GetComp<CompHackable>();
            if (!respawningAfterLoad)
            {
                top.SetRotationFromOrientation();
                curAngle = top.CurRotation;
            }
        }

        public override void DeSpawn(DestroyMode mode = DestroyMode.Vanish)
        {
            base.DeSpawn(mode);
            ResetCurrentTarget();
            progressBarEffecter?.Cleanup();
        }

        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_Values.Look(ref burstCooldownTicksLeft, "burstCooldownTicksLeft", 0);
            Scribe_Values.Look(ref burstWarmupTicksLeft, "burstWarmupTicksLeft", 0);
            Scribe_TargetInfo.Look(ref currentTargetInt, "currentTarget");
            Scribe_Values.Look(ref holdFire, "holdFire", defaultValue: false);
            Scribe_Values.Look(ref burstActivated, "burstActivated", defaultValue: false);
            Scribe_Values.Look(ref curAngle, "curAngle", 0f);
            Scribe_Deep.Look(ref gun, "gun");
            BackCompatibility.PostExposeData(this);
            if (Scribe.mode == LoadSaveMode.PostLoadInit)
            {
                if (gun == null)
                {
                    Log.Error("Turret had null gun after loading. Recreating.");
                    MakeGun();
                }
                else
                {
                    UpdateGunVerbs();
                }
            }
        }

        public override AcceptanceReport ClaimableBy(Faction by)
        {
            AcceptanceReport result = base.ClaimableBy(by);
            if (!result.Accepted)
            {
                return result;
            }

            if (mannableComp != null && mannableComp.ManningPawn != null)
            {
                return false;
            }

            if (Active && mannableComp == null)
            {
                return false;
            }

            if (((dormantComp != null && !dormantComp.Awake) || (initiatableComp != null && !initiatableComp.Initiated)) && (powerComp == null || powerComp.PowerOn))
            {
                return false;
            }

            return true;
        }

        public override void OrderAttack(LocalTargetInfo targ)
        {
            if (!targ.IsValid)
            {
                if (forcedTarget.IsValid)
                {
                    ResetForcedTarget();
                }

                return;
            }

            if ((targ.Cell - base.Position).LengthHorizontal < AttackVerb.verbProps.EffectiveMinRange(targ, this))
            {
                Messages.Message("MessageTargetBelowMinimumRange".Translate(), this, MessageTypeDefOf.RejectInput, historical: false);
                return;
            }

            if ((targ.Cell - base.Position).LengthHorizontal > AttackVerb.EffectiveRange)
            {
                Messages.Message("MessageTargetBeyondMaximumRange".Translate(), this, MessageTypeDefOf.RejectInput, historical: false);
                return;
            }

            if (forcedTarget != targ)
            {
                forcedTarget = targ;
                if (burstCooldownTicksLeft <= 0)
                {
                    TryStartShootSomething(canBeginBurstImmediately: false);
                }
            }

            if (holdFire)
            {
                Messages.Message("MessageTurretWontFireBecauseHoldFire".Translate(def.label), this, MessageTypeDefOf.RejectInput, historical: false);
            }
        }

        protected override void Tick()
        {
            if (Active && currentTargetInt.IsValid)
            {
                if (burstWarmupTicksLeft == 1 && Mathf.Abs(deltaAngle) > rotateSpeed)
                {
                    burstWarmupTicksLeft++;
                }

                RotateTowardsCurrentTarget();
            }

            base.Tick();
            curAngle = TrimAngle(curAngle);
            if (CanExtractShell && MannedByColonist)
            {
                CompChangeableProjectile compChangeableProjectile = gun.TryGetComp<CompChangeableProjectile>();
                if (!compChangeableProjectile.allowedShellsSettings.AllowedToAccept(compChangeableProjectile.LoadedShell))
                {
                    ExtractShell();
                }
            }

            if (forcedTarget.IsValid && !CanSetForcedTarget)
            {
                ResetForcedTarget();
            }

            if (!CanToggleHoldFire)
            {
                holdFire = false;
            }

            if (forcedTarget.ThingDestroyed)
            {
                ResetForcedTarget();
            }

            if (Active && (mannableComp == null || mannableComp.MannedNow) && !base.IsStunned && base.Spawned)
            {
                GunCompEq.verbTracker.VerbsTick();
                if (AttackVerb.state == VerbState.Bursting)
                {
                    return;
                }

                burstActivated = false;
                if (WarmingUp)
                {
                    burstWarmupTicksLeft--;
                    if (burstWarmupTicksLeft <= 0)
                    {
                        BeginBurst();
                    }
                }
                else
                {
                    if (burstCooldownTicksLeft > 0)
                    {
                        burstCooldownTicksLeft--;
                        if (IsMortar)
                        {
                            if (progressBarEffecter == null)
                            {
                                progressBarEffecter = EffecterDefOf.ProgressBar.Spawn();
                            }

                            progressBarEffecter.EffectTick(this, TargetInfo.Invalid);
                            MoteProgressBar mote = ((SubEffecter_ProgressBar)progressBarEffecter.children[0]).mote;
                            mote.progress = 1f - (float)Math.Max(burstCooldownTicksLeft, 0) / (float)BurstCooldownTime().SecondsToTicks();
                            mote.offsetZ = -0.8f;
                        }
                    }

                    if (burstCooldownTicksLeft <= 0 && this.IsHashIntervalTick(15))
                    {
                        TryStartShootSomething(canBeginBurstImmediately: true);
                    }
                }

                top.TurretTopTick();
            }
            else
            {
                ResetCurrentTarget();
            }
        }

        public void TryActivateBurst()
        {
            burstActivated = true;
            TryStartShootSomething(canBeginBurstImmediately: true);
        }

        public void TryStartShootSomething(bool canBeginBurstImmediately)
        {
            if (progressBarEffecter != null)
            {
                progressBarEffecter.Cleanup();
                progressBarEffecter = null;
            }

            if (!base.Spawned || (holdFire && CanToggleHoldFire) || !AttackVerb.Available())
            {
                ResetCurrentTarget();
                return;
            }

            bool isValid = currentTargetInt.IsValid;
            if (forcedTarget.IsValid)
            {
                currentTargetInt = forcedTarget;
            }
            else
            {
                currentTargetInt = TryFindNewTarget();
            }

            if (!isValid && currentTargetInt.IsValid && def.building.playTargetAcquiredSound)
            {
                SoundDefOf.TurretAcquireTarget.PlayOneShot(new TargetInfo(base.Position, base.Map));
            }

            if (currentTargetInt.IsValid)
            {
                float randomInRange = def.building.turretBurstWarmupTime.RandomInRange;
                if (randomInRange > 0f)
                {
                    burstWarmupTicksLeft = randomInRange.SecondsToTicks();
                }
                else if (canBeginBurstImmediately)
                {
                    BeginBurst();
                }
                else
                {
                    burstWarmupTicksLeft = 1;
                }
            }
            else
            {
                ResetCurrentTarget();
            }
        }

        public virtual LocalTargetInfo TryFindNewTarget()
        {
            IAttackTargetSearcher attackTargetSearcher = TargSearcher();
            Faction faction = attackTargetSearcher.Thing.Faction;
            float range = AttackVerb.verbProps.range;
            if (noautoattack)
            {
                return LocalTargetInfo.Invalid;
            }

            if (Rand.Value < 0.5f && AttackVerb.ProjectileFliesOverhead() && faction.HostileTo(Faction.OfPlayer) && base.Map.listerBuildings.allBuildingsColonist.Where(delegate (Building x)
            {
                float num = AttackVerb.verbProps.EffectiveMinRange(x, this);
                float num2 = x.Position.DistanceToSquared(base.Position);
                return num2 > num * num && num2 < range * range;
            }).TryRandomElement(out var result))
            {
                return result;
            }

            TargetScanFlags targetScanFlags = TargetScanFlags.NeedThreat | TargetScanFlags.NeedAutoTargetable;
            if (!AttackVerb.ProjectileFliesOverhead())
            {
                targetScanFlags |= TargetScanFlags.NeedLOSToAll;
                targetScanFlags |= TargetScanFlags.LOSBlockableByGas;
            }

            if (AttackVerb.IsIncendiary_Ranged())
            {
                targetScanFlags |= TargetScanFlags.NeedNonBurning;
            }

            if (IsMortar)
            {
                targetScanFlags |= TargetScanFlags.NeedNotUnderThickRoof;
            }

            return (Thing)AttackTargetFinderAngle.BestShootTargetFromCurrentPosition(attackTargetSearcher, targetScanFlags, turretOrientation, IsValidTarget);
        }

        private IAttackTargetSearcher TargSearcher()
        {
            if (mannableComp != null && mannableComp.MannedNow)
            {
                return mannableComp.ManningPawn;
            }

            return this;
        }

        private bool IsValidTarget(Thing t)
        {
            if (t is Pawn pawn)
            {
                if (base.Faction == Faction.OfPlayer && pawn.IsPrisoner)
                {
                    return false;
                }

                if (AttackVerb.ProjectileFliesOverhead())
                {
                    RoofDef roofDef = base.Map.roofGrid.RoofAt(t.Position);
                    if (roofDef != null && roofDef.isThickRoof)
                    {
                        return false;
                    }
                }

                if (mannableComp == null)
                {
                    return !GenAI.MachinesLike(base.Faction, pawn);
                }

                if (pawn.RaceProps.Animal && pawn.Faction == Faction.OfPlayer)
                {
                    return false;
                }
            }

            return true;
        }

        protected virtual void BeginBurst()
        {
            AttackVerb.TryStartCastOn(CurrentTarget);
            OnAttackedTarget(CurrentTarget);
        }

        protected virtual void BurstComplete()
        {
            burstCooldownTicksLeft = BurstCooldownTime().SecondsToTicks();
        }

        protected virtual float BurstCooldownTime()
        {
            if (def.building.turretBurstCooldownTime >= 0f)
            {
                return def.building.turretBurstCooldownTime;
            }

            return AttackVerb.verbProps.defaultCooldownTime;
        }

        public override string GetInspectString()
        {
            StringBuilder stringBuilder = new StringBuilder();
            string inspectString = base.GetInspectString();
            if (!inspectString.NullOrEmpty())
            {
                stringBuilder.AppendLine(inspectString);
            }

            if (AttackVerb.verbProps.minRange > 0f)
            {
                stringBuilder.AppendLine("MinimumRange".Translate() + ": " + AttackVerb.verbProps.minRange.ToString("F0"));
            }
            else if (base.Spawned && burstCooldownTicksLeft > 0 && BurstCooldownTime() > 5f)
            {
                stringBuilder.AppendLine("CanFireIn".Translate() + ": " + burstCooldownTicksLeft.ToStringSecondsFromTicks());
            }

            CompChangeableProjectile compChangeableProjectile = gun.TryGetComp<CompChangeableProjectile>();
            if (compChangeableProjectile != null)
            {
                if (compChangeableProjectile.Loaded)
                {
                    stringBuilder.AppendLine("ShellLoaded".Translate(compChangeableProjectile.LoadedShell.LabelCap, compChangeableProjectile.LoadedShell));
                }
                else
                {
                    stringBuilder.AppendLine("ShellNotLoaded".Translate());
                }
            }

            return stringBuilder.ToString().TrimEndNewlines();
        }

        protected override void DrawAt(Vector3 drawLoc, bool flip = false)
        {
            top.CurRotation = curAngle;
            Vector3 drawOffset = Vector3.zero;
            float angleOffset = 0f;
            if (IsMortar)
            {
                EquipmentUtility.Recoil(def.building.turretGunDef, (Verb_LaunchProjectile)AttackVerb, out drawOffset, out angleOffset, top.CurRotation);
            }

            top.DrawTurret(drawLoc, drawOffset, angleOffset);
            base.DrawAt(drawLoc, flip);
        }

        public override void DrawExtraSelectionOverlays()
        {
            base.DrawExtraSelectionOverlays();
            float effectiveRange = AttackVerb.EffectiveRange;
            float num = AttackVerb.verbProps.EffectiveMinRange(allowAdjacentShot: true);
            if (num < effectiveRange)
            {
                if (effectiveRange < 90f)
                {
                    GenDraw.DrawRadiusRing(base.Position, effectiveRange);
                }

                if (num < 90f && num > 0.1f)
                {
                    GenDraw.DrawRadiusRing(base.Position, num);
                }
            }

            if (WarmingUp)
            {
                int degreesWide = (int)((float)burstWarmupTicksLeft * 0.5f);
                GenDraw.DrawAimPie(this, CurrentTarget, degreesWide, (float)def.size.x * 0.5f);
            }

            if (forcedTarget.IsValid && (!forcedTarget.HasThing || forcedTarget.Thing.Spawned))
            {
                Vector3 b = ((!forcedTarget.HasThing) ? forcedTarget.Cell.ToVector3Shifted() : forcedTarget.Thing.TrueCenter());
                Vector3 a = this.TrueCenter();
                b.y = AltitudeLayer.MetaOverlays.AltitudeFor();
                a.y = b.y;
                GenDraw.DrawLineBetween(a, b, Building_TurretGun.ForcedTargetLineMat, 0.2f);
            }
        }

        public override IEnumerable<Gizmo> GetGizmos()
        {
            foreach (Gizmo gizmo in base.GetGizmos())
            {
                yield return gizmo;
            }

            if (CanExtractShell)
            {
                CompChangeableProjectile compChangeableProjectile = gun.TryGetComp<CompChangeableProjectile>();
                Command_Action command_Action = new Command_Action();
                command_Action.defaultLabel = "CommandExtractShell".Translate();
                command_Action.defaultDesc = "CommandExtractShellDesc".Translate();
                command_Action.icon = compChangeableProjectile.LoadedShell.uiIcon;
                command_Action.iconAngle = compChangeableProjectile.LoadedShell.uiIconAngle;
                command_Action.iconOffset = compChangeableProjectile.LoadedShell.uiIconOffset;
                command_Action.iconDrawScale = GenUI.IconDrawScale(compChangeableProjectile.LoadedShell);
                command_Action.action = delegate
                {
                    ExtractShell();
                };
                yield return command_Action;
            }

            CompChangeableProjectile compChangeableProjectile2 = gun.TryGetComp<CompChangeableProjectile>();
            if (compChangeableProjectile2 != null)
            {
                StorageSettings storeSettings = compChangeableProjectile2.GetStoreSettings();
                foreach (Gizmo item in StorageSettingsClipboard.CopyPasteGizmosFor(storeSettings))
                {
                    yield return item;
                }
            }

            if (!HideForceTargetGizmo)
            {
                if (CanSetForcedTarget)
                {
                    Command_VerbTarget command_VerbTarget = new Command_VerbTarget();
                    command_VerbTarget.defaultLabel = "CommandSetForceAttackTarget".Translate();
                    command_VerbTarget.defaultDesc = "CommandSetForceAttackTargetDesc".Translate();
                    command_VerbTarget.icon = ContentFinder<Texture2D>.Get("UI/Commands/Attack");
                    command_VerbTarget.verb = AttackVerb;
                    command_VerbTarget.hotKey = KeyBindingDefOf.Misc4;
                    command_VerbTarget.drawRadius = false;
                    command_VerbTarget.requiresAvailableVerb = false;
                    if (base.Spawned)
                    {
                        float curWeatherMaxRangeCap = base.Map.weatherManager.CurWeatherMaxRangeCap;
                        if (curWeatherMaxRangeCap > 0f && curWeatherMaxRangeCap < AttackVerb.verbProps.minRange)
                        {
                            command_VerbTarget.Disable("CannotFire".Translate() + ": " + base.Map.weatherManager.curWeather.LabelCap);
                        }
                    }

                    yield return command_VerbTarget;
                }

                if (forcedTarget.IsValid)
                {
                    Command_Action command_Action2 = new Command_Action();
                    command_Action2.defaultLabel = "CommandStopForceAttack".Translate();
                    command_Action2.defaultDesc = "CommandStopForceAttackDesc".Translate();
                    command_Action2.icon = ContentFinder<Texture2D>.Get("UI/Commands/Halt");
                    command_Action2.action = delegate
                    {
                        ResetForcedTarget();
                        SoundDefOf.Tick_Low.PlayOneShotOnCamera();
                    };
                    if (!forcedTarget.IsValid)
                    {
                        command_Action2.Disable("CommandStopAttackFailNotForceAttacking".Translate());
                    }

                    command_Action2.hotKey = KeyBindingDefOf.Misc5;
                    yield return command_Action2;
                }
            }

            if (!CanToggleHoldFire)
            {
                yield break;
            }

            Command_Toggle command_Toggle = new Command_Toggle();
            command_Toggle.defaultLabel = "CommandHoldFire".Translate();
            command_Toggle.defaultDesc = "CommandHoldFireDesc".Translate();
            command_Toggle.icon = ContentFinder<Texture2D>.Get("UI/Commands/HoldFire");
            command_Toggle.hotKey = KeyBindingDefOf.Misc6;
            command_Toggle.toggleAction = delegate
            {
                holdFire = !holdFire;
                if (holdFire)
                {
                    ResetForcedTarget();
                }
            };
            command_Toggle.isActive = () => holdFire;
            yield return command_Toggle;
        }

        protected float TrimAngle(float angle)
        {
            return Mathf.Repeat(angle, 360f);
        }

        private void RotateTowardsCurrentTarget()
        {
            float num = deltaAngle;
            if (Mathf.Approximately(num, 0f))
            {
                return;
            }

            float num2 = rotateSpeed;
            curAngle += ((Mathf.Abs(num) > num2) ? (Mathf.Sign(num) * num2) : num);
        }

        private void ExtractShell()
        {
            GenPlace.TryPlaceThing(gun.TryGetComp<CompChangeableProjectile>().RemoveShell(), base.Position, base.Map, ThingPlaceMode.Near);
        }

        private void ResetForcedTarget()
        {
            forcedTarget = LocalTargetInfo.Invalid;
            burstWarmupTicksLeft = 0;
            if (burstCooldownTicksLeft <= 0)
            {
                TryStartShootSomething(canBeginBurstImmediately: false);
            }
        }

        private void ResetCurrentTarget()
        {
            currentTargetInt = LocalTargetInfo.Invalid;
            burstWarmupTicksLeft = 0;
        }

        public void MakeGun()
        {
            gun = ThingMaker.MakeThing(def.building.turretGunDef);
            UpdateGunVerbs();
        }

        private void UpdateGunVerbs()
        {
            List<Verb> allVerbs = gun.TryGetComp<CompEquippable>().AllVerbs;
            for (int i = 0; i < allVerbs.Count; i++)
            {
                Verb verb = allVerbs[i];
                verb.caster = this;
                verb.castCompleteCallback = BurstComplete;
            }
        }
    }
    /// <summary>
    /// 攻击目标查找器（角度优化版）
    /// 提供基于角度优化的攻击目标选择功能
    /// </summary>
    public static class AttackTargetFinderAngle
    {
        // 友军误伤评分偏移量常量
        private const float FriendlyFireScoreOffsetPerHumanlikeOrMechanoid = 18f;  // 每人类或机械族的友军误伤分数偏移
        private const float FriendlyFireScoreOffsetPerAnimal = 7f;                 // 每动物的友军误伤分数偏移
        private const float FriendlyFireScoreOffsetPerNonPawn = 10f;               // 每非pawn单位的友军误伤分数偏移
        private const float FriendlyFireScoreOffsetSelf = 40f;                     // 对自己造成误伤的分数偏移
        // 临时目标列表，用于缓存计算过程中的目标
        private static List<IAttackTarget> tmpTargets = new List<IAttackTarget>(128);

        // 可用射击目标及其分数的列表
        private static List<Pair<IAttackTarget, float>> availableShootingTargets = new List<Pair<IAttackTarget, float>>();

        // 临时存储目标分数的列表
        private static List<float> tmpTargetScores = new List<float>();

        // 临时存储是否可以向目标射击的列表
        private static List<bool> tmpCanShootAtTarget = new List<bool>();
        /// <summary>
        /// 从当前位置寻找最佳射击目标
        /// </summary>
        /// <param name="searcher">搜索者（攻击目标搜索器）</param>
        /// <param name="flags">目标扫描标志</param>
        /// <param name="angle">射击角度</param>
        /// <param name="validator">目标验证器（可选）</param>
        /// <param name="minDistance">最小距离（默认0）</param>
        /// <param name="maxDistance">最大距离（默认9999）</param>
        /// <returns>最佳攻击目标，如果没有则返回null</returns>
        public static IAttackTarget BestShootTargetFromCurrentPosition(
            IAttackTargetSearcher searcher,
            TargetScanFlags flags,
            Vector3 angle,
            Predicate<Thing> validator = null,
            float minDistance = 0f,
            float maxDistance = 9999f)
        {
            // 获取当前有效动词（武器）
            Verb currentEffectiveVerb = searcher.CurrentEffectiveVerb;

            // 检查是否有攻击动词
            if (currentEffectiveVerb == null)
            {
                Log.Error("BestShootTargetFromCurrentPosition with " + searcher.ToStringSafe<IAttackTargetSearcher>() + " who has no attack verb.");
                return null;
            }

            // 计算实际的最小和最大距离，考虑武器的属性
            float actualMinDistance = Mathf.Max(minDistance, currentEffectiveVerb.verbProps.minRange);
            float actualMaxDistance = Mathf.Min(maxDistance, currentEffectiveVerb.verbProps.range);

            // 调用主要的目标查找方法
            return BestAttackTarget(
                searcher,
                flags,
                angle,
                validator,
                actualMinDistance,
                actualMaxDistance,
                default(IntVec3),
                float.MaxValue,
                false);
        }

        /// <summary>
        /// 查找最佳攻击目标（核心方法）
        /// </summary>
        /// <param name="searcher">搜索者</param>
        /// <param name="flags">目标扫描标志</param>
        /// <param name="angle">射击角度</param>
        /// <param name="validator">目标验证器</param>
        /// <param name="minDist">最小距离</param>
        /// <param name="maxDist">最大距离</param>
        /// <param name="locus">搜索中心点</param>
        /// <param name="maxTravelRadiusFromLocus">从中心点的最大移动半径</param>
        /// <param name="canTakeTargetsCloserThanEffectiveMinRange">是否可以攻击比有效最小距离更近的目标</param>
        /// <returns>最佳攻击目标</returns>
        public static IAttackTarget BestAttackTarget(
            IAttackTargetSearcher searcher,
            TargetScanFlags flags,
            Vector3 angle,
            Predicate<Thing> validator = null,
            float minDist = 0f,
            float maxDist = 9999f,
            IntVec3 locus = default(IntVec3),
            float maxTravelRadiusFromLocus = float.MaxValue,
            bool canTakeTargetsCloserThanEffectiveMinRange = true)
        {
            // 获取搜索者的Thing对象和当前有效动词
            Thing searcherThing = searcher.Thing;
            Verb verb = searcher.CurrentEffectiveVerb;

            // 验证攻击动词是否存在
            if (verb == null)
            {
                Log.Error("BestAttackTarget with " + searcher.ToStringSafe<IAttackTargetSearcher>() + " who has no attack verb.");
                return null;
            }

            // 初始化各种标志和参数
            bool onlyTargetMachines = verb.IsEMP();  // 是否只瞄准机械单位（EMP武器）
            float minDistSquared = minDist * minDist;  // 最小距离的平方（用于距离比较优化）

            // 计算从搜索中心点的最大距离平方
            float maxLocusDist = maxTravelRadiusFromLocus + verb.verbProps.range;
            float maxLocusDistSquared = maxLocusDist * maxLocusDist;

            // LOS（视线）验证器，用于检查是否被烟雾阻挡
            Predicate<IntVec3> losValidator = null;
            if ((flags & TargetScanFlags.LOSBlockableByGas) > TargetScanFlags.None)
            {
                losValidator = (IntVec3 vec3) => !vec3.AnyGas(searcherThing.Map, GasType.BlindSmoke);
            }

            // 获取潜在目标列表
            tmpTargets.Clear();
            tmpTargets.AddRange(searcherThing.Map.attackTargetsCache.GetPotentialTargetsFor(searcher));

            // 移除非战斗人员（根据标志）
            tmpTargets.RemoveAll(t => ShouldIgnoreNoncombatant(searcherThing, t, flags));

            // 内部验证器函数
            bool InnerValidator(IAttackTarget target, Predicate<IntVec3> losValidator)
            {
                Thing targetThing = target.Thing;
                if (target == searcher)
                {
                    return false;
                }

                if (minDistSquared > 0f && (float)(searcherThing.Position - targetThing.Position).LengthHorizontalSquared < minDistSquared)
                {
                    return false;
                }

                if (!canTakeTargetsCloserThanEffectiveMinRange)
                {
                    float num3 = verb.verbProps.EffectiveMinRange(targetThing, searcherThing);
                    if (num3 > 0f && (float)(searcherThing.Position - targetThing.Position).LengthHorizontalSquared < num3 * num3)
                    {
                        return false;
                    }
                }

                if (maxTravelRadiusFromLocus < 9999f && (float)(targetThing.Position - locus).LengthHorizontalSquared > maxLocusDistSquared)
                {
                    return false;
                }

                if (!searcherThing.HostileTo(targetThing))
                {
                    return false;
                }

                if (validator != null && !validator(targetThing))
                {
                    return false;
                }


                if ((flags & TargetScanFlags.NeedNotUnderThickRoof) != 0)
                {
                    RoofDef roof = targetThing.Position.GetRoof(targetThing.Map);
                    if (roof != null && roof.isThickRoof)
                    {
                        return false;
                    }
                }

                if ((flags & TargetScanFlags.NeedLOSToAll) != 0)
                {
                    if (losValidator != null && (!losValidator(searcherThing.Position) || !losValidator(targetThing.Position)))
                    {
                        return false;
                    }

                    if (!searcherThing.CanSee(targetThing))
                    {
                        if (target is Pawn)
                        {
                            if ((flags & TargetScanFlags.NeedLOSToPawns) != 0)
                            {
                                return false;
                            }
                        }
                        else if ((flags & TargetScanFlags.NeedLOSToNonPawns) != 0)
                        {
                            return false;
                        }
                    }
                }

                if (((flags & TargetScanFlags.NeedThreat) != 0 || (flags & TargetScanFlags.NeedAutoTargetable) != 0) && target.ThreatDisabled(searcher))
                {
                    return false;
                }

                if ((flags & TargetScanFlags.NeedAutoTargetable) != 0 && !AttackTargetFinder.IsAutoTargetable(target))
                {
                    return false;
                }

                if ((flags & TargetScanFlags.NeedActiveThreat) != 0 && !GenHostility.IsActiveThreatTo(target, searcher.Thing.Faction))
                {
                    return false;
                }

                Pawn pawn = target as Pawn;
                if (onlyTargetMachines && pawn != null && pawn.RaceProps.IsFlesh)
                {
                    return false;
                }

                if ((flags & TargetScanFlags.NeedNonBurning) != 0 && targetThing.IsBurning())
                {
                    return false;
                }

                if (searcherThing.def.race != null && (int)searcherThing.def.race.intelligence >= 2)
                {
                    CompExplosive compExplosive = targetThing.TryGetComp<CompExplosive>();
                    if (compExplosive != null && compExplosive.wickStarted)
                    {
                        return false;
                    }
                }

                // 距离验证
                if (!targetThing.Position.InHorDistOf(searcherThing.Position, maxDist))
                    return false;

                // 最小距离验证
                if (!canTakeTargetsCloserThanEffectiveMinRange &&
                    (float)(searcherThing.Position - targetThing.Position).LengthHorizontalSquared < minDistSquared)
                    return false;

                // 中心点距离验证
                if (locus.IsValid &&
                    (float)(locus - targetThing.Position).LengthHorizontalSquared > maxLocusDistSquared)
                    return false;

                // 自定义验证器
                if (validator != null && !validator(targetThing))
                    return false;

                return true;
            }

            // 检查是否有可以直接射击的目标
            bool hasDirectShootTarget = false;
            for (int i = 0; i < tmpTargets.Count; i++)
            {
                IAttackTarget attackTarget = tmpTargets[i];
                if (attackTarget.Thing.Position.InHorDistOf(searcherThing.Position, maxDist) &&
                    InnerValidator(attackTarget, losValidator) &&
                    CanShootAtFromCurrentPosition(attackTarget, searcher, verb))
                {
                    hasDirectShootTarget = true;
                    break;
                }
            }

            IAttackTarget bestTarget;

            if (hasDirectShootTarget)
            {
                // 如果有可以直接射击的目标，使用基于分数的随机选择
                tmpTargets.RemoveAll(x => !x.Thing.Position.InHorDistOf(searcherThing.Position, maxDist) || !InnerValidator(x, losValidator));
                bestTarget = GetRandomShootingTargetByScore(tmpTargets, searcher, verb, angle);
            }
            else
            {
                // 否则使用最近的目标选择策略
                bool needReachableIfCantHit = (flags & TargetScanFlags.NeedReachableIfCantHitFromMyPos) > TargetScanFlags.None;
                bool needReachable = (flags & TargetScanFlags.NeedReachable) > TargetScanFlags.None;

                Predicate<Thing> reachableValidator;
                if (!needReachableIfCantHit || needReachable)
                {
                    reachableValidator = (Thing t) => InnerValidator((IAttackTarget)t, losValidator);
                }
                else
                {
                    reachableValidator = (Thing t) => InnerValidator((IAttackTarget)t, losValidator) &&
                                                     CanShootAtFromCurrentPosition((IAttackTarget)t, searcher, verb);
                }

                bestTarget = (IAttackTarget)GenClosest.ClosestThing_Global(
                    searcherThing.Position,
                    tmpTargets,
                    maxDist,
                    reachableValidator,
                    null,
                    false);
            }

            tmpTargets.Clear();
            return bestTarget;
        }
        /// <summary>
        /// 检查是否应该忽略非战斗人员
        /// </summary>
        private static bool ShouldIgnoreNoncombatant(Thing searcherThing, IAttackTarget target, TargetScanFlags flags)
        {
            // 只对Pawn类型的目标进行判断
            if (!(target is Pawn pawn))
                return false;

            // 如果是战斗人员，不忽略
            if (pawn.IsCombatant())
                return false;

            // 如果设置了忽略非战斗人员标志，则忽略
            if ((flags & TargetScanFlags.IgnoreNonCombatants) > TargetScanFlags.None)
                return true;

            // 如果看不到非战斗人员，则忽略
            return !GenSight.LineOfSightToThing(searcherThing.Position, pawn, searcherThing.Map, false, null);
        }

        /// <summary>
        /// 检查是否可以从当前位置射击目标
        /// </summary>
        private static bool CanShootAtFromCurrentPosition(IAttackTarget target, IAttackTargetSearcher searcher, Verb verb)
        {
            return verb != null && verb.CanHitTargetFrom(searcher.Thing.Position, target.Thing);
        }

        /// <summary>
        /// 通过权重随机获取射击目标
        /// </summary>
        private static IAttackTarget GetRandomShootingTargetByScore(List<IAttackTarget> targets, IAttackTargetSearcher searcher, Verb verb, Vector3 angle)
        {
            var availableTargets = GetAvailableShootingTargetsByScore(targets, searcher, verb, angle);
            if (availableTargets.TryRandomElementByWeight(x => x.Second, out Pair<IAttackTarget, float> result))
            {
                return result.First;
            }
            return null;
        }

        /// <summary>
        /// 获取可用射击目标及其分数的列表
        /// </summary>
        private static List<Pair<IAttackTarget, float>> GetAvailableShootingTargetsByScore(
            List<IAttackTarget> rawTargets,
            IAttackTargetSearcher searcher,
            Verb verb,
            Vector3 angle)
        {
            availableShootingTargets.Clear();

            if (rawTargets.Count == 0)
                return availableShootingTargets;

            // 初始化临时列表
            tmpTargetScores.Clear();
            tmpCanShootAtTarget.Clear();

            float highestScore = float.MinValue;
            IAttackTarget bestTarget = null;

            // 第一轮遍历：计算基础分数并标记可射击目标
            for (int i = 0; i < rawTargets.Count; i++)
            {
                tmpTargetScores.Add(float.MinValue);
                tmpCanShootAtTarget.Add(false);

                // 跳过搜索者自身
                if (rawTargets[i] == searcher)
                    continue;

                // 检查是否可以射击
                bool canShoot = CanShootAtFromCurrentPosition(rawTargets[i], searcher, verb);
                tmpCanShootAtTarget[i] = canShoot;

                if (canShoot)
                {
                    // 计算射击目标分数
                    float score = GetShootingTargetScore(rawTargets[i], searcher, verb, angle);
                    tmpTargetScores[i] = score;

                    // 更新最佳目标
                    if (bestTarget == null || score > highestScore)
                    {
                        bestTarget = rawTargets[i];
                        highestScore = score;
                    }
                }
            }

            // 构建可用目标列表
            for (int j = 0; j < rawTargets.Count; j++)
            {
                if (rawTargets[j] != searcher && tmpCanShootAtTarget[j])
                {
                    availableShootingTargets.Add(new Pair<IAttackTarget, float>(rawTargets[j], tmpTargetScores[j]));
                }
            }

            return availableShootingTargets;
        }

        /// <summary>
        /// 计算射击目标分数（核心评分算法）
        /// </summary>
        private static float GetShootingTargetScore(IAttackTarget target, IAttackTargetSearcher searcher, Verb verb, Vector3 angle)
        {
            float score = 60f;  // 基础分数

            // 距离因素：越近分数越高（最多40分）
            float distance = (target.Thing.Position - searcher.Thing.Position).LengthHorizontal;
            score -= Mathf.Min(distance, 40f);

            // 目标正在瞄准自己：加分
            if (target.TargetCurrentlyAimingAt == searcher.Thing)
                score += 10f;

            // 最近攻击目标：加分（如果最近攻击过这个目标）
            if (searcher.LastAttackedTarget == target.Thing && Find.TickManager.TicksGame - searcher.LastAttackTargetTick <= 300)
                score += 40f;

            // 掩体因素：目标有掩体保护则减分
            float blockChance = CoverUtility.CalculateOverallBlockChance(target.Thing.Position, searcher.Thing.Position, searcher.Thing.Map);
            score -= blockChance * 10f;

            // Pawn特定因素
            if (target is Pawn pawnTarget)
            {
                // 非战斗人员减分
                score -= NonCombatantScore(pawnTarget);

                // 远程攻击目标特殊处理
                if (verb.verbProps.ai_TargetHasRangedAttackScoreOffset != 0f &&
                    pawnTarget.CurrentEffectiveVerb != null &&
                    pawnTarget.CurrentEffectiveVerb.verbProps.Ranged)
                {
                    score += verb.verbProps.ai_TargetHasRangedAttackScoreOffset;
                }

                // 倒地目标大幅减分
                if (pawnTarget.Downed)
                    score -= 50f;
            }

            // 友军误伤因素
            score += FriendlyFireBlastRadiusTargetScoreOffset(target, searcher, verb);
            score += FriendlyFireConeTargetScoreOffset(target, searcher, verb);

            // 角度因素：计算与理想角度的偏差
            Vector3 targetDirection = (target.Thing.DrawPos - searcher.Thing.DrawPos).Yto0();
            float angleDeviation = Vector3.Angle(angle, targetDirection);

            // 防止除零错误
            if (angleDeviation < 0.1f)
                angleDeviation = 0.1f;

            // 最终分数计算：考虑目标优先级因子和角度偏差
            float finalScore = score * target.TargetPriorityFactor / angleDeviation;

            // 确保返回正数
            return Mathf.Max(finalScore, 0.01f);
        }

        /// <summary>
        /// 计算非战斗人员分数
        /// </summary>
        private static float NonCombatantScore(Thing target)
        {
            if (!(target is Pawn pawn))
                return 0f;

            if (!pawn.IsCombatant())
                return 50f;  // 非战斗人员大幅减分

            if (pawn.DevelopmentalStage.Juvenile())
                return 25f;  // 未成年人中等减分

            return 0f;  // 战斗成年人不减分
        }

        /// <summary>
        /// 计算爆炸半径内的友军误伤分数偏移
        /// </summary>
        private static float FriendlyFireBlastRadiusTargetScoreOffset(IAttackTarget target, IAttackTargetSearcher searcher, Verb verb)
        {
            // 检查是否启用了避免友军误伤半径
            if (verb.verbProps.ai_AvoidFriendlyFireRadius <= 0f)
                return 0f;

            Map map = target.Thing.Map;
            IntVec3 targetPosition = target.Thing.Position;
            int cellCount = GenRadial.NumCellsInRadius(verb.verbProps.ai_AvoidFriendlyFireRadius);
            float friendlyFireScore = 0f;

            // 遍历爆炸半径内的所有单元格
            for (int i = 0; i < cellCount; i++)
            {
                IntVec3 checkCell = targetPosition + GenRadial.RadialPattern[i];

                if (!checkCell.InBounds(map))
                    continue;

                bool hasLineOfSight = true;
                List<Thing> thingsInCell = checkCell.GetThingList(map);

                // 检查单元格内的所有物体
                for (int j = 0; j < thingsInCell.Count; j++)
                {
                    Thing thing = thingsInCell[j];

                    // 只关心攻击目标且不是当前目标
                    if (!(thing is IAttackTarget) || thing == target)
                        continue;

                    // 检查视线（只检查一次）
                    if (hasLineOfSight)
                    {
                        if (!GenSight.LineOfSight(targetPosition, checkCell, map, true, null, 0, 0))
                            break;  // 没有视线，跳过这个单元格

                        hasLineOfSight = false;
                    }

                    // 计算误伤分数
                    float hitScore;
                    if (thing == searcher)
                        hitScore = FriendlyFireScoreOffsetSelf;  // 击中自己
                    else if (!(thing is Pawn))
                        hitScore = FriendlyFireScoreOffsetPerNonPawn;  // 非Pawn物体
                    else if (thing.def.race.Animal)
                        hitScore = FriendlyFireScoreOffsetPerAnimal;  // 动物
                    else
                        hitScore = FriendlyFireScoreOffsetPerHumanlikeOrMechanoid;  // 人类或机械族

                    // 根据敌对关系调整分数
                    if (!searcher.Thing.HostileTo(thing))
                        friendlyFireScore -= hitScore;  // 友军：减分
                    else
                        friendlyFireScore += hitScore * 0.6f;  // 敌军：小幅加分
                }
            }

            return friendlyFireScore;
        }

        /// <summary>
        /// 计算锥形范围内的友军误伤分数偏移
        /// </summary>
        private static float FriendlyFireConeTargetScoreOffset(IAttackTarget target, IAttackTargetSearcher searcher, Verb verb)
        {
            // 只对Pawn类型的搜索者进行计算
            if (!(searcher.Thing is Pawn searcherPawn))
                return 0f;

            // 检查智能等级
            if (searcherPawn.RaceProps.intelligence < Intelligence.ToolUser)
                return 0f;

            // 机械族不计算锥形误伤
            if (searcherPawn.RaceProps.IsMechanoid)
                return 0f;

            // 只处理射击类动词
            if (!(verb is Verb_Shoot shootVerb))
                return 0f;

            ThingDef projectileDef = shootVerb.verbProps.defaultProjectile;
            if (projectileDef == null)
                return 0f;

            // 高空飞行的抛射物不计算锥形误伤
            if (projectileDef.projectile.flyOverhead)
                return 0f;

            Map map = searcherPawn.Map;

            // 获取射击报告
            ShotReport report = ShotReport.HitReportFor(searcherPawn, verb, (Thing)target);

            // 计算强制失误半径
            float forcedMissRadius = Mathf.Max(
                VerbUtility.CalculateAdjustedForcedMiss(verb.verbProps.ForcedMissRadius, report.ShootLine.Dest - report.ShootLine.Source),
                1.5f);

            // 获取可能被误伤的所有单元格
            IEnumerable<IntVec3> potentialHitCells =
                from dest in GenRadial.RadialCellsAround(report.ShootLine.Dest, forcedMissRadius, true)
                where dest.InBounds(map)
                select new ShootLine(report.ShootLine.Source, dest)
                into line
                from pos in line.Points().Concat(line.Dest).TakeWhile(pos => pos.CanBeSeenOverFast(map))
                select pos;

            potentialHitCells = potentialHitCells.Distinct();

            float coneFriendlyFireScore = 0f;

            // 计算锥形范围内的误伤分数
            foreach (IntVec3 cell in potentialHitCells)
            {
                float interceptChance = VerbUtility.InterceptChanceFactorFromDistance(report.ShootLine.Source.ToVector3Shifted(), cell);

                if (interceptChance <= 0f)
                    continue;

                List<Thing> thingsInCell = cell.GetThingList(map);

                for (int i = 0; i < thingsInCell.Count; i++)
                {
                    Thing thing = thingsInCell[i];

                    if (!(thing is IAttackTarget) || thing == target)
                        continue;

                    // 计算误伤分数
                    float hitScore;
                    if (thing == searcher)
                        hitScore = FriendlyFireScoreOffsetSelf;
                    else if (!(thing is Pawn))
                        hitScore = FriendlyFireScoreOffsetPerNonPawn;
                    else if (thing.def.race.Animal)
                        hitScore = FriendlyFireScoreOffsetPerAnimal;
                    else
                        hitScore = FriendlyFireScoreOffsetPerHumanlikeOrMechanoid;

                    // 根据拦截概率和敌对关系调整分数
                    hitScore *= interceptChance;
                    if (!searcher.Thing.HostileTo(thing))
                        hitScore = -hitScore;  // 友军：减分
                    else
                        hitScore *= 0.6f;  // 敌军：小幅加分

                    coneFriendlyFireScore += hitScore;
                }
            }

            return coneFriendlyFireScore;
        }
    }
}
