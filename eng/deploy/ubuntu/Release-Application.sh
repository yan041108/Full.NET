#!/usr/bin/env bash
# 普通角色发布：一次冻结/渲染，批量按迁移、Worker、API 推进；不自动回滚业务状态。
set -Eeuo pipefail
umask 077
[[ $EUID == 0 ]] || exit 2
[[ $# == 2 || $# == 3 ]] || { echo '用法：Release-Application.sh CHART migrator|worker|api [--plan]；CHART all --plan|--ordinary-upgrade' >&2; exit 2; }
chart=$1
role=$2
requested_role=$role
[[ $role == migrator || $role == worker || $role == api || $role == all ]] || exit 2
mode=${3:-}
[[ -z $mode || $mode == --plan || ( $role == all && $mode == --ordinary-upgrade ) ]] || exit 2
[[ $role != all || $mode == --plan || $mode == --ordinary-upgrade ]] || { echo '批量须明确 --plan 或 --ordinary-upgrade；报表格式切版仍走专用门禁。' >&2; exit 2; }
[[ -d $chart && -f $chart/Chart.yaml ]] || { echo '候选 Chart 目录无效。' >&2; exit 2; }
plan=false
if [[ $mode == --plan ]]; then plan=true; fi
roles=("$role")
if [[ $role == all ]]; then roles=(migrator worker api); fi
export KUBECONFIG=/etc/rancher/k3s/k3s.yaml
exec 9>/run/fullnet-release.lock
flock -n 9 || { echo '已有发布正在执行。' >&2; exit 1; }
work=$(mktemp -d /run/fullnet-release-XXXXXX)
restore_required=false
original=''
action=''
chart_digest=''
chart_content_digest=''
failure_logs=()
completed_roles=()
values_digest_json=''
candidate_image_tag_json=null

roles_json() {
  local separator=''
  printf '['
  for member in "$@"; do printf '%s"%s"' "$separator" "$member"; separator=','; done
  printf ']'
}

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
  if [[ $result != 0 ]]; then
    printf '{"status":"failed","requestedRole":"%s","completedRoles":%s,"ddlRestorePending":%s}\n' \
      "$requested_role" "$(roles_json "${completed_roles[@]}")" "$restore_required" >&2
  fi
  if [[ $result == 0 ]]; then
    printf '{"schemaVersion":1,"role":"%s","roles":%s,"completedRoles":%s,"namespace":"fullnet-production","action":"%s","chartSha256":"%s","chartContentSha256":"%s","valuesSha256":{%s},"candidateImageTag":%s}\n' \
      "$requested_role" "$(roles_json "${roles[@]}")" "$(roles_json "${completed_roles[@]}")" "$action" "$chart_digest" "$chart_content_digest" "$values_digest_json" "$candidate_image_tag_json"
  fi
  exit "$result"
}
trap finish EXIT
trap 'exit 143' TERM INT

perform common-values cp -- /etc/fullnet/release/common.json "$work/common.json"
for role in "${roles[@]}"; do
  perform "$role-values" cp -- "/etc/fullnet/release/$role.json" "$work/$role.json"
done
if [[ $requested_role == all ]]; then
  # 批量共用一个候选；禁止角色覆盖成不同版本，且不依赖 Chart 的隐含默认镜像标签。
  perform batch-image-version python3 - "$work/common.json" "$work/migrator.json" "$work/worker.json" "$work/api.json" <<'PY'
import json,re,sys
def unique_object(pairs):
    result={}
    for key,value in pairs:
        if key in result: raise ValueError()
        result[key]=value
    return result
def invalid_constant(_value):
    raise ValueError()
try:
    documents=[]
    for filename in sys.argv[1:]:
        with open(filename, encoding='utf-8') as stream:
            content=stream.read(1048577)
        if len(content)>1048576: raise ValueError()
        document=json.loads(content,object_pairs_hook=unique_object,parse_constant=invalid_constant)
        if not isinstance(document,dict) or not isinstance(document.get('image',{}),dict): raise ValueError()
        documents.append(document.get('image',{}))
    identities=[]
    for overlay in documents[1:]:
        image={**documents[0],**overlay}
        tag=image.get('tag')
        if not isinstance(tag,str) or not re.fullmatch(r'[A-Za-z0-9_][A-Za-z0-9_.-]{0,127}',tag) or tag.lower()=='latest': raise ValueError()
        identity=(image.get('registry',''),image.get('repository'),tag)
        if any(value is not None and not isinstance(value,str) for value in identity): raise ValueError()
        identities.append(identity)
    if len(set(identities))!=1: raise ValueError()
    print(json.dumps(identities[0][2]))
except (ValueError,TypeError,OSError):
    print('批量 values 必须为有界 JSON 对象，并显式解析为同一镜像 registry/repository/tag，禁止 latest。',file=sys.stderr)
    sys.exit(1)
PY
  # 只保存已通过格式与一致性检查的 JSON 字符串；暂存清理后仍可签发相同候选回执。
  candidate_image_tag_json=$(< "$last_output")
fi
perform package timeout --foreground --kill-after=5s 60s helm package "$chart" --destination "$work"
shopt -s nullglob
packages=("$work"/*.tgz)
[[ ${#packages[@]} == 1 && -f ${packages[0]} && ! -L ${packages[0]} ]] || { gate_failure package-file '必须得到唯一候选 Chart 包。'; exit 1; }
package=${packages[0]}
chart_digest=$(sha256sum "$package")
chart_digest=${chart_digest%% *}
perform chart-content python3 - "$package" <<'PY'
import hashlib,json,sys,tarfile
try:
    manifest={}
    with tarfile.open(sys.argv[1],'r:gz') as archive:
        for member in archive:
            if member.isdir(): continue
            if not member.isfile() or member.name in manifest: raise ValueError()
            if member.name.startswith('/') or any(part in ('','.', '..') for part in member.name.split('/')): raise ValueError()
            if len(manifest)>=4096 or member.size>16777216: raise ValueError()
            content=archive.extractfile(member)
            digest=hashlib.sha256()
            size=0
            while chunk:=content.read(65536):
                size+=len(chunk)
                digest.update(chunk)
            if size!=member.size: raise ValueError()
            manifest[member.name]={'size':size,'sha256':digest.hexdigest()}
    if not manifest: raise ValueError()
    # 路径与原始文件字节纳入身份；忽略 tar/gzip 的打包时间、属主及权限元数据。
    canonical=json.dumps(manifest,sort_keys=True,separators=(',',':'),ensure_ascii=True).encode('ascii')
    print(hashlib.sha256(canonical).hexdigest())
except (OSError,ValueError,tarfile.TarError,EOFError):
    print('候选 Chart 内容不能生成稳定身份。',file=sys.stderr)
    sys.exit(1)
PY
chart_content_digest=$(< "$last_output")
frozen_files=("$package" "$work/common.json")
values_digest=$(sha256sum "$work/common.json")
values_digest_json="\"common\":\"${values_digest%% *}\""
for role in "${roles[@]}"; do
  frozen_files+=("$work/$role.json")
  values_digest=$(sha256sum "$work/$role.json")
  values_digest_json+=",\"$role\":\"${values_digest%% *}\""
done
perform frozen-digests sha256sum "${frozen_files[@]}"
digest_file=$last_output

verify_frozen() {
  perform frozen-inputs sha256sum --check "$digest_file"
}

select_values() {
  # 每个 Release 强制生产及单一角色，不能由原 values 同时启用其他宿主。
  values=(-f "$work/common.json" -f "$work/$role.json" --set production=true \
    --set roles.api=false,roles.worker=false,roles.migrator=false --set "roles.$role=true")
}

for role in "${roles[@]}"; do
  verify_frozen
  select_values
  perform "$role-render" timeout --foreground --kill-after=5s 60s helm template "fullnet-$role" "$package" \
    --namespace fullnet-production "${values[@]}"
done
verify_frozen
if [[ $plan == true ]]; then action=plan; exit 0; fi

for role in "${roles[@]}"; do
  verify_frozen
  select_values
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
  verify_frozen
  perform "$role-upgrade" timeout --foreground --kill-after=5s 660s helm upgrade --install "fullnet-$role" "$package" \
    --namespace fullnet-production "${values[@]}" --wait --wait-for-jobs --timeout 10m
  restore_ddl
  if [[ $role == api && -f /etc/nginx/conf.d/fullnet.conf ]]; then
    perform gateway timeout --foreground --kill-after=5s 60s python3 /usr/local/lib/fullnet/Refresh-Edge.py
  fi
  completed_roles+=("$role")
done
action=applied
