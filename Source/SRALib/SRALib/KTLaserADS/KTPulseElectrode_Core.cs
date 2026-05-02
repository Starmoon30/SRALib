using System.Collections.Generic;
using UnityEngine;
using Verse.Noise;
using Verse;

//核心逻辑
namespace SRA
{
    public class OrganDamageRule
    {
        public BodyPartTagDef tag;
        public float damageAmount;
        public DamageDef damageDef;
    }
    public class CompProperties_PulseElectrode : CompProperties
    {
        public string label = "Pulse Electrode";
        public string description = "Releases high-energy electrical arcs.";
        public string uiIconPath = "";
        public string uiIconPath_ModeOn = "";
        public string uiIconPath_ModeOff = "";
        public string uiIconPath_ForcedTarget = "UI/Commands/Attack";
        public string uiIconPath_CancelTarget = "UI/Designators/Cancel";
        public Vector2 turretOffset = Vector2.zero;
        public Vector2 arcStartOffset = Vector2.zero;
        public string turretTexPath = "";
        public float turretDrawSize = 1f;
        public float turnSpeed = 10f;
        public float range = 25f;
        public float minRange = 0f;
        public float baseRestAngle = 0f;
        public bool requireLineOfSight = true;
        public int postKillDelayTicks = 60;
        public DamageDef damageDef;
        public float damageAmount = 35f;
        public float armorPenetration = -1f;
        public float empDamageAmount = 20f;
        public float explosionRadius = 1.9f;
        public List<OrganDamageRule> organDamages = new List<OrganDamageRule>();
        public bool dessicateCorpse = true;
        public float igniteCorpseSize = 0.5f;
        public int cooldownTicks = 120;
        public SoundDef fireSound;
        public float perturbAmp = 2.5f;
        public float perturbFreq = 0.02f;
        public int arcDurationTicks = 15;
        public float arcSpacing = 0.6f;
        public float arcRailLength = 0f;
        public float arcThickness = 1.0f;
        public string lightningMatPath = "Weather/LightningBolt";
        public bool drawIdleArc = true;
        public float idleArcAmp = 0.3f;
        public float idleArcFreq = 0.05f;
        public float idleArcThickness = 0.4f;
        public CompProperties_PulseElectrode()
        {
            this.compClass = typeof(CompPulseElectrode);
        }
    }
    public class ActiveArcMesh
    {
        public Mesh mesh;
        public int ageTicks;
        public Vector3 renderStartPos;
        public ActiveArcMesh(Mesh m, Vector3 startPos)
        {
            this.mesh = m;
            this.renderStartPos = startPos;
            this.ageTicks = 0;
        }
    }
    public static class PulseArcMeshMaker
    {
        private static List<Vector3> vertBuffer = new List<Vector3>();
        private static List<Vector2> uvBuffer = new List<Vector2>();
        private static List<int> triBuffer = new List<int>();
        private static List<Vector2> centerBuffer = new List<Vector2>();
        private static Perlin noiseGen = new Perlin(1.0, 2.0, 0.5, 6, 42, QualityMode.High);
        public static void UpdateArcMesh(ref Mesh mesh, Vector3 start, Vector3 end, float perturbAmp, float perturbFreq, float thickness)
        {
            if (mesh == null)
            {
                mesh = new Mesh();
                mesh.name = "PulseArcMesh";
                mesh.MarkDynamic();
            }
            vertBuffer.Clear();
            uvBuffer.Clear();
            triBuffer.Clear();
            centerBuffer.Clear();
            Vector3 localEnd = (end - start).Yto0();
            float dist = localEnd.magnitude;
            if (dist < 0.1f)
            {
                mesh.Clear();
                return;
            }
            Vector3 dir = localEnd.normalized;
            Vector3 perp = new Vector3(-dir.z, 0, dir.x);
            int segments = Mathf.CeilToInt(dist / 0.25f);
            if (segments < 2) segments = 2;
            float zOffset = Rand.Range(0f, 10000f);
            for (int i = 0; i <= segments; i++)
            {
                float t = (float)i / segments;
                Vector3 basePos = dir * (t * dist);
                float fade = Mathf.Sin(t * Mathf.PI);
                float offset = (float)noiseGen.GetValue(i * perturbFreq, 0.0, zOffset) * perturbAmp * fade;
                Vector3 finalPos = basePos + perp * offset;
                centerBuffer.Add(new Vector2(finalPos.x, finalPos.z));
            }
            float halfWidth = thickness / 2f;
            Vector2 perp2D = new Vector2(perp.x, perp.z);
            for (int i = 0; i < centerBuffer.Count; i++)
            {
                vertBuffer.Add(new Vector3(centerBuffer[i].x - perp2D.x * halfWidth, 0, centerBuffer[i].y - perp2D.y * halfWidth));
                vertBuffer.Add(new Vector3(centerBuffer[i].x + perp2D.x * halfWidth, 0, centerBuffer[i].y + perp2D.y * halfWidth));
            }
            float v = 0f;
            for (int i = 0; i < centerBuffer.Count; i++)
            {
                uvBuffer.Add(new Vector2(0f, v));
                uvBuffer.Add(new Vector2(1f, v));
                v += 0.04f;
            }
            for (int i = 0; i < centerBuffer.Count - 1; i++)
            {
                int vi = i * 2;
                triBuffer.Add(vi);
                triBuffer.Add(vi + 1);
                triBuffer.Add(vi + 2);
                triBuffer.Add(vi + 2);
                triBuffer.Add(vi + 1);
                triBuffer.Add(vi + 3);
            }
            mesh.Clear();
            mesh.SetVertices(vertBuffer);
            mesh.SetUVs(0, uvBuffer);
            mesh.SetTriangles(triBuffer, 0);
            mesh.RecalculateBounds();
            mesh.bounds = new Bounds(mesh.bounds.center, new Vector3(1000f, 10f, 1000f));
        }
        public static Mesh GenerateArcMesh(Vector3 start, Vector3 end, float perturbAmp, float perturbFreq, float thickness)
        {
            Mesh m = null;
            UpdateArcMesh(ref m, start, end, perturbAmp, perturbFreq, thickness);
            return m;
        }
    }
}