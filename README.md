# macOS 残留文件清理工具

用于清理从 Mac 拷贝到 Windows 后残留的 macOS 专属文件，**递归整棵目录树**，永久删除（不进回收站）。

## 会删除的内容

| 类别 | 匹配 |
| --- | --- |
| Finder 缓存 | `.DS_Store`（校验文件头 `00 00 00 01 "Bud1"`） |
| AppleDouble | `._*`（校验文件头 `00 05 16 07`） |
| 外观文件/目录 | `.localized`（文件或目录形式）、`Icon\r`、`.VolumeIcon.icns`、`.background`、`.apdisk` 等 |
| 索引目录 | `.Spotlight-V100`、`.fseventsd`、`.DocumentRevisions-V100`、`.AppleDouble`、`.TemporaryItems` |
| 回收站 | `.Trashes`（可能含你在 Mac 上删过的真实文件，界面单独告警） |

## 一定不会动的内容

- `.gitignore`、`.env`、`.vscode` 等开发用点文件（白名单之外一律不碰）
- 名字以 `._` 开头但文件头不是 AppleDouble 的文件（判为用户文件，跳过并在"显示被跳过的条目"中列出）
- 符号链接 / junction / 挂载点（不进入、不删除）
- `System Volume Information`

## 安全设计

1. 扫描与删除分两步，先出清单再确认，**删除前二次弹窗**。
2. 默认开启文件头魔数校验，避免"名字像"被误删。
3. 拒绝以系统盘根目录、`C:\Windows`、`Program Files`、`ProgramData` 作为扫描根；驱动器根目录与用户主目录会额外警告。
4. 单条删除失败不中断，全部记入失败清单。
5. 日志写到 `%LOCALAPPDATA%\MacJunkCleaner\logs\`，每行即时落盘。

## 构建与运行

环境要求：Windows + .NET 9 SDK。

```powershell
dotnet build
dotnet publish src\MacJunkCleaner.App\MacJunkCleaner.App.csproj -c Release -o publish
```

产物：`publish\MacJunkCleaner.exe`（单文件、自包含，目标机无需安装 .NET）。

自检（不显示窗口，仅验证界面初始化）：

```powershell
Start-Process .\publish\MacJunkCleaner.exe -ArgumentList '--selftest' -Wait -PassThru
```

界面截图（离屏渲染，不弹窗、不抢焦点）：

```powershell
Start-Process .\publish\MacJunkCleaner.exe -ArgumentList '--screenshot','ui.png' -Wait
```

## 项目结构

```
src/MacJunkCleaner.Core/   判定器、扫描器、删除引擎、日志、报告（纯逻辑，不依赖 UI）
src/MacJunkCleaner.App/    WinForms 界面
```
