# RainsenVampSur 自动化测试入口

## 目标

`Run-ProjectChecks.ps1` 是项目交付前的统一质量门禁入口。默认依次运行 EditMode 和 PlayMode 测试，并在以下情况返回失败退出码：

- Unity 无法启动或退出码异常。
- 测试结果 XML 缺失或无法解析。
- 测试总数为 0。
- 存在失败、错误或 Inconclusive 测试。
- Unity 日志检测到 C# 编译错误。
- 项目关键场景、场景 `.meta` 或基础配置缺失。

## 使用方式

如果 Unity 已经在 PATH 中：

```powershell
.\Tools\Run-ProjectChecks.ps1
```

如果 Unity 没有在 PATH 中，显式指定 Unity 2022.3.62f3c1：

```powershell
.\Tools\Run-ProjectChecks.ps1 `
    -UnityPath 'C:\Program Files\Unity\Hub\Editor\2022.3.62f3c1\Editor\Unity.exe'
```

也可以设置环境变量，之后省略 `-UnityPath`：

```powershell
$env:UNITY_EDITOR_PATH = 'C:\Program Files\Unity\Hub\Editor\2022.3.62f3c1\Editor\Unity.exe'
.\Tools\Run-ProjectChecks.ps1
```

## 运行单个平台

```powershell
.\Tools\Run-ProjectChecks.ps1 -TestPlatform EditMode
.\Tools\Run-ProjectChecks.ps1 -TestPlatform PlayMode
```

在没有图形环境的机器或 CI 中，可以追加 `-NoGraphics`：

```powershell
.\Tools\Run-ProjectChecks.ps1 -NoGraphics
```

## 输出位置

每次运行都会在以下目录生成本次报告。`Logs/` 已被 `.gitignore` 忽略，不会污染版本控制：

```text
Logs/Automation/<时间戳>/
├── EditMode.xml
├── EditMode.log
├── PlayMode.xml
├── PlayMode.log
└── summary.json
```

## 运行前注意事项

- 尽量关闭当前正在打开同一项目的 Unity Editor，避免项目锁或 AssetDatabase 状态影响批处理测试。
- 测试脚本不会执行 Git commit、Plastic Checkin 或远程同步。
- 自动化通过不等于视觉、手感、音效和真实设备性能已经验收；这些内容仍需人工确认。
- 如果当前环境找不到 Unity，必须报告为“未验证”，不能把脚本未执行写成测试通过。

## 交付判定

代码或场景修改完成后，默认执行完整 EditMode + PlayMode 测试。相关测试失败时，先修复并重跑；如果确实无法执行，交付说明必须列出原因和未验证范围。
