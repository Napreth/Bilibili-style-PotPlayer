# FFmpeg shared-library build

Run `build/ffmpeg.ps1 -Rebuild` with x64 MinGW-w64 GCC, GNU Make and Bash. The archive URL and SHA-256 are pinned in `build/dependencies.lock.json`; the exact configure invocation is in `build/ffmpeg.sh`. Runtime license checks verify an LGPL build without `--enable-gpl` or `--enable-nonfree`.

Only `avcodec-62.dll`, `avformat-62.dll` and `avutil-60.dll` are shipped. No ffmpeg executable is installed or discovered on PATH. Import libraries are build inputs, not runtime payload.

Third-party license texts and notices are kept in `LICENSES/` at the repository root and beside the installed host. The installer includes the original FFmpeg source archive, build script and generated configuration in `third-party/`. Rebuilds use Windows thread primitives and statically link the toolchain's MIT-licensed winpthreads support where GCC atomics require it.
