# Intel Arc B580 启动占用读取修复

## 故障与原因

首次运行或开机自启动时，GPU 占用无法读取；在“硬件与系统”中将首选显卡从“自动”切换到 B580，或反向切换后恢复。

启动流程在 GPU 首次 Update 之前建立 SensorMap，并让 HardwareValueProvider 保存传感器映射副本。如果占用传感器在后续 Update 才激活，两层缓存就会漏掉它。首选显卡切换会同步重建两层缓存，所以能恢复。原十分钟维护仅重建 SensorMap，没有刷新读取缓存，无法可靠兜底。

## 修复行为

- 监听当前 GPU 的 SensorAdded/SensorRemoved，回调只标记失效。
- 每轮硬件 Update 完成后检查映射，必要时重建，并同步 HardwareValueProvider 的副本。
- 支持首次更新及更晚才激活的传感器；无需固定等待时间，也不在启动阶段预热所有显卡。
- 切换 GPU、清空映射及退出时解除旧事件订阅。
- 异步初始化调度前设置启动标记，硬件重载时在同一锁内重建映射和读取缓存。

此 fork 同时默认关闭自动更新。关闭后，后台检查在联网之前返回，并清除缓存的新版本提示；用户仍可手动检查更新，或在设置中重新开启自动检查。

## B580 占用指标选择

Intel Arc 独显优先将 `GPU Render/Compute` 映射为 GPU 占用，适合观察游戏与渲染负载。原来的 `GPU Core` 来自 Intel 全局活动计数器，与任务管理器的“最忙引擎”统计口径不同。此选择不改变其他厂商或 Intel 核显的原有逻辑。

Render/Compute 不存在时沿用原指标；该传感器存在但读数缺失、非有限值或超出 0–100% 时，使用同卡 GPU Core，再使用同卡 D3D 3D。有效的 0% 不触发回退。首选指标恢复后立即恢复读取，无需切换显卡；启动后才激活时会同步刷新两层缓存。

此指标仍不保证与任务管理器完全一致。任务管理器总占用取最忙 GPU 引擎，视频等负载可能使用其他引擎；刷新周期和界面平滑也会影响即时读数。

参考：[硬件库 IntelDiscreteGpu 实现](https://github.com/LibreHardwareMonitor/LibreHardwareMonitor/blob/v0.9.6/LibreHardwareMonitorLib/Hardware/Gpu/IntelDiscreteGpu.cs)、[微软 GPU 统计说明](https://devblogs.microsoft.com/directx/gpus-in-the-task-manager/)、[Intel 对 B580 指标问题的回复](https://github.com/IGCIT/Intel-GPU-Community-Issue-Tracker-IGCIT/issues/1562)。

## 验证

```powershell
dotnet build LiteMonitor.sln -c Release
dotnet run --project tests/GpuStartupRegression -c Release
dotnet publish LiteMonitor.csproj -c Release -r win-x64 --self-contained true -o artifacts/LiteMonitor-B580-Fix
```

回归测试直接运行真实的 HardwareMonitor.UpdateAll 和 HardwareValueProvider.GetValue 路径，使用 LibreHardwareMonitor 的 Hardware 基类及真实传感器激活事件模拟双显卡。

覆盖自动模式、显卡 Identifier 和旧名称配置、首次更新激活、延迟激活、移除与重新激活、空闲 0%、显卡切换与事件清理、清空重建、十分钟维护及关闭自动更新时不发起后台联网检查。未修复源码在首次更新读取检查中失败，修复版通过。

真实硬件探测可运行：

```powershell
dotnet run --project tests/GpuStartupRegression -c Release -- --hardware
```

当前验证环境的 Windows 可以列出 B580，但 LibreHardwareMonitor 在测试进程中未枚举到它，因此没有完成 B580 实机或重启自启动验证。硬件探测未枚举 B580 时返回退出码 2，不能据此认定实机验证通过。

## 使用已有配置

升级前退出旧程序并备份软件目录。保留 settings.json、个人主题及插件配置，将发布目录中的程序文件和依赖复制到原安装目录，可继续使用原自启动路径。

已有配置可能仍保存 AutoCheckUpdate=true；如需关闭自动更新，请在设置中关闭“自动检查更新”，或将该字段改为 false。默认关闭只适用于新建配置。
