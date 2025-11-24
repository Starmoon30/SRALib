using Verse;

namespace SRA
{
    public class TailBulletDef : DefModExtension
    {
        public FleckDef tailFleckDef; // 拖尾特效的FleckDef
        public int fleckMakeFleckTickMax = 1; // 拖尾特效的生成间隔（tick）
        public int fleckDelayTicks = 10; // 拖尾特效延迟生成时间（tick）
        public IntRange fleckMakeFleckNum = new IntRange(1, 1); // 每次生成拖尾特效的数量
        public FloatRange fleckAngle = new FloatRange(-180f, 180f); // 拖尾特效的初始角度范围
        public FloatRange fleckScale = new FloatRange(1f, 1f); // 拖尾特效的缩放范围
        public FloatRange fleckSpeed = new FloatRange(0f, 0f); // 拖尾特效的初始速度范围
        public FloatRange fleckRotation = new FloatRange(-180f, 180f); // 拖尾特效的旋转速度范围
    }
}