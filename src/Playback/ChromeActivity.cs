using System;

namespace BilibiliPotPlayer
{
    internal sealed class ChromeActivity
    {
        internal const int IdleMilliseconds = 3000;
        bool initialized, wasInside, wasTimed;
        int x, y;
        long moved;
        internal bool Visible(long now, int px, int py, bool inside, bool timed, bool interacting)
        {
            if (!initialized || px != x || py != y || inside && !wasInside || timed != wasTimed) moved = now;
            initialized = true; x = px; y = py; wasInside = inside; wasTimed = timed;
            if (interacting) { moved = now; return true; }
            return inside && (!timed || now - moved < IdleMilliseconds);
        }
    }
}
