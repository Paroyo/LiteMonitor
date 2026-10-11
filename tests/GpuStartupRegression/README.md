# GPU 启动回归验证

从项目根目录运行：

```powershell
dotnet run --project tests/GpuStartupRegression -c Release
```

该测试使用 LibreHardwareMonitor 的真实 Hardware 基类和传感器激活事件，模拟 AMD 核显与 Intel Arc B580，并直接执行实际的 HardwareMonitor.UpdateAll 和 HardwareValueProvider.GetValue 路径。测试不启动软件界面、不打开硬件驱动、不修改用户配置。测试夹具通过反射注入硬件树与监控器依赖，避免测试调用真实驱动；升级硬件库或监控器字段时需要同步维护夹具。

覆盖自动选择、Identifier 与旧显卡名称配置、首次更新激活、第四次更新才激活、传感器移除与重新激活、空闲 0%、切换显卡后解除旧事件、清空重建及十分钟维护后读取缓存同步。

可选真实硬件探测：

```powershell
dotnet run --project tests/GpuStartupRegression -c Release -- --hardware
```

该模式会打开 GPU 监控，并输出更新前后的传感器映射。若硬件库未枚举 B580，会返回退出码 2；这表示无法完成 B580 实机验证。实际开机自启动仍需重启后观察。
