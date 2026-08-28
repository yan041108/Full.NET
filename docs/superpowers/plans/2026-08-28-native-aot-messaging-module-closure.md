# Messaging 模块 Native AOT 闭环计划

基线：`f4dcb89c92d4add6c1912376a38f7b0b03fa9476`

1. 用 Architecture RED 门禁锁定匿名 SQL 参数与缺失结果物化器。
2. 替换 Dead Letter 查询/重放参数，补齐 DeadLetter 与 OutboxEnvelope materializer。
3. 建立真实死信与 Outbox 行的双库 Native AOT 读取证据，不以空页冒充物化覆盖。
4. 通过 AOT analyzer、Linux publish、双库原生进程、受影响测试和独立审查后提交。
