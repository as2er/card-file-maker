# Card File Maker

用于自制 AXXX 包的 Boss 卡片素材制作、卡片库管理、Music.xml 编辑及章节挑战配置工具。界面语言为中文，基于 .NET 8 / WPF，仅支持 Windows。

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
- 独立“章节与挑战”标签页：章节草稿、分数条件、手动领取歌曲，以及隐藏曲和影片实验配置。

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

## 章节与挑战

新增功能位于独立的“章节与挑战”标签页，原卡片页及 Music.xml 页继续使用各自的编辑和导出流程。章节配置随 `.cfmproj` 一起保存并参与五分钟自动保存；旧项目没有章节数据时按空列表读取。

1. 顶部选择游戏目录和目标 AXXX，再进入“章节与挑战”，等待读取引用。左侧新建章节、导入 Chapter.xml，或读取现有章节为草稿。
2. 在“章节设置”填写章节 ID、名称与排序，选择已有 Story、章节选曲背景及宝石图标。可新建普通 Story 分类；活动 Story 仍可能依赖服务器活动。部分 Story 标题由游戏固定显示为“第 N 章”。游玩中的舞台在原 Music.xml 页面设置。
3. 在“曲目与分数挑战”按 ID／曲名搜索并添加歌曲，导入 Music.xml，或点击“添加／更新当前 Music 草稿”。后者复制原页面内容，保留已设置的挑战条件。曲目可排序或移除。
4. 为需要解锁的歌曲启用分数挑战，填写未占用的称号 ID，选择难度、技术分或战斗分门槛、是否要求击败 Boss，以及领取所需章节宝石。费用 0 表示免费领取。
5. 在“检查与导出”检查 ID、引用、谱面难度及已知全解影响，再在左侧多选要导出的章节。确认目标文件和覆盖项后，输出到顶部选定的 AXXX。只检查配置不会写入目标包。

挑战使用的流程为：**章节内游玩 → 达到分数条件获得称号 → 章节商店出现歌曲领取项 → 手动领取进入 Mylist**。并非达标后自动加入 Mylist。技术分范围为 1～1010000；普通歌曲使用前四栏，LUNATIC 和 Re:MASTER 使用第五栏。CFM 会将挑战曲配置为初始不拥有，并生成对应 Trophy 和章节领取项。

导入已有挑战时，分数、难度和 Boss 判定可以编辑；原称号的 FullCombo、等级及其他附加条件保留。移除本工具管理的挑战曲目会同时移除其章节领取项，已有的独立 Music.xml、Trophy.xml 文件保留。普通曲目只引用已有数据，缺少于引用库的导入歌曲会随章节输出；需要更新已有普通曲目的内容时，在 Music.xml 页面保存。

导出前校验来源文件是否被外部修改。覆盖备份写入程序旁的 `save/chapter-backups`，其中包含 `backup-info.json`；不会把备份放入 AXXX。项目中的来源路径和哈希会在成功导出后更新。项目配置清单上限为 8 MB（图片独立存储），超限会拒绝保存并提示拆分章节，原项目文件不会被覆盖。

### 隐藏曲与影片实验配置

“隐藏曲与影片 · 实验”支持复制／导入 `MemoryChapter.xml`、`SecretMusicCondition.xml`、`Movie.xml` 并编辑 XML，也可新建影片引用。MemoryChapter 的 ID 必须与当前 Chapter 一致；“填入当前章节与首曲引用”可辅助替换模板引用，其余条件需按目标曲目检查。

影片只接受包内相对 `.usm` 路径，不转换 MP4，也不自动复制素材。导出只包含配置 XML，音源、谱面、影片和其他资源需在包内另行备齐。若章节已经存在 Memory 配置，设置分数挑战前必须将实际生效的 Memory XML 纳入检查；不允许通过 Memory 通关奖励绕过分数门槛直接发放同曲 Mylist。

隐藏 `???` 显示、解锁前状态和影片播放依赖 Memory 状态及推进链，目前作为实验入口提供，尚未实机验证。普通章节的本地配置可由 AXXX 定义，但自定义章节记录、称号及歌曲拥有状态能否跨登录保留，取决于在线服务器对这些 ID 的处理。其他章节、原有账号记录或其他解锁途径也可能已开放同一首歌曲。

“检查全解补丁”只读解析游戏程序集，报告本工具已识别的免费解锁、初始解锁、Memory 记录补齐和活动延长逻辑；不会执行或修改 DLL。未发现已知特征不能证明程序集完全未修改。若补丁强制开放歌曲，单靠 AXXX 无法撤销该行为。

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
