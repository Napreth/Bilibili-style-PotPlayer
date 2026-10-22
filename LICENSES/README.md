# Third-party notices

Original project code is MIT licensed. This does not relicense the following components or trademarks.

* **FFmpeg 8.0.1**: the distributed avcodec, avformat and avutil DLLs are built from the unmodified official source under LGPL 2.1 or later. GPL, nonfree, version-3-only components, external libraries, networking and programs are disabled. Only text subtitle decoders and the SubRip encoder are enabled. Dynamic libraries may be replaced with ABI-compatible builds; this project imposes no restriction on reverse engineering to debug modifications to those libraries. The exact source archive, configuration and build script accompany the installer in `Extension/BilibiliBehaviors/third-party/`; the [LGPL text](LICENSE.LGPL-2.1.txt) and all third-party notices are in the adjacent `LICENSES/`. See [FFmpeg licensing](https://ffmpeg.org/legal.html) and [official source](https://ffmpeg.org/releases/ffmpeg-8.0.1.tar.xz).
* **MinGW-w64 winpthreads support**: the toolchain's thread/atomic support is linked into the FFmpeg DLLs under its MIT/BSD-style terms; the relevant notice is included as `Winpthreads-LICENSE.txt`. It is not a GPL FFmpeg component.
* **GCC runtime support**: compiler support code is covered by the GCC Runtime Library Exception to GPLv3; `GCC-Runtime-Exception.txt` and `GCC-GPL-3.txt` accompany the package. This is separate from FFmpeg's optional GPL components, which are disabled.
* **MediaInfo**: bundled x64 library and its accompanying [MediaInfo.License.html](MediaInfo.License.html), supplied by MediaArea. See [MediaInfo](https://mediaarea.net/MediaInfo).
* **PotPlayer skin resources**: `skin/base/Default.dsf` is the fixed default-skin resource base used by this project. Original resources retain their owners' rights; the installer does not contain the PotPlayer executable or playback engine.
* **Dolby and Bilibili artwork/trademarks**: retain their owners' rights and are outside the original-code MIT license. Sources are documented in `src/Platform/assets/`.

The LGPL libraries are shared libraries, not copied into a managed assembly. `BilibiliTimedText.dll` is this project's native adapter using public FFmpeg APIs; its source is in `src/Native/TimedText/`.
