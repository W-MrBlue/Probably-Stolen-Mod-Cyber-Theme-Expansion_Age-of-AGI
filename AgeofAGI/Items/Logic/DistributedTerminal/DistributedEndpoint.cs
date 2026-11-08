using System;
using AgeOfAgi.Items.Logic.Server;

namespace AgeOfAgi.Items.Logic.DistributedTerminal
{
    internal sealed class DistributedEndpoint
    {
        internal GameItem Machine;
        internal GameSlotInventory Battery, Network;
        internal Func<ServerHardware> ReadHardware;
        internal Func<bool> CanWork;
        internal Action Run, Refresh, PrepareCycle;
        internal bool Terminal, CyclePending;

        internal bool CanSupply(bool paid)
        {
            ServerHardware hardware = ReadHardware();
            return !Terminal && GeneralHelper.IsItemOwned(Machine) && hardware != null &&
                Battery.childItem != null && GeneralHelper.IsItemOwned(Battery.childItem) &&
                (paid || PowerHelper.CanDrawPowerSource(Battery.childItem, hardware.Power));
        }
    }
}
