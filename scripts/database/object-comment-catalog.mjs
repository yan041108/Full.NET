/**
 * Full.NET 数据库对象中文注释目录：根据表名与列名生成可读说明，供迁移脚本与门禁共用。
 */

const MODULE_LABELS = {
  auditing: '审计',
  codegeneration: '代码生成',
  document: '文档',
  files: '文件',
  identity: '身份认证',
  jobs: '后台任务',
  messaging: '消息投递',
  notifications: '通知',
  organization: '组织机构',
  outbox: '事务发件箱',
  seed: '种子数据',
  serialnumbers: '序列号',
  settings: '系统设置',
  tenancy: '租户',
  tenant: '租户',
  uuid: 'UUID 契约',
  pre_v1_naming: '命名契约',
};

const ENTITY_LABELS = {
  access_log: '访问日志',
  allocation: '分配记录',
  announcement: '公告',
  api_key: 'API 密钥',
  auth_audit: '认证审计',
  category: '分类',
  config_entry: '配置项',
  counter: '计数器',
  dead_letter: '死信',
  definition: '定义',
  dict_item: '字典项',
  dict_type: '字典类型',
  domain_audit: '领域审计',
  exception_log: '异常日志',
  execution: '执行记录',
  file: '文件',
  file_reference_claim: '文件引用声明',
  grid_preference: '表格偏好',
  inbox_message: '收件箱消息',
  item: '条目',
  message: '消息',
  navigation: '导航菜单',
  operation_log: '操作日志',
  organization_unit_projection: '机构单元投影',
  outbound_call: '出站调用',
  outbox_event: '发件箱事件',
  package: '套餐',
  permission: '权限',
  position: '岗位',
  position_level: '职级',
  refresh_session: '刷新会话',
  role: '角色',
  role_data_scope_unit: '角色数据范围机构',
  role_field_grant: '角色字段授权',
  role_permission: '角色权限',
  rule: '规则',
  run: '运行',
  run_item: '运行明细',
  schedule: '调度',
  share: '分享',
  signature_nonce: '签名随机数',
  stream_ownership: '流所有权',
  tag: '标签',
  tag_assignment: '标签关联',
  template: '模板',
  tenant: '租户',
  tenant_package: '租户套餐',
  unit: '机构单元',
  user: '用户',
  user_position: '用户岗位',
  user_profile: '用户资料',
  user_role: '用户角色',
  user_totp: '用户 TOTP',
  user_unit: '用户机构',
  version: '版本',
  contract_state: '契约状态',
};

const COLUMN_LABELS = {
  AccessCount: '访问次数',
  AccessKeyId: '访问密钥标识',
  AccountType: '账户类型',
  ActionKey: '操作键',
  ActiveTenantId: '当前活动租户标识',
  ActorDisplayName: '操作者显示名',
  ActorUserId: '操作者用户标识',
  Address: '地址',
  AllocatedAtUtc: '分配时间(UTC)',
  ApplicationVersion: '应用版本',
  Args: '调度参数(JSON)',
  ArtifactCount: '产物数量',
  AttemptCount: '尝试次数',
  Attempts: '重试次数',
  BirthDate: '出生日期',
  Caption: '显示标题',
  CategoryColor: '分类颜色',
  CategoryId: '分类标识',
  CategoryName: '分类名称',
  CausationId: '因果关联标识',
  CdcSourcePositionJson: 'CDC 源位置(JSON)',
  ChangeDescription: '变更说明',
  ClientId: '客户端标识',
  ClientIpFingerprint: '客户端 IP 指纹',
  Code: '编码',
  Color: '颜色',
  ColumnsJson: '列配置(JSON)',
  CompletedAt: '完成时间',
  CompletedAtUtc: '完成时间(UTC)',
  ComponentKey: '组件键',
  ConfigKey: '配置键',
  ConfirmedAtUtc: '确认时间(UTC)',
  ConsumedAtUtc: '消费时间(UTC)',
  ConsumerModule: '消费方模块',
  ConsumerName: '消费者名称',
  ConsumerReferenceId: '消费方引用标识',
  Content: '内容',
  ContentHash: '内容哈希',
  ContentType: '内容类型',
  ContextTenantId: '上下文租户标识',
  Contributor: '贡献者名称',
  ContributorVersion: '贡献者版本',
  CorrelationId: '关联标识',
  CreatedAt: '创建时间',
  CreatedAtUtc: '创建时间(UTC)',
  CreatedById: '创建人标识',
  CreatedByUserId: '创建人用户标识',
  CreatedCount: '新建数量',
  CronExpression: 'Cron 表达式',
  CurrentOwner: '当前所有者',
  CurrentVersionId: '当前版本标识',
  CutoffEventId: '截止事件标识',
  CutoffOccurredAtUtc: '截止事件发生时间(UTC)',
  DataScopeKind: '数据范围类型',
  DeadLetterReasonCode: '死信原因码',
  DeadLetteredAtUtc: '死信时间(UTC)',
  DefaultLocale: '默认语言区域',
  DeletedAtUtc: '删除时间(UTC)',
  DeletedByUserId: '删除人用户标识',
  Description: '描述',
  DestinationHostCategory: '目标主机类别',
  DiffSummaryJson: '差异摘要(JSON)',
  DisabledAtUtc: '禁用时间(UTC)',
  DisplayName: '显示名称',
  DisplayOrder: '显示顺序',
  DocumentId: '文档标识',
  DocumentItemId: '文档项标识',
  DocumentNo: '文档编号',
  DocumentType: '文档类型',
  Domain: '域名',
  DurationMs: '耗时(毫秒)',
  EducationLevel: '学历',
  EmergencyContact: '紧急联系人',
  EmergencyContactAddress: '紧急联系人地址',
  EmergencyContactPhone: '紧急联系人电话',
  EmployeeNumber: '工号',
  EndTime: '结束时间',
  EntityId: '实体标识',
  EntityKey: '实体键',
  EnvironmentName: '环境名称',
  Error: '错误信息',
  ErrorCode: '错误码',
  ErrorMessage: '错误消息',
  Ethnicity: '民族',
  EventType: '事件类型',
  ExceptionType: '异常类型',
  ExpireTime: '过期时间',
  ExpiresAtUtc: '过期时间(UTC)',
  Extension: '文件扩展名',
  FailedLoginCount: '登录失败次数',
  FamilyId: '会话族标识',
  FieldKey: '字段键',
  FileId: '文件标识',
  FileName: '文件名',
  FinishedAtUtc: '结束时间(UTC)',
  Gender: '性别',
  GraduatedSchool: '毕业院校',
  GridKey: '表格键',
  GroupName: '分组名称',
  HttpMethod: 'HTTP 方法',
  Icon: '图标',
  Id: '逻辑主键',
  IdCardNumber: '证件号码',
  IdCardType: '证件类型',
  Identifier: '唯一标识符',
  IdempotencyKey: '幂等键',
  IpAddress: 'IP 地址',
  IsActive: '是否启用',
  IsAffix: '是否固定标签',
  IsAuthenticated: '是否已认证',
  IsDeleted: '是否已软删除',
  IsEmbedded: '是否内嵌页面',
  IsEnabled: '是否启用',
  IsHidden: '是否隐藏',
  IsKeepAlive: '是否缓存页面',
  IsPrimary: '是否主关联',
  IsSuperAdministrator: '是否超级管理员角色',
  IsSystem: '是否系统内置',
  JobDefinitionId: '任务定义标识',
  JobKey: '任务键',
  JobScheduleId: '任务调度标识',
  JoinDateUtc: '入职时间(UTC)',
  KeyHash: '密钥哈希',
  KeyPrefix: '密钥前缀',
  Label: '显示标签',
  LastAccessTime: '最后访问时间',
  LastError: '最后错误',
  LastErrorCode: '最后错误码',
  LastExecutionAtUtc: '最后执行时间(UTC)',
  LastUsedAtUtc: '最后使用时间(UTC)',
  LastValue: '最后计数值',
  LeaseExpiresAtUtc: '租约过期时间(UTC)',
  LeaseId: '租约标识',
  LinkUrl: '外链地址',
  LockId: '锁标识',
  LockedUntil: '锁定截止时间',
  LockedUntilUtc: '锁定截止时间(UTC)',
  LockoutEndUtc: '锁定结束时间(UTC)',
  ManifestSha256: '清单 SHA256',
  MaximumValue: '最大值',
  MenuType: '菜单类型',
  Message: '消息文本',
  MessageId: '消息标识',
  MessageType: '消息类型',
  MimeType: 'MIME 类型',
  MinimumValue: '最小值',
  MisfirePolicy: '错过触发策略',
  ModuleKey: '模块键',
  Name: '名称',
  NextAttemptAt: '下次重试时间',
  NextAttemptAtUtc: '下次重试时间(UTC)',
  NextExecutionAtUtc: '下次执行时间(UTC)',
  Nickname: '昵称',
  NonceDigest: '随机数摘要',
  NormalizedUsername: '规范化用户名',
  NumberOfErrors: '错误次数',
  NumberOfRuns: '运行次数',
  OccurredAt: '发生时间',
  OccurredAtUtc: '发生时间(UTC)',
  OfficePhone: '办公电话',
  OneTimeAtUtc: '一次性触发时间(UTC)',
  OperationKind: '操作类型',
  OperationKey: '操作键',
  OriginalFileName: '原始文件名',
  Outcome: '结果',
  ParentId: '父级标识',
  PartitionKey: '分区键',
  PasswordHash: '密码哈希',
  Payload: '消息正文',
  PayloadHash: '载荷哈希',
  PermissionCode: '权限码',
  PermissionLevel: '权限级别',
  PhoneNumber: '手机号',
  PoliticalStatus: '政治面貌',
  PreferredLocale: '首选语言区域',
  PreviousOwner: '上一任所有者',
  ProcessedAt: '处理完成时间',
  ProcessedAtUtc: '处理完成时间(UTC)',
  Producer: '生产者标识',
  Profile: '种子配置档',
  ProfileVersion: '资料版本号',
  ProjectedAtUtc: '投影刷新时间(UTC)',
  ProviderKey: '存储提供程序键',
  PublishedAtUtc: '发布时间(UTC)',
  ReadAtUtc: '已读时间(UTC)',
  Reason: '原因说明',
  ReceivedAtUtc: '接收时间(UTC)',
  RecipientUserId: '接收人用户标识',
  Redirect: '重定向路径',
  ReleasedAtUtc: '释放时间(UTC)',
  Remark: '备注',
  ReplacedById: '替换会话标识',
  RequestPath: '请求路径',
  RequestedByUserId: '请求人用户标识',
  ResetBucket: '重置桶',
  ResetInterval: '重置周期',
  ResourceKey: '资源键',
  ResultCode: '结果码',
  RevokedAtUtc: '撤销时间(UTC)',
  RoleId: '角色标识',
  RollbackBoundaryEventId: '回滚边界事件标识',
  RollbackGeneration: '回滚代数',
  RollbackOccurredAtUtc: '回滚发生时间(UTC)',
  RollbackPreparedAtUtc: '回滚准备时间(UTC)',
  RollbackState: '回滚状态',
  RuleId: '规则标识',
  RuleKey: '规则键',
  RunId: '运行标识',
  SafeErrorCode: '安全错误码',
  ScheduledForUtc: '计划执行时间(UTC)',
  SchemaJson: 'Schema(JSON)',
  SchemaMode: 'Schema 模式',
  SchemaSha256: 'Schema SHA256',
  SchemaVersion: 'Schema 版本',
  Scope: '作用域',
  ScopeTenantKey: '作用域租户键',
  ScopeKey: '作用域键',
  SecretProtected: '受保护密钥',
  SecurityStamp: '安全戳',
  SequenceValue: '序列值',
  SerialNumber: '序列号',
  SessionId: '会话标识',
  ShareCode: '分享码',
  SizeBytes: '大小(字节)',
  SizeKb: '大小(KB)',
  SkippedCount: '跳过数量',
  Sort: '排序',
  SortOrder: '排序顺序',
  SourceApplyRunId: '来源应用运行标识',
  SucceededRollbackSourceApplyRunId: '成功回滚来源应用运行标识',
  SourceUpdatedAtUtc: '源更新时间(UTC)',
  SourceVersion: '源版本号',
  StackTrace: '堆栈跟踪',
  StartedAt: '开始时间',
  StartedAtUtc: '开始时间(UTC)',
  State: '状态',
  Status: '状态',
  StatusCode: 'HTTP 状态码',
  StorageKey: '存储键',
  StorageState: '存储状态',
  Succeeded: '是否成功',
  TagId: '标签标识',
  TemplateId: '模板标识',
  TemplateVersion: '模板版本',
  TenantId: '租户标识；NULL 表示 Host 级',
  TenantPackageId: '租户套餐标识',
  Thumbnail: '缩略图',
  TimeZoneId: '时区标识',
  Title: '标题',
  TokenHash: '令牌哈希',
  TopicCode: '主题编码',
  TraceId: '追踪标识',
  TraceParent: '追踪父级',
  TriggerKind: '触发类型',
  Type: '类型',
  UnitId: '机构单元标识',
  UpdatedAt: '更新时间',
  UpdatedAtUtc: '更新时间(UTC)',
  UpdatedById: '更新人标识',
  UpdatedByUserId: '更新人用户标识',
  UpdatedCount: '更新数量',
  UploadedByUserId: '上传人用户标识',
  UseCount: '使用次数',
  UserAgent: '用户代理',
  UserId: '用户标识',
  Username: '用户名',
  UsernameFingerprint: '用户名指纹',
  Value: '值',
  ValueKind: '值类型',
  Version: '乐观并发版本号',
  VersionNumber: '版本号',
  DestructiveDdlApprovalId: '破坏性 DDL 审批标识',
  OperatorUserId: '操作人用户标识',
  PermissionsJson: '权限集合(JSON)',
  Pattern: '编号模式',
  PositionId: '岗位标识',
  PositionLevelId: '职级标识',
  MaxAccessCount: '最大访问次数',
  RetryCount: '重试次数',
  RouteName: '路由名称',
  RequiredPermission: '所需权限码',
  Path: '路由路径',
};

const TABLE_OVERRIDES = {
  fn_outbox_message: '事务发件箱消息，承载待发布的集成事件',
  fn_messaging_outbox_event: '消息发件箱事件，供 CDC 中继投递',
  fn_messaging_inbox_message: '消息收件箱，记录消费者幂等处理状态',
  fn_messaging_stream_ownership: '消息流发布所有权与回滚边界',
  fn_identity_refresh_session: '用户刷新令牌会话',
  fn_identity_auth_audit: '身份认证审计事件',
  fn_pre_v1_naming_contract_state: '1.0 前命名契约迁移状态',
  fn_uuid_contract_state: 'UUID 二进制契约迁移状态',
  fn_seed_run: '种子数据执行运行记录',
  fn_seed_run_item: '种子数据贡献者执行明细',
};

const SQL_KEYWORDS = new Set([
  'AND', 'OR', 'CONSTRAINT', 'PRIMARY', 'UNIQUE', 'KEY', 'CHECK', 'FOREIGN', 'REFERENCES', 'DEFAULT',
]);

const SQL_TYPE_PATTERN = '(?:uniqueidentifier|char|varchar|nvarchar|int|bigint|smallint|tinyint|bit|datetimeoffset|datetime2|datetime|date|boolean|longblob|blob|text|longtext|decimal|numeric|binary|varbinary)';

export { SQL_TYPE_PATTERN };

const SQL_TYPE_PREFIXES = [
  'uniqueidentifier', 'char', 'varchar', 'nvarchar', 'int', 'bigint', 'smallint', 'tinyint', 'bit',
  'boolean', 'datetime', 'datetimeoffset', 'datetime2', 'date', 'time', 'longblob', 'blob', 'text',
  'longtext', 'decimal', 'numeric', 'float', 'real', 'binary', 'varbinary',
];

/** 解析 fn_{module}_{entity} 表名。 */
export function parseTableName(tableName) {
  const parts = tableName.split('_');
  if (parts.length < 3 || parts[0] !== 'fn') {
    return { moduleKey: 'unknown', entityKey: tableName };
  }
  const moduleKey = parts[1];
  const entityKey = parts.slice(2).join('_');
  return { moduleKey, entityKey };
}

/** 生成表级中文说明。 */
export function describeTable(tableName) {
  if (TABLE_OVERRIDES[tableName]) {
    return TABLE_OVERRIDES[tableName];
  }
  const { moduleKey, entityKey } = parseTableName(tableName);
  const moduleLabel = MODULE_LABELS[moduleKey] ?? moduleKey;
  const entityLabel = ENTITY_LABELS[entityKey] ?? entityKey.replaceAll('_', ' ');
  return `${moduleLabel}${entityLabel}表`;
}

/** 将 PascalCase 属性名拆分为可读片段。 */
function splitPascalCase(name) {
  return name.replace(/([a-z])([A-Z])/g, '$1 $2').replace(/([A-Z]+)([A-Z][a-z])/g, '$1 $2');
}

/** 生成列级中文说明。 */
export function describeColumn(tableName, columnName) {
  if (COLUMN_LABELS[columnName]) {
    return COLUMN_LABELS[columnName];
  }
  const readable = splitPascalCase(columnName);
  if (columnName.endsWith('Id')) {
    const role = readable.replace(/ Id$/i, '').trim();
    return role ? `${role}标识` : '关联标识';
  }
  if (columnName.endsWith('Utc')) {
    return `${readable.replace(/ Utc$/i, '')}(UTC)`;
  }
  if (columnName.endsWith('Json')) {
    return `${readable.replace(/ Json$/i, '')}(JSON)`;
  }
  if (columnName.startsWith('Is')) {
    return `是否${readable.slice(3)}`;
  }
  if (columnName.startsWith('Has')) {
    return `是否拥有${readable.slice(4)}`;
  }
  return readable;
}

/** 判断一行是否为列定义（排除约束与 CHECK 子句关键字）。 */
export function isColumnDefinitionLine(line) {
  const trimmed = line.trim();
  if (!trimmed || trimmed.startsWith('--')) {
    return false;
  }
  const match = trimmed.match(/^([A-Z][A-Za-z0-9]*)\s+(.+)$/);
  if (!match) {
    return false;
  }
  const [, name, rest] = match;
  if (SQL_KEYWORDS.has(name)) {
    return false;
  }
  const lower = rest.toLowerCase();
  return SQL_TYPE_PREFIXES.some(prefix => lower.startsWith(prefix));
}

/** 构建完整注释目录。 */
export function buildCommentCatalog(schema) {
  const tables = {};
  for (const [tableName, columns] of Object.entries(schema)) {
    const filtered = [...new Set(columns)].filter(column => !SQL_KEYWORDS.has(column));
    tables[tableName] = {
      comment: describeTable(tableName),
      columns: Object.fromEntries(
        filtered.map(column => [column, describeColumn(tableName, column)])
      ),
    };
  }
  return { schemaVersion: 1, tables };
}

/** 转义 SQL 字符串字面量中的单引号。 */
export function escapeSqlString(value) {
  return value.replaceAll("'", "''");
}
