## 更新

- 根据最新 DeepSeek API 官方文档更新请求体结构
- 新增思考模式开关（Thinking Mode），支持在设置面板中开启/关闭
- 新增推理强度选择（Reasoning Effort），支持 High / Max 两种模式
- 推理强度仅在思考模式开启时生效，关闭时自动隐藏该字段
- 新增 `frequency_penalty` 和 `presence_penalty` 参数支持
- 移除已废弃的 `n` 参数
- `TopP` 类型从 `int` 修正为 `double`
- 新增中/英/繁三语国际化支持

