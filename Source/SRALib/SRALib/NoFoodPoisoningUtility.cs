using System.Reflection;
using HarmonyLib;
using RimWorld;
using Verse;

namespace SRA
{
    /// <summary>
    /// 给 ThingDef 使用的 XML 扩展。
    /// 在食物 ThingDef 的 modExtensions 里添加：
    /// <li Class="SRA.NoFoodPoisoningExtension" />
    /// 即可让该食物不会造成食物中毒。
    /// </summary>
    public class NoFoodPoisoningExtension : DefModExtension
    {
    }

    /// <summary>
    /// 工具类：判断某个 Thing 是否带有“不会造成食物中毒”的 XML 标记。
    /// </summary>
    public static class NoFoodPoisoningUtility
    {
        public static bool NeverCausesFoodPoisoning(Thing ingestible)
        {
            if (ingestible == null)
            {
                return false;
            }

            ThingDef def = ingestible.def;
            if (def == null)
            {
                return false;
            }

            return def.GetModExtension<NoFoodPoisoningExtension>() != null;
        }
    }
    /// <summary>
    /// 拦截 RimWorld.FoodUtility.AddFoodPoisoningHediff。
    /// 实际添加食物中毒 Hediff 的方法。
    /// 如果食物 ThingDef 带有 NoFoodPoisoningExtension，则直接阻止原方法执行。
    /// </summary>
    [HarmonyPatch(typeof(FoodUtility), nameof(FoodUtility.AddFoodPoisoningHediff))]
    public static class Patch_FoodUtility_AddFoodPoisoningHediff
    {
        public static bool Prefix(Pawn pawn, Thing ingestible, FoodPoisonCause cause)
        {
            if (NoFoodPoisoningUtility.NeverCausesFoodPoisoning(ingestible))
            {
                return false;
            }

            return true;
        }
    }
}
