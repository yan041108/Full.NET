# 仅消费已验证的 linux-x64 Native AOT 工具，不携带源码、SDK、模型或业务凭据。
FROM mcr.microsoft.com/dotnet/runtime-deps@sha256:099f6f87ed745377dd27bd722f0d1a352bca71b4fddaabfd75e7c064bcaa82da
WORKDIR /app
COPY --chmod=0555 Full.NET.Ai.DocxParser /app/parser
COPY licenses/ /app/licenses/
USER 65532:65532
ENTRYPOINT ["/usr/bin/timeout", "--signal=KILL", "--kill-after=1", "30", "/app/parser"]
