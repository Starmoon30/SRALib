using System.Collections.Generic;
using UnityEngine;
using Verse.Sound;
using RimWorld;
using Verse;

//对空拦截逻辑
namespace SRA
{
    public class MapComponent_LaserADSManager : MapComponent
    {
        //炮弹熔毁进度列表
        public HashSet<CompLaserADS> activeLasers = new HashSet<CompLaserADS>();
        private Dictionary<Projectile, float> irradiationProgress = new Dictionary<Projectile, float>();
        //垃圾数据缓存池
        private List<CompLaserADS> lasersToRemoveCache = new List<CompLaserADS>();
        private List<Projectile> keysToRemoveCache = new List<Projectile>();
        //公用接口
        public MapComponent_LaserADSManager(Map map) : base(map) { }
        public void Register(CompLaserADS comp) { if (!activeLasers.Contains(comp)) activeLasers.Add(comp); }
        //组网协同拦截
        public int GetTargetingCount(Thing proj)
        {
            int count = 0;
            foreach (var laser in activeLasers) if (laser.currentTarget == proj) count++;
            return count;
        }
        //照射加热熔毁逻辑
        public bool ApplyIrradiation(Projectile proj, float amount)
        {
            if (proj == null || proj.Destroyed) return false;
            if (!irradiationProgress.ContainsKey(proj)) irradiationProgress[proj] = 0f;
            irradiationProgress[proj] += amount;
            if (irradiationProgress[proj] >= 1f)
            {
                irradiationProgress.Remove(proj);
                return true;
            }
            return false;
        }
        //逻辑帧处理器
        public override void MapComponentTick()
        {
            base.MapComponentTick();
            if (Find.TickManager.TicksGame % 60 == 0)
            {
                if (activeLasers.Count > 0)
                {
                    lasersToRemoveCache.Clear();
                    foreach (var comp in activeLasers)
                    {
                        if (comp == null || comp.parent == null || !comp.parent.Spawned) lasersToRemoveCache.Add(comp);
                    }
                    for (int i = 0; i < lasersToRemoveCache.Count; i++) activeLasers.Remove(lasersToRemoveCache[i]);
                }
                if (irradiationProgress.Count > 0)
                {
                    keysToRemoveCache.Clear();
                    foreach (var k in irradiationProgress.Keys)
                    {
                        if (k == null || k.Destroyed || !k.Spawned) keysToRemoveCache.Add(k);
                    }
                    for (int i = 0; i < keysToRemoveCache.Count; i++) irradiationProgress.Remove(keysToRemoveCache[i]);
                }
            }
        }
        //渲染帧处理器
        public override void MapComponentUpdate()
        {
            base.MapComponentUpdate();
            foreach (var comp in activeLasers) comp.DrawLaserOffscreen();
        }
    }
    //防空火控雷达
    public partial class CompLaserADS : ThingComp
    {
        private void TryFindTarget_AntiAir()
        {
            Projectile bestProj = null;
            int minAttackers = int.MaxValue;
            float minDistSq = float.MaxValue;
            Vector3 myPos = GetAbsolutePosition();
            var mapComp = this.parent.Map.GetComponent<MapComponent_LaserADSManager>();
            foreach (Thing t in this.parent.Map.listerThings.ThingsInGroup(ThingRequestGroup.Projectile))
            {
                if (t is Projectile p && !p.Destroyed && p.Spawned && p.def.projectile.flyOverhead && (p.Launcher == null || p.Launcher.HostileTo(this.parent.Faction)))
                {
                    int projDamage = p.def.projectile.damageDef != null ? p.DamageAmount : 0;
                    if (projDamage >= minInterceptDamage)
                    {
                        int attackers = mapComp.GetTargetingCount(p);
                        float distSq = p.Position.DistanceToSquared(myPos.ToIntVec3());
                        if (attackers < minAttackers || (attackers == minAttackers && distSq < minDistSq))
                        {
                            minAttackers = attackers;
                            minDistSq = distSq;
                            bestProj = p;
                        }
                    }
                }
            }
            if (currentTarget != bestProj) { currentTarget = bestProj; currentTargetIrradiationTicks = 0; }
        }
        //激光照射拦截
        private void ProcessAntiAirTick()
        {
            Projectile proj = currentTarget as Projectile;
            if (proj != null && !proj.Destroyed)
            {
                EmitLaserSparks(proj.DrawPos, (proj.ExactRotation * Vector3.forward).AngleFlat() + 180f, false);
                lastIrradiationTick = Find.TickManager.TicksGame;
                var mapComp = this.parent.Map.GetComponent<MapComponent_LaserADSManager>();
                float damagePerTick = 1f / Mathf.Max(1, Props.laserDurationTicks);
                bool destroyed = mapComp.ApplyIrradiation(proj, damagePerTick);
                if (destroyed)
                {
                    EmitLaserSparks(proj.DrawPos, (proj.ExactRotation * Vector3.forward).AngleFlat(), true);
                    (Props.interceptSound ?? DefDatabase<SoundDef>.GetNamed("Explosion_Bomb", false) ?? SoundDefOf.Tick_High)?.PlayOneShot(new TargetInfo(proj.DrawPos.ToIntVec3(), this.parent.Map));
                    proj.Destroy(DestroyMode.Vanish);
                    cooldownTicksLeft = Props.cooldownTicks;
                    lastInterceptTick = Find.TickManager.TicksGame;
                    lastInterceptPos = proj.DrawPos;
                    ResetTarget();
                }
            }
        }
    }
}