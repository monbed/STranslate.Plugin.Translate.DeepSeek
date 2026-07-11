## 新增

- 设置界面新增「余额查询」：一键查询当前密钥账户的可用余额（接口 `GET /user/balance`，支持多币种显示），卡片置于设置页顶部

## 修复

- 修复繁体中文语言文件中混入简体字（請選擇模型）
- 修正接口地址描述与实际行为不符：Path 留空时实际自动填充 `/chat/completions`（原文案误写为 `/v1/chat/completions`）

## 移除

- 移除官方已弃用的 `frequency_penalty`、`presence_penalty` 请求参数（官方文档说明：该参数已不再支持，传入不会产生任何效果）
