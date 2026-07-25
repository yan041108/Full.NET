# Platform Host 工作台汇总纵向切片（2026-07-26）

## 目标

将双管理端 Overview 的核心指标与最近活动从静态占位改为 Host 汇总 API 驱动的真实数据。

## 清单

1. [x] `GET /api/v1/platform/host-dashboard-summary`（`platform.dashboard.read`）
2. [x] 跨表只读聚合：活跃租户、在线会话、今日访问、错误率、最近操作日志
3. [x] Integration **162 → 164**（SQL Server/MySQL）
4. [x] OpenAPI + client-contracts
5. [x] Vue `OverviewView` + Layui `overview-dashboard.js`
6. [x] shell-parity mock 扩展（门槛 **62** 不变）
7. [x] 路线图与验证记录

## 范围外

- 流量趋势图、健康分、待办卡片仍保留演示占位
- 日环比 delta
