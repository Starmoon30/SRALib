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
        /// <summary>
        /// 炮塔每 tick 最大转动角度，单位为度。
        /// </summary>
        public float speed = 1f;

        /// <summary>
        /// 是否完全禁止炮塔自动索敌；不影响玩家强制指定目标。
        /// </summary>
        public bool noautoattack = false;

        /// <summary>
        /// 是否允许自动攻击已被原版判定为敌对的本方越狱囚犯、叛乱奴隶和逃逸异常实体。
        /// 仅在这些单位实际进入对应逃逸/叛乱状态时生效，普通囚犯、奴隶和被收容实体仍不会成为目标。
        /// </summary>
        public bool autoTargetEscapingCaptives = false;
    }

    public class TauntAttackTargetExtension : DefModExtension
    {
        public float targetPriorityFactor = 1f;
        public bool disabled;
    }

    public class Building_TurretGunHasSpeed : Building_Turret, IAttackTarget, ISustainedShootTurretDriver
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

        // 运行时缓存 gun def 上的多炮管配置，避免每 tick/draw 重复查 mod extension。
        private ModExtension_ShootWithOffset shootWithOffsetExt;

        // 每根炮管各自维护动画状态，下标必须与 ModExtension_ShootWithOffset.offsets 对齐。
        private List<float> barrelRecoilStates;

        private List<int> barrelRecoilTimers;

        private List<int> muzzleFlashTimers;

        private Material barrelMaterial;

        private Material muzzleFlashMaterial;

        // 同轴副武器的定义和运行时状态。副武器不使用主炮的 CompChangeableProjectile，
        // 独立弹仓直接保存到炮塔本体，以便随建筑正常存档和跨地图移动。
        private ModExtension_CoaxialWeapon coaxialWeaponExt;

        private int coaxialCooldownTicksLeft;

        private int coaxialBurstShotsLeft;

        private int coaxialTicksToNextBurstShot;

        private LocalTargetInfo coaxialBurstTarget = LocalTargetInfo.Invalid;

        private int coaxialAmmoCount;

        private bool coaxialAmmoInitialized;

        // 同轴副武器独立于主炮的停火状态。开启后只中止副武器 burst，
        // 不会重置主炮的强制目标、warmup 或冷却。
        private bool coaxialHoldFire;

        private int coaxialNextBarrelIndex;

        private List<float> coaxialBarrelRecoilStates;

        private List<int> coaxialBarrelRecoilTimers;

        private List<int> coaxialMuzzleFlashTimers;

        private Material coaxialBarrelMaterial;

        private Material coaxialMuzzleFlashMaterial;

        private const int TryStartShootSomethingIntervalTicks = 15;

        //public static Material ForcedTargetLineMat = MaterialPool.MatFrom(GenDraw.LineTexPath, ShaderDatabase.Transparent, new Color(1f, 0.5f, 0.5f));
        private TauntAttackTargetExtension AttackTargetProps => def.GetModExtension<TauntAttackTargetExtension>();

        Thing IAttackTarget.Thing => this;

        LocalTargetInfo IAttackTarget.TargetCurrentlyAimingAt => CurrentTarget;

        float IAttackTarget.TargetPriorityFactor => Mathf.Max(0f, AttackTargetProps?.targetPriorityFactor ?? 1f);

        bool IAttackTarget.ThreatDisabled(IAttackTargetSearcher disabledFor)
        {
            if (!Spawned || (AttackTargetProps?.disabled ?? false))
            {
                return true;
            }

            return base.ThreatDisabled(disabledFor);
        }

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

        public LocalTargetInfo SustainedShootCurrentTarget => CurrentTarget;

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

        /// <summary>
        /// 仅玩家可控制且实际配置副武器的炮塔显示独立的副武器停火按钮。
        /// </summary>
        private bool CanToggleCoaxialHoldFire => CanToggleHoldFire && coaxialWeaponExt?.projectile != null;

        private bool IsMortar => def.building.IsMortar;

        private bool CanAcquireTargetsThroughBlockedLOS
        {
            get
            {
                Verb attackVerb = AttackVerb;
                // 原版飞越弹不要求目标 LOS；显式关闭 requireLineOfSight 的 verb 也应获得同样的索敌能力。
                return attackVerb != null && (attackVerb.ProjectileFliesOverhead() || !attackVerb.verbProps.requireLineOfSight);
            }
        }

        private bool IsActivable => interactableComp != null;

        protected virtual bool HideForceTargetGizmo => false;

        public TurretTop Top => top;

        public ModExt_HasSpeedTurret speedTurretExt => def.GetModExtension<ModExt_HasSpeedTurret>();

        public float rotateSpeed => speedTurretExt?.speed ?? 1f;

        public bool noautoattack => speedTurretExt?.noautoattack ?? false;

        /// <summary>
        /// 是否允许自动攻击越狱囚犯、叛乱奴隶和逃逸异常实体。
        /// </summary>
        private bool AutoTargetEscapingCaptives => speedTurretExt?.autoTargetEscapingCaptives ?? false;

        /// <summary>
        /// 当前炮塔是否配置了需要物品装填的独立同轴弹仓。
        /// 该属性供地图缓存和 WorkGiver 使用，不读取主炮 CompChangeableProjectile。
        /// </summary>
        public bool UsesIndependentCoaxialAmmo => coaxialWeaponExt?.ammoThingDef != null;

        /// <summary>
        /// 独立同轴弹仓是否达到自动补给阈值。与原版 CompRefuelable 一致，
        /// 阈值只决定是否派发工作；实际装填会尽量补满弹仓。
        /// </summary>
        public bool NeedsCoaxialAmmoReload => UsesIndependentCoaxialAmmo
            && CoaxialMaxAmmo > 0
            && coaxialAmmoCount < CoaxialMaxAmmo
            && CoaxialAmmoPercent <= CoaxialAutoReloadPercent;

        /// <summary>
        /// 独立同轴弹仓当前储存的可射击次数。
        /// 供原版样式的库存条 Gizmo 读取，始终限制在当前定义的容量范围内。
        /// </summary>
        public int CoaxialAmmoCount => Mathf.Clamp(coaxialAmmoCount, 0, CoaxialMaxAmmo);

        /// <summary>
        /// 独立同轴弹仓的最大可射击次数。
        /// </summary>
        public int CoaxialAmmoCapacity => CoaxialMaxAmmo;

        /// <summary>
        /// 独立同轴弹仓当前库存比例。容量为零时返回零，避免配置不完整时除以零。
        /// </summary>
        private float CoaxialAmmoPercent => CoaxialMaxAmmo > 0 ? (float)CoaxialAmmoCount / CoaxialMaxAmmo : 0f;

        /// <summary>
        /// 独立同轴弹仓消耗的物品定义。
        /// </summary>
        public ThingDef CoaxialAmmoThingDef => coaxialWeaponExt?.ammoThingDef;

        /// <summary>
        /// 使 WorkGiver 可按弹仓空余量决定一次需要搬运多少个弹药物品。
        /// </summary>
        public int CoaxialAmmoItemsNeeded
        {
            get
            {
                if (!NeedsCoaxialAmmoReload)
                {
                    return 0;
                }

                return Mathf.CeilToInt((float)(CoaxialMaxAmmo - coaxialAmmoCount) / CoaxialShotsPerAmmoItem);
            }
        }

        /// <summary>
        /// 供装填 JobDriver 使用的工作时长，始终至少等待一个 tick。
        /// </summary>
        public int CoaxialReloadTicks => Mathf.Max(1, coaxialWeaponExt?.reloadTicks ?? 1);

        private int CoaxialMaxAmmo => Mathf.Max(0, coaxialWeaponExt?.maxAmmo ?? 0);

        private int CoaxialShotsPerAmmoItem => Mathf.Max(1, coaxialWeaponExt?.shotsPerAmmoItem ?? 1);

        private float CoaxialAutoReloadPercent => Mathf.Clamp01(coaxialWeaponExt?.autoReloadPercent ?? 0.3f);

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

            // DeSpawn 会重置内置枪械的全部 Verb。重力飞船飞行复用同一炮塔实例，
            // 不会经过 ExposeData 的读档重绑流程，因此每次生成时都要恢复施法者与完成回调。
            // 否则炮塔飞行一次后将不再调用 BurstComplete，导致外层冷却永远不会重新开始。
            if (gun == null)
            {
                MakeGun();
            }
            else
            {
                UpdateGunVerbs();
            }

            RecacheShootWithOffsetAnimationData();
            RecacheCoaxialWeaponData();
            map.GetComponent<MapComponent_CoaxialWeaponTurrets>().Register(this);
            if (!respawningAfterLoad)
            {
                top.SetRotationFromOrientation();
                curAngle = top.CurRotation;
            }
        }

        public override void DeSpawn(DestroyMode mode = DestroyMode.Vanish)
        {
            Map?.GetComponent<MapComponent_CoaxialWeaponTurrets>()?.Deregister(this);
            ResetGunVerbs();
            base.DeSpawn(mode);
            ResetCurrentTarget();
            progressBarEffecter?.Cleanup();
            progressBarEffecter = null;
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
            Scribe_Collections.Look(ref barrelRecoilStates, "barrelRecoilStates", LookMode.Value);
            Scribe_Collections.Look(ref barrelRecoilTimers, "barrelRecoilTimers", LookMode.Value);
            Scribe_Collections.Look(ref muzzleFlashTimers, "muzzleFlashTimers", LookMode.Value);
            Scribe_Values.Look(ref coaxialCooldownTicksLeft, "coaxialCooldownTicksLeft", 0);
            Scribe_Values.Look(ref coaxialBurstShotsLeft, "coaxialBurstShotsLeft", 0);
            Scribe_Values.Look(ref coaxialTicksToNextBurstShot, "coaxialTicksToNextBurstShot", 0);
            Scribe_TargetInfo.Look(ref coaxialBurstTarget, "coaxialBurstTarget");
            Scribe_Values.Look(ref coaxialAmmoCount, "coaxialAmmoCount", 0);
            Scribe_Values.Look(ref coaxialAmmoInitialized, "coaxialAmmoInitialized", false);
            Scribe_Values.Look(ref coaxialHoldFire, "coaxialHoldFire", false);
            Scribe_Values.Look(ref coaxialNextBarrelIndex, "coaxialNextBarrelIndex", 0);
            Scribe_Collections.Look(ref coaxialBarrelRecoilStates, "coaxialBarrelRecoilStates", LookMode.Value);
            Scribe_Collections.Look(ref coaxialBarrelRecoilTimers, "coaxialBarrelRecoilTimers", LookMode.Value);
            Scribe_Collections.Look(ref coaxialMuzzleFlashTimers, "coaxialMuzzleFlashTimers", LookMode.Value);
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
                RecacheShootWithOffsetAnimationData();
                RecacheCoaxialWeaponData();
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
            Comp_HNGT_GlobalBallisticAttack remoteArtilleryComp = GetComp<Comp_HNGT_GlobalBallisticAttack>();
            bool remoteArtilleryActive = remoteArtilleryComp != null && remoteArtilleryComp.IsFiringInterMap;

            if (!remoteArtilleryActive && Active && currentTargetInt.IsValid)
            {
                if (burstWarmupTicksLeft == 1 && Mathf.Abs(deltaAngle) > rotateSpeed)
                {
                    burstWarmupTicksLeft++;
                }

                RotateTowardsCurrentTarget();
            }

            base.Tick();
            curAngle = TrimAngle(curAngle);
            UpdateShootWithOffsetAnimations();
            UpdateCoaxialWeaponAnimations();
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
                coaxialHoldFire = false;
            }

            if (forcedTarget.ThingDestroyed)
            {
                ResetForcedTarget();
            }

            if (Active && (mannableComp == null || mannableComp.MannedNow) && !base.IsStunned && base.Spawned)
            {
                if (remoteArtilleryActive)
                {
                    CancelCoaxialBurst();
                    GunCompEq.verbTracker.VerbsTick();
                    if (burstCooldownTicksLeft > 0)
                    {
                        burstCooldownTicksLeft--;
                    }

                    remoteArtilleryComp.TickInterMapFireForTurret(this);
                    top.TurretTopTick();
                    return;
                }

                CompSustainedShoot compSustainedShoot = GetGunSustainedShootComp();
                bool sustainedShootStarted;
                bool sustainedShootActive = TickGunSustainedShootComp(compSustainedShoot, out sustainedShootStarted);
                if (!sustainedShootStarted)
                {
                    GunCompEq.verbTracker.VerbsTick();
                    sustainedShootActive = TickGunSustainedShootComp(compSustainedShoot, out sustainedShootStarted);
                }

                // 副武器拥有独立冷却和独立弹药，但严格使用主炮当前锁定的目标与转向。
                // 因此即使主炮正在 burst 或转火，副武器也能按自己的射速持续射击。
                TickCoaxialWeapon();

                if (AttackVerb.state == VerbState.Bursting || sustainedShootActive || sustainedShootStarted)
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
                CancelCoaxialBurst();
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

            // 主炮弹尽时，已装填的独立副武器仍应能借用主炮的索敌规则取得目标。
            // 这不会让副武器拥有第二套索敌逻辑，只是避免主炮不可用时过早返回。
            if (!base.Spawned || (holdFire && CanToggleHoldFire) || (!AttackVerb.Available() && !CanAcquireTargetForCoaxialWeapon()))
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
            if (!CanAcquireTargetsThroughBlockedLOS)
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
                bool isEscapingCaptive = IsEscapingCaptiveOrRebel(pawn);
                if (base.Faction == Faction.OfPlayer && pawn.IsPrisoner && !isEscapingCaptive)
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
                    // 原版 GenAI.MachinesLike 会将本方奴隶视为友方。叛乱时它们已由原版
                    // GenHostility 标记为敌对，开启扩展后应允许无人炮塔正常选择该目标。
                    return isEscapingCaptive || !GenAI.MachinesLike(base.Faction, pawn);
                }

                if (pawn.RaceProps.Animal && pawn.Faction == Faction.OfPlayer)
                {
                    return false;
                }
            }

            return true;
        }

        /// <summary>
        /// 只识别原版已经进入敌对状态的被收容单位。实际敌对性、射程、视线、威胁状态
        /// 与可自动瞄准性仍由 AttackTargetFinderAngle 的标准筛选处理。
        /// </summary>
        private bool IsEscapingCaptiveOrRebel(Pawn pawn)
        {
            if (!AutoTargetEscapingCaptives || base.Faction != Faction.OfPlayer || pawn == null)
            {
                return false;
            }

            if (PrisonBreakUtility.IsPrisonBreaking(pawn) || SlaveRebellionUtility.IsRebelling(pawn))
            {
                return true;
            }

            // 收容实体逃逸时会由 CompHoldingPlatformTarget 标记。仅在 Anomaly 启用时访问，
            // 避免未启用 DLC 的游戏为普通 Pawn 进行额外组件查找。
            if (ModsConfig.AnomalyActive && pawn.RaceProps.IsAnomalyEntity)
            {
                return pawn.TryGetComp<CompHoldingPlatformTarget>()?.isEscaping ?? false;
            }

            return false;
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

        private void RecacheShootWithOffsetAnimationData()
        {
            shootWithOffsetExt = gun?.def.GetModExtension<ModExtension_ShootWithOffset>() ?? def.GetModExtension<ModExtension_ShootWithOffset>();
            barrelMaterial = null;
            muzzleFlashMaterial = null;
            EnsureShootWithOffsetAnimationListSizes();
            if (shootWithOffsetExt == null)
            {
                return;
            }

            // 材质加载放到 LongEvent 完成后执行，避免初始化阶段触发纹理加载时机问题。
            if (!shootWithOffsetExt.barrelTexturePath.NullOrEmpty())
            {
                ModExtension_ShootWithOffset ext = shootWithOffsetExt;
                LongEventHandler.ExecuteWhenFinished(delegate
                {
                    Shader shader = ext.barrelUseGlowShader ? ShaderDatabase.MoteGlow : ShaderDatabase.DefaultShader;
                    barrelMaterial = MaterialPool.MatFrom(ext.barrelTexturePath, shader, ext.barrelColor);
                });
            }

            if (!shootWithOffsetExt.muzzleFlashTexturePath.NullOrEmpty())
            {
                ModExtension_ShootWithOffset ext = shootWithOffsetExt;
                LongEventHandler.ExecuteWhenFinished(delegate
                {
                    Shader shader = ext.muzzleFlashUseGlowShader ? ShaderDatabase.MoteGlow : ShaderDatabase.DefaultShader;
                    muzzleFlashMaterial = MaterialPool.MatFrom(ext.muzzleFlashTexturePath, shader, ext.muzzleFlashColor);
                });
            }
        }

        private void EnsureShootWithOffsetAnimationListSizes()
        {
            int count = shootWithOffsetExt == null ? 0 : shootWithOffsetExt.SlotCount;

            // offsets 数量可能因 XML 修改或读档变化而改变，读档后要把状态列表重新对齐。
            EnsureListSize(ref barrelRecoilStates, count, 0f);
            EnsureListSize(ref barrelRecoilTimers, count, 0);
            EnsureListSize(ref muzzleFlashTimers, count, 0);
        }

        /// <summary>
        /// 读取挂在 turretGunDef 上的同轴武器扩展并恢复其绘制缓存。
        /// 建筑 Def 上的同名扩展仅作为旧式炮塔定义的兜底，优先级低于内置武器 Def。
        /// </summary>
        private void RecacheCoaxialWeaponData()
        {
            coaxialWeaponExt = gun?.def.GetModExtension<ModExtension_CoaxialWeapon>() ?? def.GetModExtension<ModExtension_CoaxialWeapon>();
            coaxialBarrelMaterial = null;
            coaxialMuzzleFlashMaterial = null;

            if (!coaxialAmmoInitialized && coaxialWeaponExt != null)
            {
                // 原版 CompRefuelable 同样仅在实例创建时按 initialFuelPercent 写入库存。
                // 弹仓以整数射击次数保存，因此在乘以容量后四舍五入到最近的可射击次数。
                coaxialAmmoCount = Mathf.Clamp(Mathf.RoundToInt(CoaxialMaxAmmo * Mathf.Clamp01(coaxialWeaponExt.initialAmmoPercent)), 0, CoaxialMaxAmmo);
                coaxialAmmoInitialized = true;
            }
            else if (coaxialWeaponExt != null)
            {
                // XML 更新后的弹仓上限可能缩小；读档时将旧库存安全夹紧。
                coaxialAmmoCount = Mathf.Clamp(coaxialAmmoCount, 0, CoaxialMaxAmmo);
            }

            EnsureCoaxialAnimationListSizes();
            ModExtension_ShootWithOffset visuals = coaxialWeaponExt?.visuals;
            if (visuals == null)
            {
                return;
            }

            if (!visuals.barrelTexturePath.NullOrEmpty())
            {
                ModExtension_CoaxialWeapon ext = coaxialWeaponExt;
                LongEventHandler.ExecuteWhenFinished(delegate
                {
                    if (coaxialWeaponExt == ext)
                    {
                        Shader shader = ext.visuals.barrelUseGlowShader ? ShaderDatabase.MoteGlow : ShaderDatabase.DefaultShader;
                        coaxialBarrelMaterial = MaterialPool.MatFrom(ext.visuals.barrelTexturePath, shader, ext.visuals.barrelColor);
                    }
                });
            }

            if (!visuals.muzzleFlashTexturePath.NullOrEmpty())
            {
                ModExtension_CoaxialWeapon ext = coaxialWeaponExt;
                LongEventHandler.ExecuteWhenFinished(delegate
                {
                    if (coaxialWeaponExt == ext)
                    {
                        Shader shader = ext.visuals.muzzleFlashUseGlowShader ? ShaderDatabase.MoteGlow : ShaderDatabase.DefaultShader;
                        coaxialMuzzleFlashMaterial = MaterialPool.MatFrom(ext.visuals.muzzleFlashTexturePath, shader, ext.visuals.muzzleFlashColor);
                    }
                });
            }
        }

        private void EnsureCoaxialAnimationListSizes()
        {
            int count = coaxialWeaponExt?.visuals?.SlotCount ?? 1;
            EnsureListSize(ref coaxialBarrelRecoilStates, count, 0f);
            EnsureListSize(ref coaxialBarrelRecoilTimers, count, 0);
            EnsureListSize(ref coaxialMuzzleFlashTimers, count, 0);
        }

        private static void EnsureListSize<T>(ref List<T> list, int count, T value)
        {
            if (count <= 0)
            {
                list = null;
                return;
            }

            if (list == null)
            {
                list = new List<T>(count);
            }

            while (list.Count < count)
            {
                list.Add(value);
            }

            if (list.Count > count)
            {
                list.RemoveRange(count, list.Count - count);
            }
        }

        public void Notify_BarrelFired(int barrelIndex)
        {
            // 这个方法只由实际发射 projectile 的 Verb 调用，因此 barrelIndex 一定代表真实开火炮管。
            if (shootWithOffsetExt == null)
            {
                RecacheShootWithOffsetAnimationData();
            }

            if (shootWithOffsetExt == null || barrelIndex < 0)
            {
                return;
            }

            EnsureShootWithOffsetAnimationListSizes();
            int slotCount = shootWithOffsetExt.SlotCount;
            if (slotCount <= 0)
            {
                return;
            }

            barrelIndex = GenMath.PositiveMod(barrelIndex, slotCount);
            if (shootWithOffsetExt.HasBarrelRecoil && barrelRecoilTimers != null && barrelIndex < barrelRecoilTimers.Count)
            {
                // 再次开火会重置该炮管动画，适配极高射速或多 burst 叠加。
                barrelRecoilTimers[barrelIndex] = Mathf.Max(1, shootWithOffsetExt.recoilDurationTicks);
            }

            if (shootWithOffsetExt.HasMuzzleFlash && muzzleFlashTimers != null && barrelIndex < muzzleFlashTimers.Count)
            {
                muzzleFlashTimers[barrelIndex] = shootWithOffsetExt.MuzzleFlashDurationTicks;
            }
        }

        private CompSustainedShoot GetGunSustainedShootComp()
        {
            if (!(AttackVerb is Verb_ShootWithOffset))
            {
                return null;
            }

            // Turret guns are inner Things, so their comps are not ticked by the map tick list.
            return gun?.TryGetComp<CompSustainedShoot>();
        }

        private bool TickGunSustainedShootComp(CompSustainedShoot compSustainedShoot, out bool startedCast)
        {
            startedCast = false;
            if (compSustainedShoot == null)
            {
                return false;
            }

            return compSustainedShoot.TickSustainedShootForTurret((ISustainedShootTurretDriver)this, out startedCast);
        }

        public LocalTargetInfo TryFindSustainedShootTarget()
        {
            return TryFindNewTarget();
        }

        public void ClearSustainedShootDelay()
        {
            if (progressBarEffecter != null)
            {
                progressBarEffecter.Cleanup();
                progressBarEffecter = null;
            }

            burstWarmupTicksLeft = 0;
            burstCooldownTicksLeft = 0;
        }

        public void PrepareSustainedShootTarget(LocalTargetInfo target)
        {
            ClearSustainedShootDelay();
            currentTargetInt = target;
        }

        public bool CanStartSustainedShoot(LocalTargetInfo target)
        {
            if (!target.IsValid)
            {
                return false;
            }

            currentTargetInt = target;
            return Mathf.Abs(deltaAngle) <= 0.1f;
        }

        public void Notify_SustainedShootStarted(LocalTargetInfo target)
        {
            OnAttackedTarget(target);
        }

        public bool SRA_CanAcceptRemoteArtilleryOrder
        {
            get
            {
                if (!Spawned || Destroyed || Faction != Faction.OfPlayer || base.IsStunned)
                {
                    return false;
                }

                if ((holdFire && CanToggleHoldFire) || !Active || (mannableComp != null && !mannableComp.MannedNow))
                {
                    return false;
                }

                return gun?.TryGetComp<CompEquippable>()?.PrimaryVerb?.Available() ?? false;
            }
        }

        public bool SRA_CanBeginRemoteArtilleryBurst
        {
            get
            {
                return SRA_CanAcceptRemoteArtilleryOrder &&
                       AttackVerb.state == VerbState.Idle &&
                       burstCooldownTicksLeft <= 0 &&
                       burstWarmupTicksLeft <= 0;
            }
        }

        public bool SRA_RemoteArtilleryVisualBurstFinished
        {
            get
            {
                return AttackVerb.state == VerbState.Idle && burstWarmupTicksLeft <= 0;
            }
        }

        public void SRA_ClearRemoteArtilleryTarget()
        {
            if (progressBarEffecter != null)
            {
                progressBarEffecter.Cleanup();
                progressBarEffecter = null;
            }

            currentTargetInt = LocalTargetInfo.Invalid;
            burstWarmupTicksLeft = 0;
        }

        public void SRA_RotateRemoteArtilleryTowards(float targetAngle)
        {
            float angleDiff = Mathf.DeltaAngle(curAngle, targetAngle);
            if (!Mathf.Approximately(angleDiff, 0f))
            {
                float speed = rotateSpeed;
                curAngle += Mathf.Abs(angleDiff) > speed ? Mathf.Sign(angleDiff) * speed : angleDiff;
                curAngle = TrimAngle(curAngle);
            }
        }

        public bool SRA_TryBeginRemoteArtilleryBurst(LocalTargetInfo target)
        {
            if (!target.IsValid)
            {
                return false;
            }

            currentTargetInt = target;
            bool started = AttackVerb.TryStartCastOn(CurrentTarget);
            if (started)
            {
                OnAttackedTarget(CurrentTarget);
            }

            return started;
        }

        private void UpdateShootWithOffsetAnimations()
        {
            if (shootWithOffsetExt == null)
            {
                return;
            }

            EnsureShootWithOffsetAnimationListSizes();
            UpdateBarrelRecoil();
            UpdateMuzzleFlash();
        }

        private void UpdateBarrelRecoil()
        {
            if (barrelRecoilTimers == null || barrelRecoilStates == null || shootWithOffsetExt == null)
            {
                return;
            }

            int duration = Mathf.Max(1, shootWithOffsetExt.recoilDurationTicks);
            int kickTicks = Mathf.Clamp(shootWithOffsetExt.recoilKickTicks, 1, duration);
            int returnTicks = Mathf.Max(1, duration - kickTicks);
            for (int i = 0; i < barrelRecoilTimers.Count; i++)
            {
                if (barrelRecoilTimers[i] <= 0)
                {
                    barrelRecoilStates[i] = 0f;
                    continue;
                }

                int elapsed = duration - barrelRecoilTimers[i];
                if (elapsed < kickTicks)
                {
                    // 后坐阶段使用二次曲线，让炮管刚开火时更有冲击感。
                    float progress = Mathf.Clamp01((float)(elapsed + 1) / kickTicks);
                    barrelRecoilStates[i] = progress * progress;
                }
                else
                {
                    // 回弹阶段也使用二次曲线，末尾自然减速回到原位。
                    float progress = Mathf.Clamp01((float)(elapsed - kickTicks + 1) / returnTicks);
                    barrelRecoilStates[i] = 1f - progress * progress;
                }

                barrelRecoilTimers[i]--;
            }
        }

        private void UpdateMuzzleFlash()
        {
            if (muzzleFlashTimers == null)
            {
                return;
            }

            for (int i = 0; i < muzzleFlashTimers.Count; i++)
            {
                if (muzzleFlashTimers[i] > 0)
                {
                    muzzleFlashTimers[i]--;
                }
            }
        }

        private void DrawShootWithOffsetAnimations(Vector3 drawLoc, Vector3 turretRecoilDrawOffset, float turretRecoilAngleOffset, bool drawBelowTurretTop)
        {
            if (shootWithOffsetExt == null || (!shootWithOffsetExt.HasBarrelRecoil && !shootWithOffsetExt.HasMuzzleFlash))
            {
                return;
            }

            if (!shootWithOffsetExt.HasBarrelRecoil && !AnyMuzzleFlashActive())
            {
                return;
            }

            EnsureShootWithOffsetAnimationListSizes();
            int slotCount = shootWithOffsetExt.SlotCount;
            if (slotCount <= 0)
            {
                return;
            }

            Vector3 origin = SRA_ShootWithOffsetUtility.TurretTopCenter(this, drawLoc, turretRecoilDrawOffset, turretRecoilAngleOffset);
            origin.y = TurretPartAltitudeFor(drawLoc.y + Altitudes.AltInc, 0f);
            float aimAngle = ShootWithOffsetDrawAngle();
            Quaternion graphicRotation = SRA_ShootWithOffsetUtility.TurretGraphicRotation(aimAngle);
            Quaternion inverseGraphicRotation = Quaternion.Inverse(graphicRotation);

            // 逐根炮管绘制。坐标点全部由同一个 origin + aimAngle + local offset 生成，
            // graphicRotation 只负责贴图朝向，不再参与解释 offset。
            for (int i = 0; i < slotCount; i++)
            {
                Vector2 offset = shootWithOffsetExt.GetOffsetFor(i);
                Vector2 flashOffset = offset + shootWithOffsetExt.MuzzleFlashLocalOffset;
                float recoil = barrelRecoilStates != null && i < barrelRecoilStates.Count ? barrelRecoilStates[i] * shootWithOffsetExt.recoilAmount : 0f;
                Vector2 barrelLocalCenter = new Vector2(offset.x, offset.y - recoil);

                // offsets 是统一基准点：projectile 出生点、炮管默认中心、火焰默认中心都从这里派生。
                // 制退只移动炮管中心；火焰只应用显式 muzzleFlashOffset / muzzleFlashForwardOffset。
                Vector3 barrelCenter = SRA_ShootWithOffsetUtility.LocalOffsetToWorld(origin, aimAngle, barrelLocalCenter);
                Vector3 flashCenter = SRA_ShootWithOffsetUtility.LocalOffsetToWorld(origin, aimAngle, flashOffset);
                DrawBarrel(origin, barrelCenter, graphicRotation, inverseGraphicRotation, drawBelowTurretTop);
                DrawMuzzleFlash(i, origin, flashCenter, graphicRotation, inverseGraphicRotation, drawBelowTurretTop);
            }
        }

        /// <summary>
        /// 使用与主炮 offset 完全相同的坐标约定绘制同轴副炮。
        /// visuals.offsets 的每一项同时决定副炮射弹出口、炮管中心和炮口火焰基准点，
        /// 因而多根同轴炮管的开火动画不会与实际射击出口脱节。
        /// </summary>
        private void DrawCoaxialWeaponAnimations(Vector3 drawLoc, Vector3 turretRecoilDrawOffset, float turretRecoilAngleOffset, bool drawBelowTurretTop)
        {
            ModExtension_ShootWithOffset visuals = coaxialWeaponExt?.visuals;
            if (visuals == null || (!visuals.HasBarrelRecoil && !visuals.HasMuzzleFlash))
            {
                return;
            }

            if (!visuals.HasBarrelRecoil && !AnyCoaxialMuzzleFlashActive())
            {
                return;
            }

            EnsureCoaxialAnimationListSizes();
            Vector3 origin = SRA_ShootWithOffsetUtility.TurretTopCenter(this, drawLoc, turretRecoilDrawOffset, turretRecoilAngleOffset);
            origin.y = TurretPartAltitudeFor(drawLoc.y + Altitudes.AltInc, 0f);
            float aimAngle = ShootWithOffsetDrawAngle();
            Quaternion graphicRotation = SRA_ShootWithOffsetUtility.TurretGraphicRotation(aimAngle);
            Quaternion inverseGraphicRotation = Quaternion.Inverse(graphicRotation);

            for (int i = 0; i < visuals.SlotCount; i++)
            {
                Vector2 offset = visuals.GetOffsetFor(i);
                float recoil = coaxialBarrelRecoilStates != null && i < coaxialBarrelRecoilStates.Count
                    ? coaxialBarrelRecoilStates[i] * visuals.recoilAmount
                    : 0f;
                Vector2 barrelCenterOffset = new Vector2(offset.x, offset.y - recoil);
                Vector2 flashOffset = offset + visuals.MuzzleFlashLocalOffset;
                Vector3 barrelCenter = SRA_ShootWithOffsetUtility.LocalOffsetToWorld(origin, aimAngle, barrelCenterOffset);
                Vector3 flashCenter = SRA_ShootWithOffsetUtility.LocalOffsetToWorld(origin, aimAngle, flashOffset);

                DrawCoaxialBarrel(origin, barrelCenter, graphicRotation, inverseGraphicRotation, visuals, drawBelowTurretTop);
                DrawCoaxialMuzzleFlash(i, origin, flashCenter, graphicRotation, inverseGraphicRotation, visuals, drawBelowTurretTop);
            }
        }

        private bool AnyCoaxialMuzzleFlashActive()
        {
            if (coaxialMuzzleFlashTimers == null)
            {
                return false;
            }

            for (int i = 0; i < coaxialMuzzleFlashTimers.Count; i++)
            {
                if (coaxialMuzzleFlashTimers[i] > 0)
                {
                    return true;
                }
            }

            return false;
        }

        private void DrawCoaxialBarrel(Vector3 sortOrigin, Vector3 barrelCenter, Quaternion graphicRotation, Quaternion inverseGraphicRotation, ModExtension_ShootWithOffset visuals, bool drawBelowTurretTop)
        {
            if (coaxialBarrelMaterial == null || !visuals.HasBarrelRecoil || PartDrawsBelowTurretTop(visuals.barrelAltitudeOffset) != drawBelowTurretTop)
            {
                return;
            }

            float turretTopAltitude = sortOrigin.y;
            float visualAltitude = TurretPartAltitudeFor(turretTopAltitude, visuals.barrelAltitudeOffset);
            sortOrigin.y = SortAltitudeForPart(turretTopAltitude, visualAltitude);
            barrelCenter.y = visualAltitude;
            Vector3 localVisualCenter = inverseGraphicRotation * (barrelCenter - sortOrigin);
            Mesh mesh = SRA_FrameMeshPool.GetAnchoredMesh(localVisualCenter, visuals.barrelTextureSize);
            Graphics.DrawMesh(mesh, Matrix4x4.TRS(sortOrigin, graphicRotation, Vector3.one), coaxialBarrelMaterial, 0);
        }

        private void DrawCoaxialMuzzleFlash(int index, Vector3 sortOrigin, Vector3 flashCenter, Quaternion graphicRotation, Quaternion inverseGraphicRotation, ModExtension_ShootWithOffset visuals, bool drawBelowTurretTop)
        {
            if (coaxialMuzzleFlashMaterial == null || !visuals.HasMuzzleFlash || coaxialMuzzleFlashTimers == null || index >= coaxialMuzzleFlashTimers.Count || coaxialMuzzleFlashTimers[index] <= 0 || PartDrawsBelowTurretTop(visuals.muzzleFlashAltitudeOffset) != drawBelowTurretTop)
            {
                return;
            }

            int totalTicks = visuals.MuzzleFlashDurationTicks;
            int elapsedTicks = Mathf.Clamp(totalTicks - coaxialMuzzleFlashTimers[index], 0, totalTicks - 1);
            int frame = Mathf.Clamp(elapsedTicks / Mathf.Max(1, visuals.muzzleFlashTicksPerFrame), 0, Mathf.Max(1, visuals.muzzleFlashFrameCount) - 1);
            float turretTopAltitude = sortOrigin.y;
            float visualAltitude = TurretPartAltitudeFor(turretTopAltitude, visuals.muzzleFlashAltitudeOffset);
            sortOrigin.y = SortAltitudeForPart(turretTopAltitude, visualAltitude);
            flashCenter.y = visualAltitude;
            Vector3 localVisualCenter = inverseGraphicRotation * (flashCenter - sortOrigin);
            Mesh mesh = SRA_FrameMeshPool.GetAnchoredFrameMesh(localVisualCenter, visuals.muzzleFlashDrawSize, frame, visuals.muzzleFlashFrameCount, visuals.muzzleFlashFrameColumns);
            Graphics.DrawMesh(mesh, Matrix4x4.TRS(sortOrigin, graphicRotation, Vector3.one), coaxialMuzzleFlashMaterial, 0);
        }

        private float ShootWithOffsetDrawAngle()
        {
            float? aimAngleOverride = AttackVerb?.AimAngleOverride;
            return aimAngleOverride ?? curAngle;
        }

        private bool AnyMuzzleFlashActive()
        {
            if (muzzleFlashTimers == null)
            {
                return false;
            }

            for (int i = 0; i < muzzleFlashTimers.Count; i++)
            {
                if (muzzleFlashTimers[i] > 0)
                {
                    return true;
                }
            }

            return false;
        }

        private float TurretPartAltitudeFor(float turretTopAltitude, float offset)
        {
            float layerMin = def.altitudeLayer.AltitudeFor() + 0.001f;
            int nextLayerIndex = Mathf.Min((int)def.altitudeLayer + 1, (int)AltitudeLayer._Count - 1);
            float layerMax = ((AltitudeLayer)nextLayerIndex).AltitudeFor() - 0.001f;
            if (layerMax <= layerMin)
            {
                return turretTopAltitude + offset;
            }

            return Mathf.Clamp(turretTopAltitude + offset, layerMin, layerMax);
        }

        private void DrawBarrel(Vector3 sortOrigin, Vector3 barrelCenter, Quaternion graphicRotation, Quaternion inverseGraphicRotation, bool drawBelowTurretTop)
        {
            if (barrelMaterial == null || shootWithOffsetExt == null || !shootWithOffsetExt.HasBarrelRecoil)
            {
                return;
            }

            if (PartDrawsBelowTurretTop(shootWithOffsetExt.barrelAltitudeOffset) != drawBelowTurretTop)
            {
                return;
            }

            // DrawShootWithOffsetAnimations 已经算出炮管中心；这里仅负责锚定绘制和高度排序。
            float turretTopAltitude = sortOrigin.y;
            float visualAltitude = TurretPartAltitudeFor(turretTopAltitude, shootWithOffsetExt.barrelAltitudeOffset);
            sortOrigin.y = SortAltitudeForPart(turretTopAltitude, visualAltitude);
            barrelCenter.y = visualAltitude;
            Vector3 localVisualCenter = inverseGraphicRotation * (barrelCenter - sortOrigin);
            Mesh mesh = SRA_FrameMeshPool.GetAnchoredMesh(localVisualCenter, shootWithOffsetExt.barrelTextureSize);
            Graphics.DrawMesh(mesh, Matrix4x4.TRS(sortOrigin, graphicRotation, Vector3.one), barrelMaterial, 0);
        }

        private void DrawMuzzleFlash(int index, Vector3 sortOrigin, Vector3 flashCenter, Quaternion graphicRotation, Quaternion inverseGraphicRotation, bool drawBelowTurretTop)
        {
            if (muzzleFlashMaterial == null || shootWithOffsetExt == null || !shootWithOffsetExt.HasMuzzleFlash || muzzleFlashTimers == null || index >= muzzleFlashTimers.Count || muzzleFlashTimers[index] <= 0)
            {
                return;
            }

            if (PartDrawsBelowTurretTop(shootWithOffsetExt.muzzleFlashAltitudeOffset) != drawBelowTurretTop)
            {
                return;
            }

            int totalTicks = shootWithOffsetExt.MuzzleFlashDurationTicks;
            int elapsedTicks = Mathf.Clamp(totalTicks - muzzleFlashTimers[index], 0, totalTicks - 1);
            int ticksPerFrame = Mathf.Max(1, shootWithOffsetExt.muzzleFlashTicksPerFrame);
            int frame = Mathf.Clamp(elapsedTicks / ticksPerFrame, 0, Mathf.Max(1, shootWithOffsetExt.muzzleFlashFrameCount) - 1);

            Vector3 drawPos = flashCenter;
            float turretTopAltitude = sortOrigin.y;
            float visualAltitude = TurretPartAltitudeFor(turretTopAltitude, shootWithOffsetExt.muzzleFlashAltitudeOffset);
            sortOrigin.y = SortAltitudeForPart(turretTopAltitude, visualAltitude);
            drawPos.y = visualAltitude;

            // 通过带局部偏移的 UV mesh 播放序列帧，同时让排序锚点保持在父炮塔顶层中心。
            Vector3 localVisualCenter = inverseGraphicRotation * (drawPos - sortOrigin);
            Mesh frameMesh = SRA_FrameMeshPool.GetAnchoredFrameMesh(localVisualCenter, shootWithOffsetExt.muzzleFlashDrawSize, frame, shootWithOffsetExt.muzzleFlashFrameCount, shootWithOffsetExt.muzzleFlashFrameColumns);
            Graphics.DrawMesh(frameMesh, Matrix4x4.TRS(sortOrigin, graphicRotation, Vector3.one), muzzleFlashMaterial, 0);
        }

        private static bool PartDrawsBelowTurretTop(float altitudeOffset)
        {
            return altitudeOffset < 0f;
        }

        private static float SortAltitudeForPart(float turretTopAltitude, float visualAltitude)
        {
            // 负偏移只降低实际视觉顶点，不降低排序锚点。
            // 否则会丢掉南侧父炮塔相对北侧炮塔的一格排序优势。
            return Mathf.Max(turretTopAltitude, visualAltitude);
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
            if (base.Spawned && burstCooldownTicksLeft > 0 && BurstCooldownTime() > 5f)
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

            if (UsesIndependentCoaxialAmmo)
            {
                // 与原版 CompChangeableProjectile 的炮塔检查信息保持一致：
                // ShellLoaded 的第二个参数必须是可解析 label 的弹药 Def；
                // 射击次数由原版样式库存条单独显示，不能作为该参数传入。
                if (coaxialAmmoCount > 0)
                {
                    stringBuilder.AppendLine("ShellLoaded".Translate(coaxialWeaponExt.ammoThingDef.LabelCap, coaxialWeaponExt.ammoThingDef));
                }
                else
                {
                    stringBuilder.AppendLine("ShellNotLoaded".Translate());
                }
            }
            else if (coaxialWeaponExt?.projectile != null)
            {
                stringBuilder.AppendLine("SRA_CoaxialWeapon_InfiniteAmmo".Translate());
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

            DrawShootWithOffsetAnimations(drawLoc, drawOffset, angleOffset, drawBelowTurretTop: true);
            DrawCoaxialWeaponAnimations(drawLoc, drawOffset, angleOffset, drawBelowTurretTop: true);
            top.DrawTurret(drawLoc, drawOffset, angleOffset);
            DrawShootWithOffsetAnimations(drawLoc, drawOffset, angleOffset, drawBelowTurretTop: false);
            DrawCoaxialWeaponAnimations(drawLoc, drawOffset, angleOffset, drawBelowTurretTop: false);
            base.DrawAt(drawLoc, flip);
        }

        public override void DrawExtraSelectionOverlays()
        {
            base.DrawExtraSelectionOverlays();
            float effectiveRange = AttackVerb.EffectiveRange;
            float num = AttackVerb.verbProps.EffectiveMinRange(allowAdjacentShot: true);
            SRA_TurretRangeOverlayUtility.DrawTurretRangeRings(base.Position, effectiveRange, num);

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

            // 炮塔内置 gun 不是地图可选实体，无法走 Pawn 装备栏的 Gizmo 路径，
            // 因此这里主动转发弹种选择按钮。
            if (AttackVerb is Verb_ShootWithOffset shootVerb)
            {
                foreach (Gizmo gizmo in shootVerb.GetMultiProjectileGizmos())
                {
                    yield return gizmo;
                }
            }

            // 与原版 CompRefuelable 相同，单选玩家建筑时显示只读库存条。
            // 独立副炮弹仓没有可配置的目标库存，因此不允许拖动。
            if (UsesIndependentCoaxialAmmo && base.Faction == Faction.OfPlayer && Find.Selector.SelectedObjects.Count == 1)
            {
                yield return new Gizmo_CoaxialAmmoLevel(this);
            }

            if (DebugSettings.ShowDevGizmos && UsesIndependentCoaxialAmmo)
            {
                // 开发者按钮不指定 icon，直接使用原版 Gizmo 的默认空图标表现，
                // 不需要为调试操作额外注册任何贴图资源。
                yield return new Command_Action
                {
                    defaultLabel = "SRA_CoaxialWeapon_DevFill".Translate(coaxialWeaponExt.ammoThingDef.LabelCap),
                    defaultDesc = "SRA_CoaxialWeapon_DevFillDesc".Translate(),
                    action = delegate
                    {
                        SetCoaxialAmmoCount(CoaxialMaxAmmo);
                    }
                };

                yield return new Command_Action
                {
                    defaultLabel = "SRA_CoaxialWeapon_DevEmpty".Translate(coaxialWeaponExt.ammoThingDef.LabelCap),
                    defaultDesc = "SRA_CoaxialWeapon_DevEmptyDesc".Translate(),
                    action = delegate
                    {
                        SetCoaxialAmmoCount(0);
                    }
                };
            }

            if (CanToggleCoaxialHoldFire)
            {
                // 复用原版停火按钮的图标与开关表现，但状态只属于同轴副武器。
                // 不设置热键，避免与主炮的 Misc6 停火快捷键冲突。
                yield return new Command_Toggle
                {
                    defaultLabel = "SRA_CoaxialWeapon_HoldFire".Translate(),
                    defaultDesc = "SRA_CoaxialWeapon_HoldFireDesc".Translate(),
                    icon = ContentFinder<Texture2D>.Get("UI/Commands/HoldFire"),
                    toggleAction = delegate
                    {
                        coaxialHoldFire = !coaxialHoldFire;
                        if (coaxialHoldFire)
                        {
                            CancelCoaxialBurst();
                        }
                    },
                    isActive = () => coaxialHoldFire
                };
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

            if (PlayerControlled)
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

        private void ResetGunVerbs()
        {
            CompEquippable compEquippable = gun?.TryGetComp<CompEquippable>();
            if (compEquippable == null)
            {
                return;
            }

            List<Verb> allVerbs = compEquippable.AllVerbs;
            for (int i = 0; i < allVerbs.Count; i++)
            {
                allVerbs[i]?.Reset();
            }
        }

        /// <summary>
        /// 副武器装填 WorkGiver 的入口。该方法只在 Pawn 寻找搬运工作、并已从缓存中选中
        /// 此炮塔时调用。取弹流程对齐原版 RefuelWorkGiverUtility.FindBestFuel：
        /// 以 Pawn 为起点，在配置半径内寻找最近的可达弹药。
        /// </summary>
        public bool TryFindCoaxialReloadAmmo(Pawn pawn, bool forced, out Thing ammo)
        {
            ammo = null;
            if (!NeedsCoaxialAmmoReload || pawn == null || pawn.Map != Map || pawn.Faction != Faction)
            {
                return false;
            }

            // 原版在选工阶段只预检能否预留目标建筑；到达性由 JobDriver 的 Goto Toil
            // 处理。这样不会因 Pawn 当前的普通危险度限制而遗漏可装填的弹药。
            if (!pawn.CanReserve(this, 1, -1, null, forced))
            {
                return false;
            }

            ThingDef ammoDef = coaxialWeaponExt.ammoThingDef;
            ammo = GenClosest.ClosestThingReachable(
                pawn.Position,
                pawn.Map,
                ThingRequest.ForDef(ammoDef),
                PathEndMode.ClosestTouch,
                TraverseParms.For(pawn, Danger.Deadly),
                Mathf.Max(0f, coaxialWeaponExt.reloadSearchRadius),
                delegate(Thing candidate)
                {
                    return candidate.stackCount > 0
                        && !candidate.IsForbidden(pawn)
                        && !candidate.IsBurning()
                        && pawn.CanReserveAndReach(candidate, PathEndMode.ClosestTouch, Danger.Deadly, 1, -1, null, forced);
                });
            return ammo != null;
        }

        /// <summary>
        /// 把 Pawn 手持的独立副武器弹药转换为射击次数。若弹仓只剩部分空间，
        /// 仅消耗需要的物品数量，剩余物品继续留在 Pawn 的搬运容器内。
        /// </summary>
        public int LoadCoaxialAmmo(Thing ammo)
        {
            if (!NeedsCoaxialAmmoReload || ammo == null || ammo.def != coaxialWeaponExt.ammoThingDef)
            {
                return 0;
            }

            int itemCount = Mathf.Min(ammo.stackCount, CoaxialAmmoItemsNeeded);
            if (itemCount <= 0)
            {
                return 0;
            }

            int loadedShots = Mathf.Min(CoaxialAmmoSpace(), itemCount * CoaxialShotsPerAmmoItem);
            int itemsConsumed = Mathf.CeilToInt((float)loadedShots / CoaxialShotsPerAmmoItem);
            if (itemsConsumed <= 0)
            {
                return 0;
            }

            coaxialAmmoCount = Mathf.Min(CoaxialAmmoSpace() + coaxialAmmoCount, coaxialAmmoCount + itemsConsumed * CoaxialShotsPerAmmoItem);
            if (itemsConsumed >= ammo.stackCount)
            {
                ammo.Destroy(DestroyMode.Vanish);
            }
            else
            {
                ammo.stackCount -= itemsConsumed;
            }

            BroadcastCompSignal("Refueled");
            return itemsConsumed;
        }

        /// <summary>
        /// 原版 CompRefuelable 的开发者操作同样直接写入库存值并广播 Refueled 信号。
        /// 该方法只操作副武器自己的库存，绝不会影响主炮 CompChangeableProjectile。
        /// </summary>
        private void SetCoaxialAmmoCount(int value)
        {
            coaxialAmmoCount = Mathf.Clamp(value, 0, CoaxialMaxAmmo);
            if (coaxialAmmoCount <= 0)
            {
                CancelCoaxialBurst();
            }

            BroadcastCompSignal("Refueled");
        }

        private int CoaxialAmmoSpace()
        {
            return Mathf.Max(0, CoaxialMaxAmmo - coaxialAmmoCount);
        }

        private void UpdateCoaxialWeaponAnimations()
        {
            if (coaxialCooldownTicksLeft > 0)
            {
                coaxialCooldownTicksLeft--;
            }

            if (coaxialTicksToNextBurstShot > 0)
            {
                coaxialTicksToNextBurstShot--;
            }

            ModExtension_ShootWithOffset visuals = coaxialWeaponExt?.visuals;
            if (visuals == null)
            {
                return;
            }

            EnsureCoaxialAnimationListSizes();
            UpdateCoaxialBarrelRecoil(visuals);
            if (coaxialMuzzleFlashTimers != null)
            {
                for (int i = 0; i < coaxialMuzzleFlashTimers.Count; i++)
                {
                    if (coaxialMuzzleFlashTimers[i] > 0)
                    {
                        coaxialMuzzleFlashTimers[i]--;
                    }
                }
            }
        }

        private void UpdateCoaxialBarrelRecoil(ModExtension_ShootWithOffset visuals)
        {
            if (!visuals.HasBarrelRecoil || coaxialBarrelRecoilTimers == null || coaxialBarrelRecoilStates == null)
            {
                return;
            }

            int duration = Mathf.Max(1, visuals.recoilDurationTicks);
            int kickTicks = Mathf.Clamp(visuals.recoilKickTicks, 1, duration);
            int returnTicks = Mathf.Max(1, duration - kickTicks);
            for (int i = 0; i < coaxialBarrelRecoilTimers.Count; i++)
            {
                if (coaxialBarrelRecoilTimers[i] <= 0)
                {
                    coaxialBarrelRecoilStates[i] = 0f;
                    continue;
                }

                int elapsed = duration - coaxialBarrelRecoilTimers[i];
                if (elapsed < kickTicks)
                {
                    float progress = Mathf.Clamp01((float)(elapsed + 1) / kickTicks);
                    coaxialBarrelRecoilStates[i] = progress * progress;
                }
                else
                {
                    float progress = Mathf.Clamp01((float)(elapsed - kickTicks + 1) / returnTicks);
                    coaxialBarrelRecoilStates[i] = 1f - progress * progress;
                }

                coaxialBarrelRecoilTimers[i]--;
            }
        }

        private void TickCoaxialWeapon()
        {
            if (coaxialHoldFire)
            {
                // 立即结束同轴 burst，避免停火后仍在下个 burst 间隔发射一枚。
                CancelCoaxialBurst();
                return;
            }

            if (coaxialBurstShotsLeft <= 0)
            {
                if (!CanFireCoaxialWeapon(currentTargetInt, requireReadyCooldown: true))
                {
                    return;
                }

                coaxialBurstShotsLeft = Mathf.Max(1, coaxialWeaponExt.burstShotCount);
                coaxialBurstTarget = currentTargetInt;
            }

            if (coaxialTicksToNextBurstShot > 0)
            {
                return;
            }

            if (!CanFireCoaxialWeapon(coaxialBurstTarget, requireReadyCooldown: false))
            {
                CancelCoaxialBurst();
                return;
            }

            FireCoaxialBurstShot(coaxialBurstTarget);
            coaxialBurstShotsLeft--;
            if (coaxialBurstShotsLeft <= 0)
            {
                FinishCoaxialBurst();
            }
            else
            {
                coaxialTicksToNextBurstShot = Mathf.Max(0, coaxialWeaponExt.ticksBetweenBurstShots);
            }
        }

        /// <summary>
        /// 发射当前 burst 的单发。轮内目标由 coaxialBurstTarget 固定，
        /// 不会因主炮下一次索敌更新而让同一轮射击突然转向其他目标。
        /// </summary>
        private void FireCoaxialBurstShot(LocalTargetInfo intendedTarget)
        {
            int slotCount = coaxialWeaponExt.visuals?.SlotCount ?? 1;
            int barrelIndex = GenMath.PositiveMod(coaxialNextBarrelIndex, Mathf.Max(1, slotCount));
            Vector2 localOffset = coaxialWeaponExt.visuals?.GetOffsetFor(barrelIndex) ?? Vector2.zero;
            Vector3 turretCenter = SRA_ShootWithOffsetUtility.TurretTopCenter(this, DrawPos);
            Vector3 launchOrigin = SRA_ShootWithOffsetUtility.LocalOffsetToWorld(turretCenter, curAngle, localOffset);
            IntVec3 spawnCell = launchOrigin.ToIntVec3();
            if (!spawnCell.InBounds(Map) || spawnCell.Impassable(Map))
            {
                spawnCell = Position;
                launchOrigin = spawnCell.ToVector3Shifted();
                launchOrigin.y = DrawPos.y;
            }

            LocalTargetInfo usedTarget = GetCoaxialUsedTarget(intendedTarget);
            Projectile projectile = GenSpawn.Spawn(coaxialWeaponExt.projectile, spawnCell, Map) as Projectile;
            if (projectile == null)
            {
                return;
            }

            // 独立库存只在 projectile 成功生成后扣除，避免错误 Def 或生成失败时平白损失弹药。
            if (UsesIndependentCoaxialAmmo && coaxialAmmoCount <= 0)
            {
                projectile.Destroy();
                return;
            }

            if (UsesIndependentCoaxialAmmo)
            {
                coaxialAmmoCount--;
            }

            projectile.Launch(this, launchOrigin, usedTarget, intendedTarget, ProjectileHitFlags.All, false, gun);
            coaxialWeaponExt.shootSound?.PlayOneShot(new TargetInfo(Position, Map));
            Notify_CoaxialBarrelFired(barrelIndex);
            coaxialNextBarrelIndex = GenMath.PositiveMod(barrelIndex + 1, Mathf.Max(1, slotCount));
        }

        private void FinishCoaxialBurst()
        {
            coaxialBurstShotsLeft = 0;
            coaxialTicksToNextBurstShot = 0;
            coaxialBurstTarget = LocalTargetInfo.Invalid;
            coaxialCooldownTicksLeft = Mathf.Max(0, coaxialWeaponExt?.cooldownTicks ?? 0);
        }

        private void CancelCoaxialBurst()
        {
            coaxialBurstShotsLeft = 0;
            coaxialTicksToNextBurstShot = 0;
            coaxialBurstTarget = LocalTargetInfo.Invalid;
        }

        private bool CanFireCoaxialWeapon(LocalTargetInfo target, bool requireReadyCooldown)
        {
            if (coaxialWeaponExt?.projectile == null || (requireReadyCooldown && coaxialCooldownTicksLeft > 0) || holdFire || coaxialHoldFire || !target.IsValid)
            {
                return false;
            }

            if (UsesIndependentCoaxialAmmo && coaxialAmmoCount <= 0)
            {
                return false;
            }

            if (target.HasThing && target.Thing.Destroyed)
            {
                return false;
            }

            Vector3 turretCenter = SRA_ShootWithOffsetUtility.TurretTopCenter(this, DrawPos);
            IntVec3 sourceCell = turretCenter.ToIntVec3();
            float distance = sourceCell.DistanceTo(target.Cell);
            float maxRange = coaxialWeaponExt.range > 0f ? coaxialWeaponExt.range : AttackVerb.EffectiveRange;
            if (distance > maxRange || distance < Mathf.Max(0f, coaxialWeaponExt.minRange))
            {
                return false;
            }

            if (Mathf.Abs(CoaxialAngleDelta(target)) > Mathf.Max(0f, coaxialWeaponExt.aimTolerance))
            {
                return false;
            }

            return !coaxialWeaponExt.requireLineOfSight || GenSight.LineOfSight(sourceCell, target.Cell, Map, true);
        }

        private float CoaxialAngleDelta(LocalTargetInfo target)
        {
            Vector3 targetDirection = (target.CenterVector3 - DrawPos).Yto0();
            return Vector3.SignedAngle(turretOrientation, targetDirection, Vector3.up);
        }

        private bool CanAcquireTargetForCoaxialWeapon()
        {
            return !coaxialHoldFire && coaxialWeaponExt?.projectile != null && (!UsesIndependentCoaxialAmmo || coaxialAmmoCount > 0);
        }

        private LocalTargetInfo GetCoaxialUsedTarget(LocalTargetInfo intendedTarget)
        {
            float radius = coaxialWeaponExt.forcedMissRadius;
            if (radius <= 0f || radius >= GenRadial.MaxRadialPatternRadius)
            {
                return intendedTarget;
            }

            int cellCount = GenRadial.NumCellsInRadius(radius);
            if (cellCount <= 0)
            {
                return intendedTarget;
            }

            IntVec3 targetCell = intendedTarget.Cell + GenRadial.RadialPattern[Rand.Range(0, cellCount)];
            targetCell.x = Mathf.Clamp(targetCell.x, 0, Map.Size.x - 1);
            targetCell.z = Mathf.Clamp(targetCell.z, 0, Map.Size.z - 1);
            return new LocalTargetInfo(targetCell);
        }

        private void Notify_CoaxialBarrelFired(int barrelIndex)
        {
            ModExtension_ShootWithOffset visuals = coaxialWeaponExt?.visuals;
            if (visuals == null)
            {
                return;
            }

            EnsureCoaxialAnimationListSizes();
            int slotCount = visuals.SlotCount;
            barrelIndex = GenMath.PositiveMod(barrelIndex, slotCount);
            if (visuals.HasBarrelRecoil && coaxialBarrelRecoilTimers != null)
            {
                coaxialBarrelRecoilTimers[barrelIndex] = Mathf.Max(1, visuals.recoilDurationTicks);
            }

            if (visuals.HasMuzzleFlash && coaxialMuzzleFlashTimers != null)
            {
                coaxialMuzzleFlashTimers[barrelIndex] = visuals.MuzzleFlashDurationTicks;
            }
        }

        public void MakeGun()
        {
            gun = ThingMaker.MakeThing(def.building.turretGunDef);
            UpdateGunVerbs();
            RecacheShootWithOffsetAnimationData();
            RecacheCoaxialWeaponData();
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
