using System.Collections.Generic;
using HarmonyLib;
using RimWorld;
using UnityEngine;
using Verse;

namespace SRA
{
    public class CompClearTimedGameConditions : ThingComp
    {
        private static readonly System.Reflection.FieldInfo CurWeatherDurationField = AccessTools.Field(typeof(WeatherDecider), "curWeatherDuration");
        private static readonly System.Reflection.FieldInfo CurWeatherAgeField = AccessTools.Field(typeof(WeatherManager), "curWeatherAge");

        private CompPowerTrader powerComp;

        public CompProperties_ClearTimedGameConditions Props => (CompProperties_ClearTimedGameConditions)props;

        public override void PostSpawnSetup(bool respawningAfterLoad)
        {
            base.PostSpawnSetup(respawningAfterLoad);
            powerComp = parent.GetComp<CompPowerTrader>();
        }

        public override IEnumerable<Gizmo> CompGetGizmosExtra()
        {
            foreach (Gizmo gizmo in base.CompGetGizmosExtra())
            {
                yield return gizmo;
            }

            if (parent.Faction != Faction.OfPlayer || parent.Map == null)
            {
                yield break;
            }

            Command_Action clearCommand = new Command_Action
            {
                defaultLabel = Props.buttonLabelKey.Translate(),
                defaultDesc = Props.buttonDescKey.Translate(),
                icon = ResolveIcon(),
                action = ClearTimedConditions
            };

            if (!CanUseNow())
            {
                clearCommand.Disable(Props.powerRequiredMessageKey.Translate());
            }
            else if (!HasAnyRemovableTarget())
            {
                clearCommand.Disable(Props.noTargetMessageKey.Translate());
            }

            yield return clearCommand;
        }

        private bool CanUseNow()
        {
            if (!Props.requirePower)
            {
                return true;
            }

            powerComp ??= parent.GetComp<CompPowerTrader>();
            return powerComp == null || powerComp.PowerOn;
        }

        private bool HasAnyRemovableTarget()
        {
            Map map = parent.Map;
            if (map == null || map.gameConditionManager == null)
            {
                return false;
            }

            return HasRemovableConditionAffectingMap(map) || HasCurrentRemovableWeather(map);
        }

        private void ClearTimedConditions()
        {
            if (!CanUseNow())
            {
                Messages.Message(Props.powerRequiredMessageKey.Translate(), MessageTypeDefOf.RejectInput, false);
                return;
            }

            Map map = parent.Map;
            if (map == null || map.gameConditionManager == null)
            {
                Messages.Message(Props.noTargetMessageKey.Translate(), MessageTypeDefOf.RejectInput, false);
                return;
            }

            List<GameCondition> toClear = new List<GameCondition>();
            CollectRemovableConditionsAffectingMap(map, toClear);

            List<string> clearedLabels = new List<string>(toClear.Count);
            for (int i = 0; i < toClear.Count; i++)
            {
                GameCondition condition = toClear[i];
                clearedLabels.Add(condition.LabelCap);
                condition.End();
            }

            if (TryClearCurrentWeather(map, out string clearedWeatherLabel))
            {
                clearedLabels.Add(clearedWeatherLabel);
            }

            if (clearedLabels.Count == 0)
            {
                Messages.Message(Props.noTargetMessageKey.Translate(), MessageTypeDefOf.RejectInput, false);
                return;
            }

            Messages.Message(Props.clearedMessageKey.Translate(clearedLabels.Count, clearedLabels.ToCommaList()), MessageTypeDefOf.PositiveEvent, false);
        }

        private bool ShouldClearCondition(GameCondition condition)
        {
            return HasTimedConditionEnd(condition) && !IsWhitelistedCondition(condition);
        }

        private bool HasTimedConditionEnd(GameCondition condition)
        {
            return condition != null && !condition.Permanent && condition.TicksLeft > 0;
        }

        /// <summary>
        /// 地图条件管理器只保存本地图条件；太阳耀斑、日食和极光等世界事件保存在 Parent 中。
        /// 只处理 CanApplyOnMap 为真的条件，避免结束其他地图或不适用地图层的世界级效果。
        /// </summary>
        private bool HasRemovableConditionAffectingMap(Map map)
        {
            for (GameConditionManager manager = map?.gameConditionManager; manager != null; manager = manager.Parent)
            {
                List<GameCondition> conditions = manager.ActiveConditions;
                for (int i = 0; i < conditions.Count; i++)
                {
                    GameCondition condition = conditions[i];
                    if (condition != null && condition.CanApplyOnMap(map) && ShouldClearCondition(condition))
                    {
                        return true;
                    }
                }
            }

            return false;
        }

        private void CollectRemovableConditionsAffectingMap(Map map, List<GameCondition> toClear)
        {
            for (GameConditionManager manager = map?.gameConditionManager; manager != null; manager = manager.Parent)
            {
                List<GameCondition> conditions = manager.ActiveConditions;
                for (int i = 0; i < conditions.Count; i++)
                {
                    GameCondition condition = conditions[i];
                    if (condition != null && condition.CanApplyOnMap(map) && ShouldClearCondition(condition))
                    {
                        toClear.Add(condition);
                    }
                }
            }
        }

        private bool HasCurrentRemovableWeather(Map map)
        {
            return TryGetRemovableWeather(map, out _, out _);
        }

        private bool TryClearCurrentWeather(Map map, out string clearedWeatherLabel)
        {
            clearedWeatherLabel = null;
            if (!TryGetRemovableWeather(map, out WeatherDef currentWeather, out WeatherDef replacementWeather))
            {
                return false;
            }

            map.weatherManager.TransitionTo(replacementWeather);
            clearedWeatherLabel = currentWeather.LabelCap;
            return true;
        }

        private bool TryGetRemovableWeather(Map map, out WeatherDef currentWeather, out WeatherDef replacementWeather)
        {
            currentWeather = null;
            replacementWeather = null;

            if (map?.weatherManager == null || map.weatherDecider == null)
            {
                return false;
            }

            currentWeather = map.weatherManager.curWeather;
            if (currentWeather == null || !ShouldClearWeather(currentWeather))
            {
                return false;
            }

            if (GetCurrentWeatherTicksLeft(map) <= 0 || map.weatherDecider.ForcedWeather != null || IsWeatherControlledByCondition(currentWeather, map))
            {
                return false;
            }

            replacementWeather = ChooseReplacementWeather(map, currentWeather);
            return replacementWeather != null && replacementWeather != currentWeather;
        }

        private bool IsWhitelistedCondition(GameCondition condition)
        {
            if (condition == null)
            {
                return false;
            }

            // Planetkiller 是剧本/任务的世界毁灭倒计时。即使它带有 TicksLeft，也绝不能被通用清除按钮绕过。
            if (condition.def?.defName == "Planetkiller")
            {
                return true;
            }

            if (Props.gameConditionWhitelist != null && Props.gameConditionWhitelist.Contains(condition.def))
            {
                return true;
            }

            WeatherDef forcedWeather = condition.ForcedWeather() ?? condition.def?.weatherDef;
            return forcedWeather != null && Props.weatherWhitelist != null && Props.weatherWhitelist.Contains(forcedWeather);
        }

        private bool ShouldClearWeather(WeatherDef weather)
        {
            if (weather == null)
            {
                return false;
            }

            return Props.weatherWhitelist == null || !Props.weatherWhitelist.Contains(weather);
        }

        private bool IsWeatherControlledByCondition(WeatherDef weather, Map map)
        {
            if (weather == null || map == null)
            {
                return false;
            }

            for (GameConditionManager manager = map.gameConditionManager; manager != null; manager = manager.Parent)
            {
                List<GameCondition> conditions = manager.ActiveConditions;
                for (int i = 0; i < conditions.Count; i++)
                {
                    GameCondition condition = conditions[i];
                    if (condition == null || !condition.CanApplyOnMap(map))
                    {
                        continue;
                    }

                    WeatherDef forcedWeather = condition.ForcedWeather() ?? condition.def?.weatherDef;
                    if (forcedWeather == weather)
                    {
                        return true;
                    }
                }
            }

            return false;
        }

        private int GetCurrentWeatherTicksLeft(Map map)
        {
            if (map?.weatherDecider == null || map.weatherManager == null || CurWeatherDurationField == null || CurWeatherAgeField == null)
            {
                return -1;
            }

            int duration = (int)CurWeatherDurationField.GetValue(map.weatherDecider);
            int age = (int)CurWeatherAgeField.GetValue(map.weatherManager);
            return duration > 0 && age >= 0 ? duration - age : -1;
        }

        private WeatherDef ChooseReplacementWeather(Map map, WeatherDef currentWeather)
        {
            WeatherDef bestWeather = null;
            float bestCommonality = float.MinValue;

            foreach (WeatherCommonalityRecord record in map.weatherDecider.WeatherCommonalities)
            {
                if (record.weather == null || record.weather == currentWeather || record.commonality <= bestCommonality)
                {
                    continue;
                }

                bestWeather = record.weather;
                bestCommonality = record.commonality;
            }

            return bestWeather;
        }

        private Texture2D ResolveIcon()
        {
            Texture2D icon = null;

            if (!Props.iconPath.NullOrEmpty())
            {
                icon = ContentFinder<Texture2D>.Get(Props.iconPath, false);
            }

            return icon ?? BaseContent.BadTex;
        }
    }
}
