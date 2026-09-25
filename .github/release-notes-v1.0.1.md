## Codex Quota Monitor v1.0.1

这是面向 ChatGPT Pro 用户的修复版本。

### 修复

- 升级套餐后若旧 App Server 会话失效，悬浮窗会自动重启连接并刷新 ChatGPT 登录令牌
- Pro 当前只返回一个每周额度窗口时，只显示一个真实额度环，不再补画不存在的 `0%`
- 标题栏增加 `PRO` 套餐标识
- 本地状态输出增加 `planType`，便于排查套餐识别问题

### 下载

下载下方的 `Setup.exe` 并运行。支持 Windows 10/11 x64，需要 Codex Desktop 已登录 ChatGPT。

`Setup.exe` SHA-256：

```text
712E13862E019452CCBF7B53665CEAF1890826268511A821946E080F98CE69A1
```

> 安装包尚未使用商业代码签名证书签名，Windows SmartScreen 可能显示“未知发布者”。请核对以上 SHA-256。
