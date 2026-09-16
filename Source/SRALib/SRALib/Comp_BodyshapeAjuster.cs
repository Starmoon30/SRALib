using System;
using System.Collections;
using System.Reflection;
using RimWorld;
using Verse;

namespace SRA
{
    public class CompProperties_BodyShapeAjuster : CompProperties
    {
        public CompProperties_BodyShapeAjuster()
        {
            this.compClass = typeof(Comp_BodyshapeAjuster);
        }
    }
    public class Comp_BodyshapeAjuster : ThingComp
    {
        private BodyTypeDef originalBodyType;
        private bool bodyTypeChanged;

        public override void Notify_Equipped(Pawn pawn)
        {
            base.Notify_Equipped(pawn);
            if (pawn?.story == null || bodyTypeChanged)
            {
                return;
            }

            BodyTypeDef targetBodyType = GetTargetBodyType(pawn);
            if (targetBodyType != null && pawn.story.bodyType != targetBodyType)
            {
                originalBodyType = pawn.story.bodyType;
                bodyTypeChanged = true;
                pawn.story.bodyType = targetBodyType;
            }
        }

        private static BodyTypeDef GetTargetBodyType(Pawn pawn)
        {
            // 大多数物种没有额外的体型限制，直接沿用原本的 Thin 逻辑。
            if (SupportsBodyType(pawn, BodyTypeDefOf.Thin))
            {
                return BodyTypeDefOf.Thin;
            }

            // 异种族框架可能只注册 Male/Female。优先使用与 Pawn 性别相符的体型。
            BodyTypeDef genderBodyType = pawn.gender == Gender.Female
                ? BodyTypeDefOf.Female
                : BodyTypeDefOf.Male;
            if (SupportsBodyType(pawn, genderBodyType))
            {
                return genderBodyType;
            }

            BodyTypeDef otherBodyType = genderBodyType == BodyTypeDefOf.Female
                ? BodyTypeDefOf.Male
                : BodyTypeDefOf.Female;
            return SupportsBodyType(pawn, otherBodyType) ? otherBodyType : null;
        }

        private static bool SupportsBodyType(Pawn pawn, BodyTypeDef bodyType)
        {
            if (pawn?.def == null || bodyType == null)
            {
                return false;
            }

            object bodyTypes;
            if (!TryGetConfiguredBodyTypes(pawn, out bodyTypes))
            {
                return true;
            }

            IEnumerable entries = bodyTypes as IEnumerable;
            if (entries == null)
            {
                return true;
            }

            foreach (object entry in entries)
            {
                if (BodyTypeEntryMatches(entry, bodyType))
                {
                    return true;
                }
            }

            return false;
        }

        private static bool TryGetConfiguredBodyTypes(Pawn pawn, out object bodyTypes)
        {
            // 原版 RaceProperties 没有 bodyTypes；以下读取同时兼容直接扩展和 HAR 的嵌套定义。
            if (TryGetMemberValue(pawn.RaceProps, "bodyTypes", out bodyTypes) && bodyTypes != null)
            {
                return true;
            }

            if (TryGetMemberValue(pawn.def, "bodyTypes", out bodyTypes) && bodyTypes != null)
            {
                return true;
            }

            object alienRace = GetMemberValue(pawn.def, "alienRace");
            object generalSettings = GetMemberValue(alienRace, "generalSettings");
            object bodyGenerator = GetMemberValue(generalSettings, "alienPartGenerator");
            return TryGetMemberValue(bodyGenerator, "bodyTypes", out bodyTypes) && bodyTypes != null;
        }

        private static bool BodyTypeEntryMatches(object entry, BodyTypeDef bodyType)
        {
            if (entry == null)
            {
                return false;
            }

            BodyTypeDef directBodyType = entry as BodyTypeDef;
            if (directBodyType != null)
            {
                return directBodyType == bodyType;
            }

            object configuredBodyType = GetMemberValue(entry, "bodyType")
                ?? GetMemberValue(entry, "bodyTypeDef")
                ?? GetMemberValue(entry, "def")
                ?? GetMemberValue(entry, "name");
            BodyTypeDef configuredDef = configuredBodyType as BodyTypeDef;
            if (configuredDef != null)
            {
                return configuredDef == bodyType;
            }

            string configuredName = configuredBodyType as string;
            return configuredName == bodyType.defName || configuredName == bodyType.label;
        }

        private static object GetMemberValue(object target, string memberName)
        {
            object value;
            return TryGetMemberValue(target, memberName, out value) ? value : null;
        }

        private static bool TryGetMemberValue(object target, string memberName, out object value)
        {
            value = null;
            if (target == null)
            {
                return false;
            }

            const BindingFlags flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
            Type type = target.GetType();
            while (type != null)
            {
                PropertyInfo property = type.GetProperty(memberName, flags);
                if (property != null && property.CanRead && property.GetIndexParameters().Length == 0)
                {
                    value = property.GetValue(target, null);
                    return true;
                }

                FieldInfo field = type.GetField(memberName, flags);
                if (field != null)
                {
                    value = field.GetValue(target);
                    return true;
                }

                type = type.BaseType;
            }

            return false;
        }

        public override void Notify_Unequipped(Pawn pawn)
        {
            base.Notify_Unequipped(pawn);
            if (bodyTypeChanged && originalBodyType != null)
            {
                pawn.story.bodyType = originalBodyType;
                bodyTypeChanged = false;
                originalBodyType = null;
            }
        }

        public override void PostExposeData()
        {
            base.PostExposeData();
            Scribe_Defs.Look(ref originalBodyType, "originalBodyType");
            Scribe_Values.Look(ref bodyTypeChanged, "bodyTypeChanged", false);
        }
    }
}
