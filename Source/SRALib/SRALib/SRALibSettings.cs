using HarmonyLib;
using HarmonyMod;
using RimWorld;
using System;
using System.Reflection;
using UnityEngine;
using Verse;

namespace SRA
{
    [StaticConstructorOnStartup]
    public class SRALib : Mod
    {
        public static SRALibSettings settings;

        public SRALib(ModContentPack content) : base(content)
        {
            settings = GetSettings<SRALibSettings>();

            // 初始化Harmony
            var harmony = new Harmony("DiZhuan.SRALib");
            harmony.PatchAll(Assembly.GetExecutingAssembly());

            SRALog.Debug("[SRALib] Harmony patches applied.");
        }

        public override void DoSettingsWindowContents(Rect inRect)
        {
            Listing_Standard listingStandard = new Listing_Standard();
            listingStandard.Begin(inRect);

            listingStandard.CheckboxLabeled("Enable Debug Logs".Translate(), ref settings.enableDebugLogs, "Enable detailed debug logging (independent of DevMode)".Translate());

            listingStandard.End();
            base.DoSettingsWindowContents(inRect);
        }

        public override string SettingsCategory()
        {
            return "SRALib";
        }
    }

    public class SRALibSettings : ModSettings
    {
        public bool enableDebugLogs = false;

        public override void ExposeData()
        {
            Scribe_Values.Look(ref enableDebugLogs, "enableDebugLogs", false);
            base.ExposeData();
        }
    }
}
