# syntax=docker/dockerfile:1.7

# Full.NET 生产镜像：同一 SDK build stage，三个 final target（api / worker / migrator）。
# 最终层只复制 publish 产物，并以非 root 用户运行。

ARG DOTNET_VERSION=10.0

FROM mcr.microsoft.com/dotnet/sdk:${DOTNET_VERSION} AS build
ARG BUILD_CONFIGURATION=Release
ARG SOURCE_COMMIT=unknown
WORKDIR /src

COPY Directory.Build.props Directory.Packages.props ./
COPY contracts/ ./contracts/
COPY src/ ./src/

RUN dotnet restore src/Hosts/Full.NET.Host.Api/Full.NET.Host.Api.csproj \
 && dotnet restore src/Hosts/Full.NET.Host.Worker/Full.NET.Host.Worker.csproj \
 && dotnet restore src/Hosts/Full.NET.Host.Migrator/Full.NET.Host.Migrator.csproj

RUN dotnet publish src/Hosts/Full.NET.Host.Api/Full.NET.Host.Api.csproj \
      -c ${BUILD_CONFIGURATION} \
      -o /app/publish/api \
      --no-restore \
      /p:SourceRevisionId=${SOURCE_COMMIT} \
 && dotnet publish src/Hosts/Full.NET.Host.Worker/Full.NET.Host.Worker.csproj \
      -c ${BUILD_CONFIGURATION} \
      -o /app/publish/worker \
      --no-restore \
      /p:SourceRevisionId=${SOURCE_COMMIT} \
 && dotnet publish src/Hosts/Full.NET.Host.Migrator/Full.NET.Host.Migrator.csproj \
      -c ${BUILD_CONFIGURATION} \
      -o /app/publish/migrator \
      --no-restore \
      /p:SourceRevisionId=${SOURCE_COMMIT}

FROM mcr.microsoft.com/dotnet/aspnet:${DOTNET_VERSION} AS api
ARG SOURCE_COMMIT=unknown
WORKDIR /app
COPY --from=build /app/publish/api .
ENV ASPNETCORE_URLS=http://+:8080 \
    DOTNET_EnableDiagnostics=0 \
    FULLNET_SOURCE_COMMIT=${SOURCE_COMMIT}
EXPOSE 8080
LABEL org.opencontainers.image.title="Full.NET API" \
      org.opencontainers.image.revision="${SOURCE_COMMIT}" \
      org.opencontainers.image.source="https://github.com/full-net/Full.NET"
USER $APP_UID
ENTRYPOINT ["dotnet", "Full.NET.Host.Api.dll"]

FROM mcr.microsoft.com/dotnet/aspnet:${DOTNET_VERSION} AS worker
ARG SOURCE_COMMIT=unknown
WORKDIR /app
COPY --from=build /app/publish/worker .
ENV DOTNET_EnableDiagnostics=0 \
    FULLNET_SOURCE_COMMIT=${SOURCE_COMMIT}
LABEL org.opencontainers.image.title="Full.NET Worker" \
      org.opencontainers.image.revision="${SOURCE_COMMIT}" \
      org.opencontainers.image.source="https://github.com/full-net/Full.NET"
USER $APP_UID
ENTRYPOINT ["dotnet", "Full.NET.Host.Worker.dll"]

FROM mcr.microsoft.com/dotnet/aspnet:${DOTNET_VERSION} AS migrator
ARG SOURCE_COMMIT=unknown
WORKDIR /app
COPY --from=build /app/publish/migrator .
ENV DOTNET_EnableDiagnostics=0 \
    FULLNET_SOURCE_COMMIT=${SOURCE_COMMIT}
LABEL org.opencontainers.image.title="Full.NET Migrator" \
      org.opencontainers.image.revision="${SOURCE_COMMIT}" \
      org.opencontainers.image.source="https://github.com/full-net/Full.NET"
USER $APP_UID
ENTRYPOINT ["dotnet", "Full.NET.Host.Migrator.dll"]
