using RimWorld;
using Verse;
using Verse.Sound;
using System.Collections.Generic;

namespace SRA
{
    public class CompProperties_PlaySoundOnSpawn : CompProperties
    {
        public SoundDef sound;

        // 可选：延迟播放声音（秒）
        public float delaySeconds = 0f;

        // 可选：只在特定条件下播放
        public bool onlyIfPlayerFaction = false;
        public bool onlyIfHostileFaction = false;
        public bool onlyIfNeutralFaction = false;

        // 可选：音量控制
        public float volume = 1f;
        public float pitch = 1f;

        // 可选：播放位置
        public bool playOnCamera = false; // 在摄像机位置播放
        public bool playAtThingPosition = true; // 在物体位置播放

        public CompProperties_PlaySoundOnSpawn()
        {
            compClass = typeof(CompPlaySoundOnSpawn);
        }
    }
    public class CompPlaySoundOnSpawn : ThingComp
    {
        private CompProperties_PlaySoundOnSpawn Props => (CompProperties_PlaySoundOnSpawn)props;

        private bool soundPlayed = false;
        private int delayTicksRemaining = 0;

        public override void Initialize(CompProperties props)
        {
            base.Initialize(props);

            // 计算延迟的 ticks
            if (Props.delaySeconds > 0)
            {
                delayTicksRemaining = (int)(Props.delaySeconds * 60f);
            }
        }

        public override void PostSpawnSetup(bool respawningAfterLoad)
        {
            base.PostSpawnSetup(respawningAfterLoad);

            // 如果是重新加载存档，不播放声音
            if (respawningAfterLoad)
                return;

            // 检查播放条件
            if (!ShouldPlaySound())
                return;

            // 如果有延迟，设置延迟计数器
            if (Props.delaySeconds > 0)
            {
                delayTicksRemaining = (int)(Props.delaySeconds * 60f);
            }
            else
            {
                // 立即播放声音
                PlaySound();
            }
        }

        public override void CompTick()
        {
            base.CompTick();

            // 处理延迟播放
            if (delayTicksRemaining > 0 && !soundPlayed)
            {
                delayTicksRemaining--;
                if (delayTicksRemaining <= 0)
                {
                    PlaySound();
                }
            }
        }

        private bool ShouldPlaySound()
        {
            if (soundPlayed)
                return false;

            if (Props.sound == null)
                return false;

            // 检查派系条件
            if (parent.Faction != null)
            {
                if (Props.onlyIfPlayerFaction && parent.Faction != Faction.OfPlayer)
                    return false;

                if (Props.onlyIfHostileFaction && parent.Faction.RelationKindWith(Faction.OfPlayer) != FactionRelationKind.Hostile)
                    return false;

                if (Props.onlyIfNeutralFaction && parent.Faction.RelationKindWith(Faction.OfPlayer) != FactionRelationKind.Neutral)
                    return false;
            }
            else
            {
                // 如果没有派系，检查是否要求特定派系
                if (Props.onlyIfPlayerFaction || Props.onlyIfHostileFaction || Props.onlyIfNeutralFaction)
                    return false;
            }

            return true;
        }

        private void PlaySound()
        {
            if (soundPlayed || Props.sound == null)
                return;

            try
            {
                SoundInfo soundInfo;

                if (Props.playOnCamera)
                {
                    // 在摄像机位置播放
                    soundInfo = SoundInfo.OnCamera();
                }
                else if (Props.playAtThingPosition)
                {
                    // 在物体位置播放
                    soundInfo = SoundInfo.InMap(new TargetInfo(parent.Position, parent.Map));
                }
                else
                {
                    // 默认在物体位置播放
                    soundInfo = SoundInfo.InMap(new TargetInfo(parent.Position, parent.Map));
                }

                // 应用音量和音调设置
                if (Props.volume != 1f)
                    soundInfo.volumeFactor = Props.volume;

                if (Props.pitch != 1f)
                    soundInfo.pitchFactor = Props.pitch;

                // 播放声音
                Props.sound.PlayOneShot(soundInfo);
                soundPlayed = true;

                // 调试日志
                if (Prefs.DevMode)
                {
                    SRALog.Debug($"Played spawn sound: {Props.sound.defName} for {parent.Label} at {parent.Position}");
                }
            }
            catch (System.Exception ex)
            {
                Log.Error($"Error playing spawn sound for {parent.Label}: {ex}");
            }
        }

        public override void PostExposeData()
        {
            base.PostExposeData();
            Scribe_Values.Look(ref soundPlayed, "soundPlayed", false);
            Scribe_Values.Look(ref delayTicksRemaining, "delayTicksRemaining", 0);
        }

        // 调试工具
        public override IEnumerable<Gizmo> CompGetGizmosExtra()
        {
            if (DebugSettings.ShowDevGizmos)
            {
                yield return new Command_Action
                {
                    defaultLabel = "Dev: Test Spawn Sound",
                    defaultDesc = $"Play sound: {Props.sound?.defName ?? "None"}",
                    action = () =>
                    {
                        if (Props.sound != null)
                        {
                            PlaySound();
                        }
                        else
                        {
                            Log.Warning("No sound defined for CompPlaySoundOnSpawn");
                        }
                    }
                };

                yield return new Command_Action
                {
                    defaultLabel = $"Dev: Sound Status - Played: {soundPlayed}, Delay: {delayTicksRemaining}",
                    action = () => { }
                };
            }
        }

        // 重置状态（用于重新播放）
        public void Reset()
        {
            soundPlayed = false;
            delayTicksRemaining = Props.delaySeconds > 0 ? (int)(Props.delaySeconds * 60f) : 0;
        }
    }
}
