using System;
using IL2CPU.API.Attribs;

namespace WeatherOS.Plugs
{
    [Plug(TargetName = "Cosmos.Core_Asm.CPUImpl, Cosmos.Core_Asm")]
    public static class CpuImplPlug
    {
        public static void DoEnableInterrupts()
        {
        }
    }

    [Plug(Target = typeof(Cosmos.Core.CPU))]
    public static class CpuPlug
    {
        public static void DoEnableInterrupts()
        {
        }

        public static void EnableInterrupts()
        {
        }
    }
}
