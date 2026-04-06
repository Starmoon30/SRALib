using System;
using RimWorld;
using Verse;

namespace SRA
{
    public class Building_TempControler : Building_TempControl
    {
        public override void TickRare()
        {
            bool hasPower = compPowerTrader == null || compPowerTrader.PowerOn;
            if (!hasPower || compTempControl == null || this.GetRoom(RegionType.Set_Passable) == null)
            {
                return;
            }
            this.GetRoom(RegionType.Set_Passable).Temperature = this.compTempControl.targetTemperature;
        }
    }
}
