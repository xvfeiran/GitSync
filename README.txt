GitSync（原 VendorSync）
========================

把源仓库（上游 Git 仓库）的全部分支和标签，一次性同步到目标仓库。
双击 GitSync.exe 即可运行，不需要安装 .NET。

完整说明请看 README.md（特性、快速开始、命令行、构建方法、安全说明）。

安全要点：
  · 本程序不保存、不录入任何账号、密码、令牌或私钥。
  · HTTPS 凭据由 Git Credential Manager 写入 Windows 凭据管理器。
  · SSH 私钥始终留在 ~/.ssh，程序只读取公钥用于展示和复制。
  · 配置文件 vendorsync.config.json 只记录路径与远程名。

自检与排查：
  GitSync.exe --selftest    完整逻辑自测（临时仓库，不碰真实数据）
  GitSync.exe --inspect     只读查看当前项目的分支差异，绝不推送
  GitSync.exe --version     显示版本
