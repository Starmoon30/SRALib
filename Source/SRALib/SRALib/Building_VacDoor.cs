using System;
using System.Linq;
using RimWorld;
using UnityEngine;
using Verse;

namespace SRA
{
    public class Building_VacDoor : Building_SupportedDoor
    {
        public CompPowerTrader PowerTrader
        {
            get
            {
                CompPowerTrader result;
                if ((result = this.powerTraderCached) == null)
                {
                    result = (this.powerTraderCached = base.GetComp<CompPowerTrader>());
                }
                return result;
            }
        }
        public VacuumComponent Vacuum
        {
            get
            {
                VacuumComponent result;
                if ((result = this.vacuumCached) == null)
                {
                    Map mapHeld = base.MapHeld;
                    result = (this.vacuumCached = ((mapHeld != null) ? mapHeld.GetComponent<VacuumComponent>() : null));
                }
                return result;
            }
        }
        public override bool ExchangeVacuum
        {
            get
            {
                return !this.IsAirtight || (base.Open && !this.PowerTrader.PowerOn);
            }
        }
        protected override float TempEqualizeRate
        {
            get
            {
                if (!this.PowerTrader.PowerOn)
                {
                    return base.TempEqualizeRate;
                }
                return 0f;
            }
        }
        protected override void ReceiveCompSignal(string signal)
        {
            if (signal == "PowerTurnedOn" || signal == "PowerTurnedOff")
            {
                VacuumComponent vacuum = this.Vacuum;
                if (vacuum == null)
                {
                    return;
                }
                vacuum.Dirty();
            }
        }
        private CompPowerTrader powerTraderCached;
        private VacuumComponent vacuumCached;
    }
}
