using System;
namespace BilibiliPotPlayer
{
    internal static class Program
    {
        [STAThread] static int Main(string[] args) { return BehaviorRuntime.Run(args); }
    }
}
