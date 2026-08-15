using System;
using System.Linq;
using RimWorld;
using UnityEngine;
using Verse;

namespace SRA
{
    public class VacDoorExtension : DefModExtension
    {
        // Prevent unreleased prisoners, including prison-break pawns with CanOpenAnyDoor,
        // from opening this door normally. This does not prevent attacking or destroying it.
        public bool preventPrisonerPrying = false;
    }

    public class Building_VacDoor : Building_SupportedDoor
    {
        private VacDoorExtension VacDoorProps
        {
            get
            {
                if (!this.vacDoorPropsResolved)
                {
                    this.vacDoorPropsCached = def.GetModExtension<VacDoorExtension>();
                    this.vacDoorPropsResolved = true;
                }

                return this.vacDoorPropsCached;
            }
        }

        public CompPowerTrader PowerTrader
        {
            get
            {
                if (!this.powerTraderResolved)
                {
                    this.powerTraderCached = base.GetComp<CompPowerTrader>();
                    this.powerTraderResolved = true;
                }

                return this.powerTraderCached;
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

        public override bool PawnCanOpen(Pawn p)
        {
            if (PreventsPrisonerPrying(p))
            {
                return false;
            }

            return base.PawnCanOpen(p);
        }

        public override bool ExchangeVacuum
        {
            get
            {
                return !this.IsAirtight || (base.Open && !this.PowerAvailable);
            }
        }
        protected override float TempEqualizeRate
        {
            get
            {
                if (!this.PowerAvailable)
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

        private bool PreventsPrisonerPrying(Pawn p)
        {
            return (VacDoorProps?.preventPrisonerPrying ?? false) &&
                   p != null &&
                   p.IsPrisoner &&
                   (p.guest == null || !p.guest.Released) &&
                   p.HostFaction == Faction;
        }

        private bool PowerAvailable => this.PowerTrader == null || this.PowerTrader.PowerOn;

        private VacDoorExtension vacDoorPropsCached;
        private bool vacDoorPropsResolved;
        private CompPowerTrader powerTraderCached;
        private bool powerTraderResolved;
        private VacuumComponent vacuumCached;
    }
}
