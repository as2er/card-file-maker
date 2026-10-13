# 第三方内容与来源说明

## 代码

本仓库自行编写的程序代码按根目录 LICENSE 中的 MIT 条款提供。代码许可证不改变任何第三方内容已有的权利归属。

## Pirika 二创图标

`src/Assets/Pirika.png` 和 `src/Assets/Pirika.ico` 来自用户提供的 AI 辅助二创图像，角色参考为 SOUND VOLTEX 的 Pirika（ピリカ）。ICO 是该 PNG 的多尺寸转换版本。

相关角色设定、名称、商标及其他原有权利归各自权利人所有。此项目与 SOUND VOLTEX / KONAMI 无官方关联或背书。上述图标不包含在本项目 MIT 代码授权范围内；本仓库不额外授予第三方角色与设定的再许可权。

## Unity 资源占位模板

`src/Templates/*.bin` 的序列化类型结构以本地 Unity 5.6.4f1 兼容性样本为基础，经过本工具重新封装对象。资源名、容器路径和示例 ID 使用通用值，像素为人工生成的 1×1 蓝灰色 RGBA（96、128、176、255）。这些文件不含原卡片的插画像素。

占位像素由本项目生成；不能因为替换了像素，就声称所有底层第三方格式结构均为项目原创或获得了第三方许可。本项目 MIT 许可证只覆盖有权授予的自有内容。

XML 模板保留兼容所需的字段结构、枚举和版本／技能引用，并将原卡片及角色信息改成通用示例。

## 游戏数据与兼容性

オンゲキ、SOUND VOLTEX、Unity 及相关产品名称仅用于说明兼容性与素材来源，权利归各自权利人所有。仓库不包含游戏本体、音频、谱面、玩家存档或官方角色插画，不提供它们的下载。

用户自行提供的素材、导入 XML 和生成的卡片内容不自动受本项目 MIT 许可证覆盖。

## Release 内置舞台预览

运行包中的 78 张舞台预览由用户本地 SDDT 1.60 场景离线生成，保留静态视角并简化材质。图片加密嵌入程序资源，加密包不提交公开源码。图中游戏场景、贴图、联动作品和商标等权利归原权利人所有，不包含在本项目 MIT 代码授权范围内；封装和加密不改变原有权利归属。

## 开源运行依赖

- BCnEncoder.Net 2.3.0：https://github.com/Nominom/BCnEncoder.NET，本次采用 MIT 许可（上游另提供 Unlicense）。
- CommunityToolkit.HighPerformance 8.4.0：https://github.com/CommunityToolkit/dotnet，MIT。
- SharpCompress 0.39.0：https://github.com/adamhathcock/sharpcompress，MIT。
- ZstdSharp.Port 0.8.4：https://github.com/oleg-st/ZstdSharp，MIT。
- System.Buffers 4.6.0：Microsoft，MIT；.NET 8 运行包使用框架提供的实现，不额外分发该 DLL。

以下保留许可原文。

### BCnEncoder.Net

```text
MIT License

Copyright 2020 Nomi Lakkala

Permission is hereby granted, free of charge, to any person obtaining a copy
of this software and associated documentation files (the "Software"), to deal
in the Software without restriction, including without limitation the rights
to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
copies of the Software, and to permit persons to whom the Software is
furnished to do so, subject to the following conditions:

The above copyright notice and this permission notice shall be included in all
copies or substantial portions of the Software.

THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE
SOFTWARE.
```

### SharpCompress

```text
MIT License

Copyright (c) 2014 Adam Hathcock
NuGet package notice: Copyright (c) 2025 Adam Hathcock

Permission is hereby granted, free of charge, to any person obtaining a copy
of this software and associated documentation files (the "Software"), to deal
in the Software without restriction, including without limitation the rights
to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
copies of the Software, and to permit persons to whom the Software is
furnished to do so, subject to the following conditions:

The above copyright notice and this permission notice shall be included in all
copies or substantial portions of the Software.

THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE
SOFTWARE.
```

### SharpCompress 所含 Bouncy Castle 代码

```text
MIT License

Copyright (c) 2000 - 2011 The Legion Of The Bouncy Castle (http://www.bouncycastle.org)

Permission is hereby granted, free of charge, to any person obtaining a copy
of this software and associated documentation files (the "Software"), to deal
in the Software without restriction, including without limitation the rights
to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
copies of the Software, and to permit persons to whom the Software is
furnished to do so, subject to the following conditions:

The above copyright notice and this permission notice shall be included in all
copies or substantial portions of the Software.

THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE
SOFTWARE.
```

### ZstdSharp.Port

```text
MIT License

Copyright (c) 2021 Oleg Stepanischev
NuGet package notice: Copyright Oleg Stepanischev 2024

Permission is hereby granted, free of charge, to any person obtaining a copy
of this software and associated documentation files (the "Software"), to deal
in the Software without restriction, including without limitation the rights
to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
copies of the Software, and to permit persons to whom the Software is
furnished to do so, subject to the following conditions:

The above copyright notice and this permission notice shall be included in all
copies or substantial portions of the Software.

THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE
SOFTWARE.
```

### System.Buffers

```text
MIT License

Copyright (c) Microsoft Corporation. All rights reserved.

Permission is hereby granted, free of charge, to any person obtaining a copy
of this software and associated documentation files (the "Software"), to deal
in the Software without restriction, including without limitation the rights
to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
copies of the Software, and to permit persons to whom the Software is
furnished to do so, subject to the following conditions:

The above copyright notice and this permission notice shall be included in all
copies or substantial portions of the Software.

THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE
SOFTWARE.
```

### CommunityToolkit.HighPerformance — License.md

```text
# .NET Community Toolkit

Copyright © .NET Foundation and Contributors

All rights reserved.

## MIT License (MIT)

Permission is hereby granted, free of charge, to any person obtaining a copy of this software and associated documentation files (the “Software”), to deal in the Software without restriction, including without limitation the rights to use, copy, modify, merge, publish, distribute, sublicense, and/or sell copies of the Software, and to permit persons to whom the Software is furnished to do so, subject to the following conditions:

The above copyright notice and this permission notice shall be included in all copies or substantial portions of the Software.

THE SOFTWARE IS PROVIDED *AS IS*, WITHOUT WARRANTY OF ANY KIND, EXPRESS OR IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY, FITNESS FOR A PARTICULAR PURPOSE AND NON-INFRINGEMENT. IN NO EVENT SHALL THE AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM, OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE SOFTWARE.

```

### CommunityToolkit.HighPerformance — ThirdPartyNotices.txt

```text
.NET-Community-Toolkit

THIRD-PARTY SOFTWARE NOTICES AND INFORMATION
Do Not Translate or Localize

This project incorporates components from the projects listed below. The original copyright notices and the licenses under which the .NET Foundation received such components are set forth below. The .NET Foundation reserves all rights not expressly granted herein, whether by implication, estoppel or otherwise.

1.	PedroLamas/DeferredEvents version 1.0.4 (https://github.com/PedroLamas/DeferredEvents), included in CommunityToolkit.Common/Deferred.
2.	MichaeIDietrich/UwpNotificationNetCoreTest commit 5c1a4a3 (https://github.com/MichaeIDietrich/UwpNotificationNetCoreTest), used in DesktopNotificationManagerCompat.cs to support .NET Core 3.0.
3.	lbugnion/mvvmlight commit 4cbf77c (https://github.com/lbugnion/mvvmlight), from which some APIs from the `CommunityToolkit.Mvvm` package take inspiration from.
4.  PrivateObject/PrivateType (https://github.com/microsoft/testfx/tree/664ac7c2ac9dbfbee9d2a0ef560cfd72449dfe34/src/TestFramework/Extension.Desktop), included in UnitTests.
5.  QuinnDamerell/UniversalMarkdown (https://github.com/QuinnDamerell/UniversalMarkdown) contributed by Quinn Damerell and Paul Bartrum for the MarkdownTextBlock control, relicensed to this .NET Foundation project under the MIT license upon contribution in https://github.com/CommunityToolkit/WindowsCommunityToolkit/pull/772.
6.  qmatteoq/DesktopBridgeHelpers commit e278153 (https://github.com/qmatteoq/DesktopBridgeHelpers), contributed by Matteo Pagani to identify if running with identity in DesktopNotificationManagerCompat.cs and DesktopBridgeHelpers.cs, relicensed to this .NET Foundation project under the MIT license upon contribution in https://github.com/CommunityToolkit/WindowsCommunityToolkit/pull/3457.
7.  Sergio0694/ComputeSharp (https://github.com/Sergio0694/ComputeSharp), contributed by Sergio Pedri to reuse some helpers to support incremental generators in the MVVM Toolkit.

%% PedroLamas/DeferredEvents NOTICES AND INFORMATION BEGIN HERE
=========================================
Copyright (C) 2017 Pedro Lamas, http://www.pedrolamas.com

Permission is hereby granted, free of charge, to any person obtaining a copy
of this software and associated documentation files (the "Software"), to deal
in the Software without restriction, including without limitation the rights
to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
copies of the Software, and to permit persons to whom the Software is
furnished to do so, subject to the following conditions:

The above copyright notice and this permission notice shall be included in
all copies or substantial portions of the Software.

THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN
THE SOFTWARE.
=========================================
END OF PedroLamas/DeferredEvents NOTICES AND INFORMATION

%% MichaeIDietrich/UwpNotificationNetCoreTest NOTICES AND INFORMATION BEGIN HERE
=========================================
The MIT License (MIT)

Copyright (c) 2020 Michael Dietrich

Permission is hereby granted, free of charge, to any person obtaining a copy
of this software and associated documentation files (the "Software"), to deal
in the Software without restriction, including without limitation the rights
to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
copies of the Software, and to permit persons to whom the Software is
furnished to do so, subject to the following conditions:

The above copyright notice and this permission notice shall be included in
all copies or substantial portions of the Software.

THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN
THE SOFTWARE.
=========================================
END OF MichaeIDietrich/UwpNotificationNetCoreTest NOTICES AND INFORMATION

%% lbugnion/mvvmlight NOTICES AND INFORMATION BEGIN HERE
=========================================
The MIT License (MIT)

Copyright (c) 2009 - 2018 Laurent Bugnion
Permission is hereby granted, free of charge, to any person obtaining a copy
of this software and associated documentation files (the "Software"), to deal
in the Software without restriction, including without limitation the rights
to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
copies of the Software, and to permit persons to whom the Software is
furnished to do so, subject to the following conditions:

The above copyright notice and this permission notice shall be included in all
copies or substantial portions of the Software.

THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE
SOFTWARE.
=========================================
END OF lbugnion/mvvmlight NOTICES AND INFORMATION

%% PrivateOject/PrivateType NOTICED AND INFORMATION BEGIN HERE
=========================================
MSTest Framework

Copyright (c) Microsoft Corporation. All rights reserved.

MIT License

Permission is hereby granted, free of charge, to any person obtaining a copy
of this software and associated documentation files (the "Software"), to deal
in the Software without restriction, including without limitation the rights
to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
copies of the Software, and to permit persons to whom the Software is
furnished to do so, subject to the following conditions:

The above copyright notice and this permission notice shall be included in all
copies or substantial portions of the Software.

THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE
SOFTWARE.
=========================================
END OF PrivateOject/PrivateType NOTICES AND INFORMATION

%% Sergio0694/ComputeSharp NOTICES AND INFORMATION BEGIN HERE
=========================================
MIT License

Copyright (c) 2021 Sergio Pedri

Permission is hereby granted, free of charge, to any person obtaining a copy
of this software and associated documentation files (the "Software"), to deal
in the Software without restriction, including without limitation the rights
to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
copies of the Software, and to permit persons to whom the Software is
furnished to do so, subject to the following conditions:

The above copyright notice and this permission notice shall be included in all
copies or substantial portions of the Software.

THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE
SOFTWARE.
=========================================
END OF Sergio0694/ComputeSharp NOTICES AND INFORMATION
```
