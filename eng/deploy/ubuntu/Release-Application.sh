#!/usr/bin/env bash
# 单角色普通发布：冻结候选并先渲染；不创建数据库、不播种或自动回滚迁移。
set -Eeuo pipefail
umask 077
[[ $EUID == 0 ]] || exit 2
[[ $# == 2 || ( $# == 3 && $3 == --plan ) ]] || { echo '用法：Release-Application.sh CHART migrator|worker|api [--plan]' >&2; exit 2; }
chart=$1
role=$2
[[ $role == migrator || $role == worker || $role == api ]] || exit 2
[[ -d $chart && -f $chart/Chart.yaml ]] || { echo '候选 Chart 目录无效。' >&2; exit 2; }
plan=false
if [[ $# == 3 ]]; then plan=true; fi
export KUBECONFIG=/etc/rancher/k3s/k3s.yaml
exec 9>/run/fullnet-release.lock
flock -n 9 || { echo '已有发布正在执行。' >&2; exit 1; }
work=$(mktemp -d /run/fullnet-release-XXXXXX)
restore_required=false
original=''
action=''
chart_digest=''
failure_logs=()

perform() {
  local phase=$1
  shift
  last_output="$work/$phase.log"
  if "$@" > "$last_output" 2>&1; then return 0; fi
  failure_logs+=("$last_output")
  echo "发布阶段失败：$phase" >&2
  return 1
}

gate_failure() {
  printf '%s\n' "$2" > "$work/$1.log"
  failure_logs+=("$work/$1.log")
  echo "$2" >&2
  return 1
}

restore_ddl() {
  [[ $restore_required == true ]] || return 0
  perform ddl-restore timeout --foreground --kill-after=5s 30s mysql --connect-timeout=5 --protocol=socket -uroot \
    -e "SET GLOBAL log_bin_trust_function_creators=$original;" || return 1
  perform ddl-restored-value timeout --foreground --kill-after=5s 30s mysql --connect-timeout=5 --protocol=socket -uroot \
    -NBe 'SELECT @@GLOBAL.log_bin_trust_function_creators;' || return 1
  [[ $(< "$last_output") == "$original" ]] || { gate_failure ddl-restore-value 'MySQL 临时 DDL 设置未恢复原值。'; return 1; }
  restore_required=false
}

finish() {
  local result=$?
  trap - EXIT
  if ! restore_ddl; then result=1; fi
  # 工作目录仅由本次 mktemp 创建；失败只保留有界原始诊断，不保留 values 或候选副本。
  if [[ ! -d $work || -L $work || $work != /run/fullnet-release-* ]]; then exit 1; fi
  if [[ $result == 0 ]]; then
    if ! rm -rf -- "$work"; then result=1; fi
  else
    if [[ ${#failure_logs[@]} == 0 ]]; then
      printf '发布中断或前置校验未完成。\n' > "$work/interrupted.log"
      failure_logs+=("$work/interrupted.log")
    fi
    for file in "$work"/*; do
      keep=false
      for diagnostic in "${failure_logs[@]}"; do [[ $file != "$diagnostic" ]] || keep=true; done
      if [[ $keep == true ]]; then
        if head -c 65536 "$file" > "$file.partial" && mv -- "$file.partial" "$file"; then chmod 0600 "$file"; else result=1; fi
      else
        rm -f -- "$file" || result=1
      fi
    done
    echo "发布未完成，私有诊断目录：$work" >&2
  fi
  if [[ $result == 0 ]]; then
    printf '{"schemaVersion":1,"role":"%s","namespace":"fullnet-production","action":"%s","chartSha256":"%s"}\n' "$role" "$action" "$chart_digest"
  fi
  exit "$result"
}
trap finish EXIT
trap 'exit 143' TERM INT

perform common-values cp -- /etc/fullnet/release/common.json "$work/common.json"
perform role-values cp -- "/etc/fullnet/release/$role.json" "$work/role.json"
perform package timeout --foreground --kill-after=5s 60s helm package "$chart" --destination "$work"
shopt -s nullglob
packages=("$work"/*.tgz)
[[ ${#packages[@]} == 1 && -f ${packages[0]} && ! -L ${packages[0]} ]] || { gate_failure package-file '必须得到唯一候选 Chart 包。'; exit 1; }
package=${packages[0]}
chart_digest=$(sha256sum "$package")
chart_digest=${chart_digest%% *}
# 显式绑定生产与单一角色，拒绝 values 漂移把 worker 作为 api Release 发布。
values=(-f "$work/common.json" -f "$work/role.json" --set production=true \
  --set roles.api=false,roles.worker=false,roles.migrator=false --set "roles.$role=true")
perform render timeout --foreground --kill-after=5s 60s helm template "fullnet-$role" "$package" \
  --namespace fullnet-production "${values[@]}"
if [[ $plan == true ]]; then action=plan; exit 0; fi

if [[ $role == migrator ]]; then
  perform backup systemctl start fullnet-backup.service
  perform backup-result systemctl show fullnet-backup.service -p Result --value
  [[ $(< "$last_output") == success ]] || { gate_failure backup-result-value '备份服务没有成功完成。'; exit 1; }
  perform ddl-original timeout --foreground --kill-after=5s 30s mysql --connect-timeout=5 --protocol=socket -uroot \
    -NBe 'SELECT @@GLOBAL.log_bin_trust_function_creators;'
  original=$(< "$last_output")
  [[ $original == 0 || $original == 1 ]] || { gate_failure ddl-original-value 'MySQL 临时 DDL 原值无效。'; exit 1; }
  # 在 SET 之前登记恢复义务；响应丢失也不能遗漏可能已经提交的全局设置。
  restore_required=true
  perform ddl-enable timeout --foreground --kill-after=5s 30s mysql --connect-timeout=5 --protocol=socket -uroot \
    -e 'SET GLOBAL log_bin_trust_function_creators=1;'
  perform ddl-enabled-value timeout --foreground --kill-after=5s 30s mysql --connect-timeout=5 --protocol=socket -uroot \
    -NBe 'SELECT @@GLOBAL.log_bin_trust_function_creators;'
  [[ $(< "$last_output") == 1 ]] || { gate_failure ddl-enable-value 'MySQL 临时 DDL 设置未生效。'; exit 1; }
fi
perform upgrade timeout --foreground --kill-after=5s 660s helm upgrade --install "fullnet-$role" "$package" \
  --namespace fullnet-production "${values[@]}" --wait --wait-for-jobs --timeout 10m
restore_ddl
if [[ $role == api && -f /etc/nginx/conf.d/fullnet.conf ]]; then
  perform gateway timeout --foreground --kill-after=5s 60s python3 /usr/local/lib/fullnet/Refresh-Edge.py
fi
action=applied
