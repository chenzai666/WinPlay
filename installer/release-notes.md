## WinPlay 0.1.3

打开就有声音，断开再连也马上有声音。实时档是约 0.3 秒。支持链接打开这个仓库。

### 更新
- 正在推流时禁用本机音量：喇叭静音，音量数值被按住，键盘音量键只调节 HomePod。断开后恢复原来的 Windows 音量并取消静音
- 音量快捷键可以自己设置。默认是 Ctrl+PgUp 提高、Ctrl+PgDn 降低、Ctrl+M 静音。托盘右键「设置快捷键…」，按下组合键或直接输入，例如 Ctrl+Shift+F1
- 推流时，键盘上的音量加、音量减、静音键仍然直接控制 HomePod
- 音量弹窗保持在最前面
- 开机自启后如果声卡还没准备好，音量锁定会继续重试
- 打开程序就弹出设备窗口。点到窗口外面只是把卡片藏起来，托盘图标还在。托盘右键最下面的 Quit WinPlay 才会退出
- 默认缓冲是「AirPlay 实时（约 0.3 秒）」。另外三档仍是 0.7 秒、2 秒、3 秒
- 单独的 HomePod（audioOS 27）从第一次连接就跟着音箱自己的时钟。重新打开和断开再连都会马上出声
- Support on GitHub 和 Report an issue 打开 https://github.com/chenzai666/WinPlay，安装包里的支持地址也一样

### 安装
- `WinPlay-0.1.3-win-x64-Setup.exe`：安装包，当前用户，不需要管理员
- `WinPlay-0.1.3-win-x64-portable.zip`：免安装，解压后运行 WinPlay.App.exe

第一次运行如果出现「Windows 已保护你的电脑」，点「更多信息」→「仍要运行」。
