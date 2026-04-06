using System;
using UnityEngine;
using Verse;

namespace SRA
{
	public class Mote_ScaleAndRotate : Mote
	{
		protected override void DrawAt(Vector3 drawLoc, bool flip = false)
		{
			this.Graphic.Draw(drawLoc, base.Rotation, this, this.exactRotation);
		}

		protected override void TimeInterval(float deltaTime)
		{
			bool flag = this.EndOfLife && !base.Destroyed;
			if (flag)
			{
				this.Destroy(DestroyMode.Vanish);
			}
			else
			{
				bool flag2 = this.def.mote.needsMaintenance && Find.TickManager.TicksGame - 1 > this.lastMaintainTick;
				if (flag2)
				{
					int num = this.def.mote.fadeOutTime.SecondsToTicks();
					bool flag3 = !this.def.mote.fadeOutUnmaintained || Find.TickManager.TicksGame - this.lastMaintainTick > num;
					if (flag3)
					{
						this.Destroy(DestroyMode.Vanish);
						return;
					}
				}
				bool flag4 = this.def.mote.scalers != null;
				if (flag4)
				{
					this.curvedScale = this.def.mote.scalers.ScaleAtTime(base.AgeSecs);
				}
			}
		}
		public void MaintainMote()
		{
			this.lastMaintainTick = Find.TickManager.TicksGame;
		}
		protected override void Tick()
		{
			base.Tick();
			this.exactRotation = (float)Find.TickManager.TicksGame % 360f;
			bool flag = Mathf.Abs(this.tickimpact - this.tickspawned) > 0;
			if (flag)
			{
				this.currentscale = this.iniscale * ((float)(Find.TickManager.TicksGame - this.tickspawned) / (float)(this.tickimpact - this.tickspawned) * 0.5f + 1f);
				this.linearScale = new Vector3(this.currentscale, this.currentscale, this.currentscale);
				this.Graphic.drawSize = this.linearScale;
			}
			bool linked = this.link1.Linked;
			if (linked)
			{
				bool flag2 = this.detachAfterTicks == -1 || Find.TickManager.TicksGame - this.spawnTick < this.detachAfterTicks;
				bool flag3 = !this.link1.Target.ThingDestroyed && flag2;
				if (flag3)
				{
					this.link1.UpdateDrawPos();
					bool rotateWithTarget = this.link1.rotateWithTarget;
					if (rotateWithTarget)
					{
						base.Rotation = this.link1.Target.Thing.Rotation;
					}
				}
				Vector3 attachedDrawOffset = this.def.mote.attachedDrawOffset;
				this.exactPosition = this.link1.LastDrawPos + attachedDrawOffset;
				IntVec3 intVec = this.exactPosition.ToIntVec3();
				bool flag4 = base.Spawned && !intVec.InBounds(base.Map);
				if (flag4)
				{
					this.Destroy(DestroyMode.Vanish);
				}
				else
				{
					base.Position = intVec;
				}
			}
		}

		public float iniscale;

		public float currentscale;

		public int tickimpact;

		public int tickspawned;

		private int lastMaintainTick;
	}
}
