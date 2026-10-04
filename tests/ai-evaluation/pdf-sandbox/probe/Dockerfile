# 与正式解析器相同的固定运行时；对抗探针不随正式镜像发布。
FROM mcr.microsoft.com/dotnet/runtime-deps@sha256:099f6f87ed745377dd27bd722f0d1a352bca71b4fddaabfd75e7c064bcaa82da
WORKDIR /app
COPY --chmod=0555 Full.NET.PdfSandbox.Probe /app/parser
USER 65532:65532
ENTRYPOINT ["/usr/bin/timeout", "--signal=KILL", "--kill-after=1", "30", "/app/parser"]
