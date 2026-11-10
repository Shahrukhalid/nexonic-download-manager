#!/usr/bin/env bash
# Runs the app's own FFmpeg build on an Android emulator: the same job yt-dlp gives it (join video, audio, subtitles and
# chapters into one .mp4 without re-encoding), then checks the result with the app's ffprobe.
#   android/ffmpeg-check.sh FFMPEG_DIR SAMPLE_DIR   (FFMPEG_DIR has libffmpeg.so and libffprobe.so)
set -euo pipefail
B="$1"; S="$2"; D=/data/local/tmp/ndm-ffmpeg
adb shell "rm -rf $D && mkdir -p $D"
adb push "$B/libffmpeg.so" "$B/libffprobe.so" "$S/video.mp4" "$S/audio.m4a" "$S/en.vtt" "$S/chapters.txt" "$D/" >/dev/null
adb shell "chmod 755 $D/libffmpeg.so $D/libffprobe.so"
adb shell "cd $D && ./libffmpeg.so -hide_banner -version" | head -1
adb shell "cd $D && ./libffmpeg.so -hide_banner -loglevel error -y -i video.mp4 -i audio.m4a -i en.vtt -i chapters.txt \
  -map 0:v -map 1:a -map 2:0 -c copy -c:s mov_text -map_metadata 3 -movflags +faststart out.mp4"
info="$(adb shell "cd $D && ./libffprobe.so -hide_banner -show_streams -show_chapters out.mp4" 2>&1)"
for want in codec_name=h264 codec_name=aac codec_name=mov_text "TAG:title=One" "TAG:title=Two"; do
  grep -q "$want" <<<"$info" || { echo "$info"; echo "FAILED: $want missing from the joined file"; exit 1; }
done
echo "FFmpeg on Android joined video, audio, subtitles and chapters correctly."
