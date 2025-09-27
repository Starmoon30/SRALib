using HarmonyLib;
using RimWorld;
using System;
using UnityEngine;
using Verse;

namespace SRA
{

    public class SRALib : Mod
    {
        public SRALib(ModContentPack content)
            : base(content)
        {
            new Harmony("DiZhuan.SRALib").PatchAll();
        }
    }
}
