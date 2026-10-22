using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
using System.Windows.Forms;
namespace BilibiliPotPlayer
{
    internal sealed class BehaviorContext : ApplicationContext
    {
        readonly Dictionary<IntPtr, PlayerControls> sessions = new Dictionary<IntPtr, PlayerControls>();
        readonly Dictionary<IntPtr, IInputSession> inputSessions = new Dictionary<IntPtr, IInputSession>();
        readonly System.Windows.Forms.Timer timer = new System.Windows.Forms.Timer();
        readonly Stopwatch clock = Stopwatch.StartNew();
        readonly EventWaitHandle stop;
        readonly InputController input;
        long nextDiscovery;
        internal BehaviorContext(Profile profile, EventWaitHandle stop)
        {
            this.stop = stop;
            input = new InputController(profile, inputSessions);
            timer.Interval = 15; timer.Tick += Tick; timer.Start();
        }
        void Tick(object sender, EventArgs args)
        {
            if (stop.WaitOne(0)) { ExitThread(); return; }
            long now = clock.ElapsedMilliseconds;
            if (now >= nextDiscovery)
            {
                nextDiscovery = now + 500;
                if (!Native.AnyPlayerRunning()) { ExitThread(); return; }
                List<IntPtr> windows = Native.PlayerWindows();
                foreach (IntPtr window in new List<IntPtr>(sessions.Keys))
                    if (!windows.Contains(window))
                    { inputSessions.Remove(window); sessions[window].Dispose(); sessions.Remove(window); }
                foreach (IntPtr window in windows)
                    if (!sessions.ContainsKey(window))
                    {
                        PlayerControls session = new PlayerControls(window, input.SetBaseSpeed);
                        sessions.Add(window, session); inputSessions.Add(window, session);
                    }
            }
            input.Tick();
            IntPtr focused = input.FocusedPlayer();
            foreach (PlayerControls session in sessions.Values) session.Update(now, focused);
        }
        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                timer.Stop(); timer.Dispose(); input.Dispose();
                foreach (PlayerControls session in sessions.Values) session.Dispose();
                sessions.Clear(); inputSessions.Clear();
            }
            base.Dispose(disposing);
        }
    }
}
