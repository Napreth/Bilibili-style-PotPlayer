using System;
using System.Reflection;
namespace BilibiliPotPlayer
{
    internal static class ModuleCatalog
    {
        internal static readonly string[] Names = { "Contracts", "Platform", "Input", "Windowing", "Media", "Captions", "Playback", "Settings" };
        internal static void Validate()
        {
            Version expected = typeof(BehaviorRuntime).Assembly.GetName().Version;
            foreach (string name in Names)
            {
                Assembly module = Assembly.Load("BilibiliBehaviors." + name);
                if (module.GetName().Version != expected)
                    throw new InvalidOperationException("Module version mismatch: " + name);
            }
        }
    }
}
