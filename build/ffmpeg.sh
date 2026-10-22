#!/usr/bin/env bash
set -euo pipefail
# Run from the verified FFmpeg source directory, under MSYS2 or Git Bash.
./configure \
  --target-os=mingw32 --arch=x86_64 --cc=gcc \
  --disable-gpl --disable-nonfree --disable-version3 --disable-autodetect \
  --disable-static --enable-shared --disable-programs --disable-doc \
  --disable-debug --disable-x86asm --disable-network --disable-everything \
  --disable-pthreads --enable-w32threads --extra-ldflags=-static-libgcc \
  --extra-libs='-Wl,-Bstatic -lwinpthread -Wl,-Bdynamic' \
  --enable-avformat --enable-avcodec --enable-avutil \
  --disable-avdevice --disable-avfilter --disable-swscale --disable-swresample \
  --enable-protocol=file --enable-demuxers \
  --enable-decoder=ass,ssa,subrip,srt,webvtt,movtext,text,microdvd,subviewer,subviewer1,sami,realtext,jacosub,mpl2,pjs,vplayer,stl,ttml \
  --enable-encoder=subrip
"${MAKE:-make}" -j"${NUMBER_OF_PROCESSORS:-4}" V=0
