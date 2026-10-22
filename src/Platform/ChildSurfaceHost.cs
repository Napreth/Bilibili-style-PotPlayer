using System;
using System.Collections.Generic;

namespace BilibiliPotPlayer
{
    // Share the repaint exclusion between the three child controls, and restore only our style bit.
    internal static class ChildSurfaceHost
    {
        sealed class Lease { internal int Count; internal bool AddedClip; }
        static readonly Dictionary<IntPtr, Lease> parents = new Dictionary<IntPtr, Lease>();
        internal static void Acquire(IntPtr parent)
        {
            Lease lease;
            if (!parents.TryGetValue(parent, out lease))
            {
                int style = Native.GetWindowLong(parent, -16);
                lease = new Lease { AddedClip = (style & 0x02000000) == 0 };
                if (lease.AddedClip) Native.SetWindowLongPtr(parent, -16, (IntPtr)(style | 0x02000000));
                parents.Add(parent, lease);
            }
            lease.Count++;
        }
        internal static void Release(IntPtr parent)
        {
            Lease lease;
            if (!parents.TryGetValue(parent, out lease) || --lease.Count > 0) return;
            parents.Remove(parent);
            if (lease.AddedClip && Native.IsWindow(parent))
                Native.SetWindowLongPtr(parent, -16, (IntPtr)(Native.GetWindowLong(parent, -16) & ~0x02000000));
        }
    }
}
