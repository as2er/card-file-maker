# Card File Maker

用于自制 AXXX 包的 Boss 卡片素材制作、卡片库管理及 Music.xml 编辑工具。界面语言为中文，基于 .NET 8 / WPF，仅支持 Windows。

## 下载与运行

在本仓库的 GitHub Releases 中下载 Windows 运行包，解压到有写入权限的独立文件夹，运行 `CardFileMaker.exe`。请保留压缩包中的 DLL 和运行配置文件，不能只复制 EXE。

运行需要 **.NET 8 Desktop Runtime（Windows x64）**。

程序会在所在目录创建 `save`，用于项目、五分钟自动保存和覆盖备份。升级时保留自己的 `save`，不要将它提交到仓库或上传到 Release。

## 功能

- 导入图片，详细裁剪或按两组素材快速生成六种卡图；尺寸不足时支持透明补边。
- 创建、修改、保存卡片项目，框选和批量导出；检查卡片 ID、角色 ID、资源名称和 CAB 冲突。
- 读取所选 AXXX 的卡片库，复制为草稿，并在删除前检查歌曲引用和相关资源。
- 引用已有角色，或创建无模型角色；读取官方游戏包和自制 opt 的角色、舞台、技能等数据。
- 新建、导入、编辑和保存 Music.xml；支持覆盖源 XML 并保存备份。
- 固定舞台与 Boss 卡面预览，支持点击查看大图；读取官方 BC7 及自制 RGBA 卡面。
- Release 内置 78 个可选舞台的静态预览；图片加密嵌入程序，选中舞台即可显示。
- LUNATIC / Re:MASTER 界面互斥；保存 Re:MASTER 时采用双 true 标记。
- 将 opt 的 UI 资源注册进 assets.bytes，保留既有记录和依赖。


## 基本使用

1. 选择自己的游戏目录及目标 AXXX 文件夹，等待卡片、角色和引用数据加载。
2. 创建卡片，导入图片并裁剪，填写名称和 ID，选择属性及已有角色或空角色模式。
3. 保存项目，选择要导出的卡片，核对覆盖提示后导出。
4. 在 Music.xml 页新建或导入歌曲，选择 Boss 卡片、舞台和谱面名称，填写必要引用。
5. 点击保存并单独选择 XML 输出位置，或覆盖导入的原 XML。导出结果仍需实机验证。

六种输出素材规格：

| 素材 | 宽 × 高 | Mipmap 层数 |
| --- | --- | --- |
| 完整卡面 | 768 × 1052 | 1 |
| 小卡面 | 256 × 356 | 1 |
| 头像 | 256 × 256 | 1 |
| 小头像 | 100 × 100 | 1 |
| 战斗立绘 | 1024 × 1024 | 11 |
| 竖版人物图 | 768 × 1052 | 1 |

战斗立绘右侧 256 像素保持透明。快速模式会从高分辨率素材缩小生成对应小图。


## 从源码构建

安装 Windows x64 的 .NET SDK 8.0.413。`src/global.json` 和 `tests/global.json` 固定此 SDK；请在相应目录执行命令。

```powershell
Set-Location src
dotnet build CardFileMaker.csproj -c Release
```

输出在 `build/bin/Release/net8.0-windows`。运行包需要下列文件：

- CardFileMaker.exe
- CardFileMaker.dll
- CardFileMaker.Core.dll
- CardFileMaker.deps.json
- CardFileMaker.runtimeconfig.json
- BCnEncoder.dll
- CommunityToolkit.HighPerformance.dll
- SharpCompress.dll
- ZstdSharp.dll

PDB 是可选调试文件，不是运行必需文件。首次构建会从官方 NuGet 源还原 BCnEncoder.Net 2.3.0、SharpCompress 0.39.0 及其依赖。构建机器需要上述 SDK 和 Windows Desktop 引用程序集；完整第三方许可随运行包提供。

## 模板和图标

`src/Templates` 包含通用 XML 示例和六份 Unity 序列化占位模板。二进制模板的图像是人工生成的 **1×1 蓝灰色 RGBA 像素**，导出时会完整替换为用户裁剪图、目标尺寸和 mipmap；其中不含原卡片插画。资源路径和示例 ID 已改成通用值。

这些二进制文件是本工具内部的 serialized v17 模板，不是普通 PNG，也不能直接用游戏里的 UnityFS 文件改名替代。Unity 类型结构基于本地兼容性样本整理；来源与许可边界见 THIRD_PARTY_NOTICES.md。

可在本地使用 `-p:CfmTemplateDirectory="绝对模板目录"` 指定替代模板。需要 Card.xml、Chara.xml、Music.xml、DataConfig.xml，以及 card.bin、cardSmall.bin、icon.bin、iconSmall.bin、battle.bin、portrait.bin。请用独立配置或清理构建输出，避免切换模板后混用旧产物。

```powershell
.\build\bin\Release\net8.0-windows\CardFileMaker.exe --check-templates
```

此诊断模式不打开窗口：退出码 0 表示模板完整，2 表示缺少模板。

程序保留 Pirika 二创图标。MIT 许可证不授予第三方角色、商标及原设定的权利，见 THIRD_PARTY_NOTICES.md。

## 测试

在 `tests` 目录执行：

```powershell
dotnet run --project CardFileMaker.Tests.csproj -c Release
```

默认测试使用生成数据和通用模板，不需要游戏目录。真实游戏目录的只读检查仅在显式设置 `CFM_TEST_GAME` 和 `CFM_TEST_OPT` 后启用。

文件工作流集成测试还需要设置 `CFM_TEST_GAME`，可用 `CFM_TEST_MANIFESTS` 指定以分号分隔的 assets.bytes 路径；运行时传入 `--workflow-integration` 和一个尚不存在的测试输出目录。该模式会在输出目录生成测试文件，并在测试程序旁的 save 中生成备份；不要把这些内容发布。


## 许可证

项目自行编写的代码采用 MIT，见 LICENSE。第三方格式结构、游戏名称、角色设定及二创图标的说明见 THIRD_PARTY_NOTICES.md。
