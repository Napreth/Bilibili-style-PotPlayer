using System;
using System.Collections.Generic;
using System.Reflection;
using BilibiliPotPlayer;
internal static class ModuleChecks
{
    internal static void Run(Action<bool, bool, string> equal)
    {
        ModuleCatalog.Validate();
        HashSet<string> names = new HashSet<string>(ModuleCatalog.Names);
        equal(true, names.Count == 7, "host validates seven feature/foundation DLLs plus runtime");
        foreach (string name in names)
        {
            Assembly assembly = Assembly.Load("BilibiliBehaviors." + name);
            foreach (AssemblyName reference in assembly.GetReferencedAssemblies())
            {
                equal(false, reference.Name == "BilibiliBehaviors.Runtime", name + " does not reference its runtime host");
                if (name == "Captions" || name == "Windowing" || name == "Input")
                    equal(false, reference.Name == "BilibiliBehaviors.Media" || reference.Name == "BilibiliBehaviors.Playback", name + " consumes contracts instead of media/playback controls");
            }
        }
    }
}
