# Nexonic Download Manager server: no window, managed from a phone or browser (see docs/SERVER.md).
#   docker build -f docker/Dockerfile -t nexonic-server .
#   docker run -d -p 47813:47813 -v nexonic-data:/data -v /path/to/downloads:/downloads nexonic-server
FROM mcr.microsoft.com/dotnet/sdk:10.0@sha256:212fe4cb74627d5adf9817a4088906ae3edff1bd853bb27565d4b46731efe61e AS build
ARG TARGETARCH
WORKDIR /src
COPY VERSION ./
COPY windows/Directory.Build.props windows/
COPY windows/Core windows/Core
COPY linux/Directory.Build.props linux/
COPY linux/Desktop linux/Desktop
COPY linux/Server linux/Server
COPY scripts/fetch-video-tools.sh scripts/
RUN case "$TARGETARCH" in arm64) RID=linux-arm64 ;; *) RID=linux-x64 ;; esac \
 && dotnet publish linux/Server/Server.csproj -c Release -r "$RID" --self-contained false -p:Version="$(cat VERSION)" \
      -p:DebugType=embedded -o /app -nologo \
 && bash scripts/fetch-video-tools.sh "$RID" /tools \
 && cp /tools/yt-dlp /app/nexonic-video-engine && cp /tools/qjs /app/ \
 && if [ -f /tools/NOTICE-video-tools-2.txt ]; then cp /tools/NOTICE-video-tools-2.txt /app/; fi

FROM mcr.microsoft.com/dotnet/runtime:10.0@sha256:4f1f30cf0bb4ab0c8b2daa84613051683456f8df4a9bf7c812e20d74962992ac
# ffmpeg joins the parts of streamed videos (Debian's package; its source is in Debian's archive).
RUN apt-get update && apt-get install -y --no-install-recommends ffmpeg ca-certificates && rm -rf /var/lib/apt/lists/* \
 && mkdir -p /data /downloads && chown app:app /data /downloads
COPY --from=build /app /app
COPY LICENSE LICENSE-APACHE packaging/THIRD-PARTY-NOTICES.txt /app/
RUN ln -s /usr/bin/ffmpeg /app/ffmpeg && ln -s /usr/bin/ffprobe /app/ffprobe && chmod 755 /app/nexonic-video-engine /app/qjs
ENV XDG_DATA_HOME=/data HOME=/data NDM_DOWNLOADS=/downloads NDM_PORT=47813
VOLUME ["/data", "/downloads"]
EXPOSE 47813
USER app
WORKDIR /app
ENTRYPOINT ["/app/nexonic-server"]
