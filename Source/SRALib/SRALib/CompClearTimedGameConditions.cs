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
            List<GameCondition> activeConditions = map?.gameConditionManager?.ActiveConditions;
            if (activeConditions == null)
            {
                return false;
            }

            for (int i = 0; i < activeConditions.Count; i++)
            {
                if (ShouldClearCondition(activeConditions[i]))
                {
                    return true;
                }
            }

            return HasCurrentRemovableWeather(map, activeConditions);
        }

        private void ClearTimedConditions()
        {
            if (!CanUseNow())
            {
                Messages.Message(Props.powerRequiredMessageKey.Translate(), MessageTypeDefOf.RejectInput, false);
                return;
            }

            List<GameCondition> activeConditions = parent.Map?.gameConditionManager?.ActiveConditions;
            if (activeConditions == null)
            {
                Messages.Message(Props.noTargetMessageKey.Translate(), MessageTypeDefOf.RejectInput, false);
                return;
            }

            List<GameCondition> toClear = new List<GameCondition>();
            for (int i = 0; i < activeConditions.Count; i++)
            {
                GameCondition condition = activeConditions[i];
                if (ShouldClearCondition(condition))
                {
                    toClear.Add(condition);
                }
            }

            List<string> clearedLabels = new List<string>(toClear.Count);
            for (int i = 0; i < toClear.Count; i++)
            {
                GameCondition condition = toClear[i];
                clearedLabels.Add(condition.LabelCap);
                condition.End();
            }

            if (TryClearCurrentWeather(parent.Map, activeConditions, out string clearedWeatherLabel))
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
            if (!HasTimedConditionEnd(condition))
            {
                return false;
            }

            if (!HasAnyWhitelist())
            {
                return true;
            }

            return !IsWhitelistedCondition(condition);
        }

        private bool HasTimedConditionEnd(GameCondition condition)
        {
            return condition != null && !condition.Permanent && condition.TicksLeft > 0;
        }

        private bool HasCurrentRemovableWeather(Map map, List<GameCondition> activeConditions)
        {
            return TryGetRemovableWeather(map, activeConditions, out _, out _);
        }

        private bool TryClearCurrentWeather(Map map, List<GameCondition> activeConditions, out string clearedWeatherLabel)
        {
            clearedWeatherLabel = null;
            if (!TryGetRemovableWeather(map, activeConditions, out WeatherDef currentWeather, out WeatherDef replacementWeather))
            {
                return false;
            }

            map.weatherManager.TransitionTo(replacementWeather);
            clearedWeatherLabel = currentWeather.LabelCap;
            return true;
        }

        private bool TryGetRemovableWeather(Map map, List<GameCondition> activeConditions, out WeatherDef currentWeather, out WeatherDef replacementWeather)
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

            if (GetCurrentWeatherTicksLeft(map) <= 0 || map.weatherDecider.ForcedWeather != null || IsWeatherControlledByCondition(currentWeather, activeConditions))
            {
                return false;
            }

            replacementWeather = ChooseReplacementWeather(map, currentWeather);
            return replacementWeather != null && replacementWeather != currentWeather;
        }

        private bool HasAnyWhitelist()
        {
            return (Props.gameConditionWhitelist != null && Props.gameConditionWhitelist.Count > 0) ||
                   (Props.weatherWhitelist != null && Props.weatherWhitelist.Count > 0);
        }

        private bool IsWhitelistedCondition(GameCondition condition)
        {
            if (condition == null)
            {
                return false;
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

            if (!HasAnyWhitelist())
            {
                return true;
            }

            return Props.weatherWhitelist == null || !Props.weatherWhitelist.Contains(weather);
        }

        private bool IsWeatherControlledByCondition(WeatherDef weather, List<GameCondition> activeConditions)
        {
            if (weather == null || activeConditions == null)
            {
                return false;
            }

            for (int i = 0; i < activeConditions.Count; i++)
            {
                GameCondition condition = activeConditions[i];
                if (condition == null)
                {
                    continue;
                }

                WeatherDef forcedWeather = condition.ForcedWeather() ?? condition.def?.weatherDef;
                if (forcedWeather == weather)
                {
                    return true;
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
