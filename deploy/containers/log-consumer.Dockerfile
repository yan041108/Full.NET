# 独立消费者为 JIT 运维进程，不使用 API/Worker 镜像或业务装配。
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY Directory.Build.props Directory.Build.targets Directory.Packages.props global.json .editorconfig ./
COPY src/Platform/Full.NET.LogConsumer/ src/Platform/Full.NET.LogConsumer/
COPY src/Hosts/Full.NET.Host.LogConsumer/ src/Hosts/Full.NET.Host.LogConsumer/
RUN dotnet publish src/Hosts/Full.NET.Host.LogConsumer/Full.NET.Host.LogConsumer.csproj -c Release -p:FullNetPublishMode=Jit -o /out

FROM mcr.microsoft.com/dotnet/aspnet:10.0
WORKDIR /app
COPY --from=build /out .
USER $APP_UID
EXPOSE 8080
STOPSIGNAL SIGTERM
ENTRYPOINT ["dotnet", "Full.NET.Host.LogConsumer.dll"]
