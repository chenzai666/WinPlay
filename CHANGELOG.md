# Changelog

All notable changes to WinPlay are documented here. The format is based on
[Keep a Changelog](https://keepachangelog.com/en/1.1.0/), and this project adheres to
[Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [0.1.5] — 2026-11-06

### 界面设计补齐

- 音量 HUD 使用真实胶囊轮廓，设备与设置窗口采用随 DPI 缩放的 18 逻辑像素圆角，统一裁剪内容与玻璃底材。
- 快捷键按独立键帽展示，点击后可捕获组合键或直接输入，保留原有保存与校验逻辑。
- 设备卡片悬停与选中过渡分别为 150ms、180ms，按钮按压缩放为 100ms；尊重系统动画与高对比度设置。
- 保留独立字体颜色、自动强调色及仅设置窗口着色的玻璃材质，增加有限时设计诊断入口。

## [0.1.4] — 2026-11-05

### 修复与改进

- 修复深浅主题切换闪退、玻璃通透度与音量弹窗多余外框。
- 字体颜色与强调色独立设置，自动强调色读取 Windows；仅设置页底材跟随强调色。
- 统一通用、音频、快捷键及关于页面的按钮与半透明控件。
- 保留原有设备、音频、镜像与快捷键逻辑，增加界面诊断和材质参数回归测试。
- 安装包与应用版本统一为 0.1.4，修正 ARM64 安装包架构限制并提供发布文件校验清单。

## [0.1.1] — 2026-08-18

Maintenance release — functionally identical to 0.1.0. The v0.2.0 release has been
withdrawn because of audio playback regressions; its features will return in a future
release once they meet the quality bar. If you installed 0.2.0, install this build over it.

### Added

- **Support link** — a Sponsor button on the repository and a "Support WinPlay" section in
  the README, pointing to [Ko-fi](https://ko-fi.com/thedinesh).

## [0.1.0] — 2026-07-20

First public release. A native Windows AirPlay 2 sender.

### Added

- **Discovery** — bundled mDNS/DNS‑SD browser (no Bonjour); iOS Control‑Center‑style picker
  that collapses stereo pairs and multi‑room groups into single entries.
- **Audio streaming** — WASAPI loopback → lossless ALAC → encrypted RTP to any AirPlay 2
  receiver: HomePod, HomePod mini, stereo pairs, multi‑room groups, Apple TV, third‑party
  speakers.
- **Multi‑room & stereo pairs** — coordinated per‑member sessions on a built‑in
  **AirPlay‑2 PTP grandmaster clock** for sample‑accurate sync.
- **Pairing** — transient (HomePod), on‑screen **PIN** (Apple TV), and fast **pair‑verify**
  reconnects; credentials stored DPAPI‑encrypted.
- **FairPlay SAP** — clean‑room white‑box authentication, verified against golden vectors,
  enabling Apple TV screen mirroring.
- **Screen mirroring** — DXGI Desktop Duplication → D3D11 Video Processor → Media Foundation
  H.264, with **resolution negotiated from the TV**; live‑verified at 2560×1440 @ 60 fps.
- **Now Playing** metadata (title/artist/album + cover art) from the system media session.
- **Resilience** — automatic reconnect with exponential backoff.
- **Front ends** — a WinUI 3 system‑tray flyout and a scriptable CLI.

### Known limitations

- Screen mirroring is SDR H.264 only; HDR10 / Dolby Vision and HEVC are on the roadmap.
- Audio is not yet streamed alongside screen mirroring.
- Receiver identity is not pinned across sessions (same‑LAN trust model).

[0.1.1]: https://github.com/dineshdhotrad/WinPlay/releases/tag/v0.1.1
[0.1.0]: https://github.com/dineshdhotrad/WinPlay/releases/tag/v0.1.0
