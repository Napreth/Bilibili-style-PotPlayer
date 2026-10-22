using System.Collections.Generic;
namespace BilibiliPotPlayer
{
    // Feature modules consume media state without depending on media UI controls.
    internal interface IMediaContext
    {
        string SourcePath { get; }
        object Revision { get; }
        IList<ContainerCaption> CaptionTracks { get; }
        bool CameraCandidate { get; }
        double DisplayRatio { get; }
    }
}
