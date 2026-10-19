set -euo pipefail
cd /src
export NUGET_PACKAGES=/work/nuget DOTNET_PROCESSOR_COUNT=2 MSBUILDDISABLENODEREUSE=1 DOTNET_CLI_USE_MSBUILD_SERVER=0 UseArtifactsOutput=true ArtifactsPath=/work/capacity
unset DOTNET_GCHeapHardLimit DOTNET_gcServer
# 隔离程序集向上查找仓库标记；只创建指向只读 Main 的浅链接，不复制源码。
for entry in Full.NET.slnx src contracts Directory.Build.props Directory.Build.targets Directory.Packages.props global.json .editorconfig; do
 if [ -e "/work/$entry" ] || [ -L "/work/$entry" ]; then
  test "$(readlink "/work/$entry")" = "/src/$entry"
 else
  ln -s "/src/$entry" "/work/$entry"
 fi
done
mkdir -p /src/artifacts/native-aot
if [ -e /work/artifacts ] || [ -L /work/artifacts ]; then
 test "$(readlink /work/artifacts)" = /src/artifacts
else
 ln -s /src/artifacts /work/artifacts
fi
# 私有卷保留历史构建与日志；只归档本轮新写日志，避免每次复制整个历史目录。
runLogMarker=/tmp/fullnet-cache-capacity-started
touch "$runLogMarker"
function step(){
 local name="$1"; shift
 set +e
 "$@" > "/results/$name.log" 2>&1
 local code=$?
 set -e
 echo "$code" > "/results/$name.exit"
 if [ "$code" != 0 ]; then tail -35 "/results/$name.log"; exit "$code"; fi
}
function finish(){
 local original=$?
 trap - EXIT
 set +e
 sha256sum --check --quiet /task/source.sha256 > /results/source-final.log 2>&1
 local checked=$?
 echo "$checked" > /results/source-final.exit
 echo "$original" > /results/driver-original.exit
 local copied=0
 if [ -d artifacts/native-aot ]; then
  find artifacts/native-aot -type f -path '*/test-logs/*.log' -newer "$runLogMarker" -print -exec cp -a --parents -t /results {} + > /results/native-log-files.txt
  copied=$?
 fi
 echo "$copied" > /results/native-log-copy.exit
 if [ "$original" = 0 ] && [ "$checked" != 0 ]; then exit "$checked"; fi
 if [ "$original" = 0 ] && [ "$copied" != 0 ]; then exit "$copied"; fi
 exit "$original"
}
trap finish EXIT
step source-initial sha256sum --check --quiet /task/source.sha256
test "$(dotnet --version)" = '10.0.402'
# 只显式链接容量生命周期驱动，既有 Integration/Native 的源码及默认发现集合不变。
integration=/work/capacity/bin/Full.NET.IntegrationTests/release/Full.NET.IntegrationTests.dll
if [ -f /task/reuse-consumer.sha256 ]; then
 step reused-consumer sha256sum --check --quiet /task/reuse-consumer.sha256
else
 step driver-build dotnet build tests/Full.NET.IntegrationTests/Full.NET.IntegrationTests.csproj -c Release -p:RestoreLockedMode=true -p:UseArtifactsOutput=true -p:ArtifactsPath=/work/capacity -p:UseSharedCompilation=false -m:2 --nologo -p:CustomAfterMicrosoftCommonTargets=/task/harness/driver.targets
fi
sha256sum "$integration" > /results/consumer.sha256
filter='FullyQualifiedName~Full.NET.IntegrationTests.Capacity.NativeCacheCapacityDriver'
step discovery dotnet "$integration" --list-tests json --filter "$filter" --no-ansi --progress off
export TESTCONTAINERS_HOST_OVERRIDE=host.docker.internal FULLNET_TESTCONTAINERS_REUSE=0 TEMP=/tmp TMP=/tmp TMPDIR=/tmp
export DOTNET_GCHeapHardLimit=40000000 DOTNET_gcServer=0
step lifecycle dotnet "$integration" --filter "$filter" --minimum-expected-tests 2 --settings /task/sequential.runsettings --timeout 100m --no-ansi --progress off --report-trx --results-directory /results/lifecycle --report-trx-filename lifecycle.trx
