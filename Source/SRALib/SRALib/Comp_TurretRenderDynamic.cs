using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using UnityEngine;
using Verse;

namespace SRA
{
    public class CompProperties_TurretRenderDynamic : CompProperties
    {
        public CompProperties_TurretRenderDynamic()
        {
            this.compClass = typeof(Comp_TurretRenderDynamic);
        }

        public string texturePath;
        public int totalFrames = 1;
        public int ticksPerFrame = 15;
        public Vector2 drawSize = Vector2.one;
        public Vector3 offset = Vector3.zero;
        public bool useGlowShader = true;
        public Color color = Color.white;
    }
    public class Comp_TurretRenderDynamic : ThingComp
    {
        private CompProperties_TurretRenderDynamic Props => (CompProperties_TurretRenderDynamic)this.props;

        private Material cachedMaterial;
        private int currentFrame;
        private int lastFrameTick;

        public Material LightMaterial
        {
            get
            {
                if (cachedMaterial == null)
                {
                    Shader shader = Props.useGlowShader ? ShaderDatabase.MoteGlow : ShaderDatabase.DefaultShader;
                    cachedMaterial = MaterialPool.MatFrom(Props.texturePath, shader, Props.color);
                }
                return cachedMaterial;
            }
        }

        public void DrawLight(Vector3 drawPos, Quaternion rotation, float turretTopDrawSize)
        {
            // 更新帧动画
            if (Find.TickManager.TicksGame > lastFrameTick + Props.ticksPerFrame)
            {
                currentFrame = (currentFrame + 1) % Props.totalFrames;
                lastFrameTick = Find.TickManager.TicksGame;
            }

            // 设置纹理偏移和缩放用于序列帧
            Vector2 textureScale = new Vector2(1f / Props.totalFrames, 1f);
            Vector2 textureOffset = new Vector2(currentFrame * textureScale.x, 0f);

            Material material = LightMaterial;
            material.mainTextureOffset = textureOffset;
            material.mainTextureScale = textureScale;

            // 计算绘制位置和大小
            Vector3 lightOffset = Props.offset;
            Vector3 finalDrawPos = drawPos + lightOffset;

            Vector3 scale = new Vector3(
                Props.drawSize.x * turretTopDrawSize,
                1f,
                Props.drawSize.y * turretTopDrawSize
            );

            // 绘制发光部分
            Graphics.DrawMesh(MeshPool.plane10, Matrix4x4.TRS(finalDrawPos, rotation, scale), material, 0);
        }

        public override void PostExposeData()
        {
            Scribe_Values.Look(ref currentFrame, "currentFrame", 0);
            Scribe_Values.Look(ref lastFrameTick, "lastFrameTick", 0);
            base.PostExposeData();
        }
    }
}
