using UnityEngine;
using Verse;

namespace SRA
{
    public class MoteTiledBeamSheathExtension : DefModExtension
    {
        public float segmentSpacing = 0.95f;
        public float segmentLength = 0.8f;
        public float baseSize = -1f;
        public float expandedSize = -1f;
        public float minSizeFactor = -1f;
        public float baseWidth = 0.55f;
        public float expandedWidth = 2.35f;
        public float minWidthFactor = 0.35f;
        public float maxLengthFactor = 1.25f;
        public float scrollSpeed = 0.22f;
        public float phaseStride = 0.31f;
        public float alpha = 0.55f;
        public float alphaPower = 1.25f;
        public float outerAlpha = 1f;
        public float outerSizeFactor = -1f;
        public float outerWidthFactor = 1f;
        public float outerLengthFactor = 1f;
        public bool drawInnerLayer = true;
        public float innerAlpha = 0.8f;
        public float innerSizeFactor = -1f;
        public float innerWidthFactor = 0.38f;
        public float innerLengthFactor = 0.7f;
        public float innerWhiteBlend = 0.7f;
        public float sizeJitter = -1f;
        public float widthJitter = 0.2f;
        public float lengthJitter = 0.18f;
        public float perpendicularJitter = 0.16f;
        public float altitudeOffset = 0.01f;
        public int maxSegments = 96;
    }

    public class Mote_TiledBeamSheath : MoteDualAttached
    {
        private static readonly MaterialPropertyBlock PropertyBlock = new MaterialPropertyBlock();

        private MoteTiledBeamSheathExtension Props => def.GetModExtension<MoteTiledBeamSheathExtension>() ?? new MoteTiledBeamSheathExtension();

        protected override void DrawAt(Vector3 drawLoc, bool flip = false)
        {
            UpdatePositionAndRotation();

            Vector3 start = link1.LastDrawPos;
            Vector3 end = link2.LastDrawPos;
            Vector3 beam = (end - start).Yto0();
            float beamLength = beam.magnitude;
            if (beamLength <= 0.05f)
            {
                return;
            }

            MoteTiledBeamSheathExtension props = Props;
            float spacing = Mathf.Max(0.15f, props.segmentSpacing);
            int segmentCount = Mathf.Clamp(Mathf.CeilToInt(beamLength / spacing), 1, Mathf.Max(1, props.maxSegments));
            Vector3 direction = beam / beamLength;
            Vector3 perpendicular = direction.RotatedBy(90f);
            Quaternion rotation = Quaternion.LookRotation(direction, Vector3.up);
            Material material = Graphic.MatSingleFor(this);
            Color baseColor = Graphic.Color;
            float tickTime = def.mote.realTime ? Time.realtimeSinceStartup * 60f : Find.TickManager.TicksGame;
            float moteAlpha = Alpha * props.alpha;

            for (int i = 0; i < segmentCount; i++)
            {
                float distance = (i + 0.5f) * spacing;
                if (distance > beamLength)
                {
                    distance = beamLength - 0.01f;
                }

                float hashA = Hash01(offsetRandom, i, 17);
                float hashB = Hash01(offsetRandom, i, 43);
                float hashC = Hash01(offsetRandom, i, 91);
                float phase = Mathf.Repeat(tickTime * props.scrollSpeed / 60f + i * props.phaseStride + hashA, 1f);
                float expand = 1f - (1f - phase) * (1f - phase);
                float fade = Mathf.Pow(1f - phase, Mathf.Max(0.1f, props.alphaPower));
                float baseSize = props.baseSize > 0f ? props.baseSize : props.baseWidth;
                float expandedSize = props.expandedSize > 0f ? props.expandedSize : props.expandedWidth;
                float minSizeFactor = props.minSizeFactor > 0f ? props.minSizeFactor : props.minWidthFactor;
                float sizeJitterRange = props.sizeJitter >= 0f ? props.sizeJitter : props.widthJitter;
                float sizeJitter = Mathf.Lerp(1f - sizeJitterRange, 1f + sizeJitterRange, hashB);
                float size = Mathf.Lerp(baseSize * minSizeFactor, expandedSize, expand) * sizeJitter;
                float sideOffset = (hashA - 0.5f) * props.perpendicularJitter * expand;
                Vector3 center = start + direction * distance + perpendicular * sideOffset;
                center.y = drawLoc.y;

                float outerSizeFactor = props.outerSizeFactor > 0f ? props.outerSizeFactor : props.outerWidthFactor;
                DrawSegment(center, rotation, material, baseColor, size * outerSizeFactor, moteAlpha * fade * props.outerAlpha, 0f);

                if (props.drawInnerLayer && props.innerAlpha > 0f)
                {
                    Color innerColor = Color.Lerp(baseColor, Color.white, Mathf.Clamp01(props.innerWhiteBlend));
                    innerColor.a = baseColor.a;
                    float innerSizeFactor = props.innerSizeFactor > 0f ? props.innerSizeFactor : props.innerWidthFactor;
                    DrawSegment(center, rotation, material, innerColor, size * innerSizeFactor, moteAlpha * fade * props.innerAlpha, props.altitudeOffset);
                }
            }
        }

        private static void DrawSegment(Vector3 center, Quaternion rotation, Material material, Color baseColor, float size, float alphaFactor, float altitudeOffset)
        {
            Color color = baseColor;
            color.a *= alphaFactor;
            if (color.a <= 0.01f || size <= 0.01f)
            {
                return;
            }

            center.y += altitudeOffset;
            PropertyBlock.Clear();
            PropertyBlock.SetColor(ShaderPropertyIDs.Color, color);
            Matrix4x4 matrix = Matrix4x4.TRS(center, rotation, new Vector3(size, 1f, size));
            Graphics.DrawMesh(MeshPool.plane10, matrix, material, 0, null, 0, PropertyBlock);
        }

        private static float Hash01(int seed, int index, int salt)
        {
            unchecked
            {
                uint value = (uint)(seed * 73856093) ^ (uint)(index * 19349663) ^ (uint)(salt * 83492791);
                value ^= value << 13;
                value ^= value >> 17;
                value ^= value << 5;
                return (value & 0x00FFFFFF) / 16777215f;
            }
        }
    }
}
