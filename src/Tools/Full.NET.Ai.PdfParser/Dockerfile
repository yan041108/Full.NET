# 只接收本工具的已验证 linux-x64 Native AOT 发布目录；不带源码、SDK 或业务配置。
FROM mcr.microsoft.com/dotnet/runtime-deps@sha256:099f6f87ed745377dd27bd722f0d1a352bca71b4fddaabfd75e7c064bcaa82da
WORKDIR /app
COPY --chmod=0555 Full.NET.Ai.PdfParser /app/parser
COPY licenses/ /app/licenses/
USER 65532:65532
ENTRYPOINT ["/usr/bin/timeout", "--signal=KILL", "--kill-after=1", "30", "/app/parser"]
