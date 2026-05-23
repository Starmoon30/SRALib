using System.Collections.Generic;
using Verse;

namespace SRA
{
    public class TailFleckEntry
    {
        public FleckDef tailFleckDef; // 拖尾特效的 FleckDef
        public int fleckMakeFleckTickMax = 1; // 首次生成之后的生成间隔（tick）
        public int fleckDelayTicks = 10; // 首次生成拖尾前的延迟（tick）
        public IntRange fleckMakeFleckNum = new IntRange(1, 1); // 每次生成拖尾特效的数量
        public FloatRange fleckAngle = new FloatRange(-180f, 180f); // 拖尾特效的初始角度范围
        public FloatRange fleckScale = new FloatRange(1f, 1f); // 拖尾特效的缩放范围
        public FloatRange fleckSpeed = new FloatRange(0f, 0f); // 拖尾特效的初始速度范围
        public FloatRange fleckRotation = new FloatRange(-180f, 180f); // 拖尾特效的旋转速度范围
    }

    public class TailBulletDef : DefModExtension
    {
        public List<TailFleckEntry> tailFlecks = new List<TailFleckEntry>();
    }
}
