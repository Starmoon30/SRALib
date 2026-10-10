using System;
using System.Linq.Expressions;
using HarmonyLib;
using RimWorld;
using Verse;

namespace SRA
{
    /// <summary>
    /// TIP 可选兼容：沿用 TIP 的按钮和存档，仅把空闲朝向交给 SRALib 自身的转速逻辑。
    /// </summary>
    [StaticConstructorOnStartup]
    internal static class TurretIdlePositionCompatibility
    {
        // TIP 的查询会 Split 字符串并创建 Tuple。
        private const int RefreshIntervalTicks = 30;
        private static readonly Func<Building_Turret, Tuple<float, float>> getIdlePosition;

        public static bool Enabled => getIdlePosition != null;

        /// <summary>
        /// 仅保存运行时快照；生成、读档和跨地图重新生成时重新读取 TIP。
        /// </summary>
        internal struct IdleAngleCache
        {
            public bool initialized;
            public float? angle;
        }

        static TurretIdlePositionCompatibility()
        {
            if (!ModsConfig.IsActive("Mlie.TurretIdlePosition"))
            {
                return;
            }

            try
            {
                // 在启动时反射一次，并编译成后续可直接调用的委托。
                Type tipType = AccessTools.TypeByName("TurretIdlePosition.TurretIdlePosition");
                var componentField = tipType == null ? null :
                    AccessTools.Field(tipType, "TurretIdlePositionGameComponent");
                Type valuesType = typeof(Tuple<float, float>);
                var tryGetMethod = componentField == null ? null :
                    AccessTools.Method(componentField.FieldType, "TryGetTurretIdlePosition",
                        new[] { typeof(Building_Turret), valuesType.MakeByRefType() });

                if (componentField == null || !componentField.IsStatic || tryGetMethod == null ||
                    tryGetMethod.IsStatic || tryGetMethod.ReturnType != typeof(bool))
                {
                    Log.Warning("[SRALib] Turret Idle Position API not recognized; idle rotation compatibility is disabled.");
                    return;
                }

                var turret = Expression.Parameter(typeof(Building_Turret), "turret");
                var component = Expression.Variable(componentField.FieldType, "component");
                var values = Expression.Variable(valuesType, "values");
                var noValues = Expression.Constant(null, valuesType);
                var body = Expression.Block(new[] { component, values },
                    // 每次刷新读取当前 GameComponent，不能把某个存档的实例永久绑定到静态委托。
                    Expression.Assign(component, Expression.Field(null, componentField)),
                    Expression.Condition(
                        Expression.NotEqual(component, Expression.Constant(null, component.Type)),
                        Expression.Condition(Expression.Call(component, tryGetMethod, turret, values),
                            values, noValues),
                        noValues));

                getIdlePosition = Expression.Lambda<Func<Building_Turret, Tuple<float, float>>>(body, turret).Compile();
            }
            catch (Exception exception)
            {
                // 模组接口变化时仅停用兼容。
                Log.Warning("[SRALib] Could not initialize Turret Idle Position compatibility: " + exception);
            }
        }

        /// <summary>
        /// 由炮塔在无目标、无人操作、非跨地图炮击时调用。
        /// </summary>
        public static void TickIdleRotation(Building_TurretGunHasSpeed turret, ref IdleAngleCache cache)
        {
            if (!cache.initialized || turret.IsHashIntervalTick(RefreshIntervalTicks))
            {
                Tuple<float, float> values = getIdlePosition(turret);
                cache.angle = values != null && !float.IsNaN(values.Item1) && !float.IsInfinity(values.Item1)
                    ? (float?)values.Item1 : null;
                cache.initialized = true;
            }

            if (cache.angle.HasValue)
            {
                // 与参考补丁一致，回到设定的中心角。
                turret.SRA_RotateRemoteArtilleryTowards(cache.angle.Value);
            }
        }
    }
}
