using System.Reflection;
using System.Collections;
using System.Runtime.CompilerServices;
using LiteMonitor;
using LibreHardwareMonitor.Hardware;
using LiteMonitor.src.Core;
using LiteMonitor.src.SystemServices;

if (args.Contains("--hardware"))
{
    var computer = new Computer { IsGpuEnabled = true };
    try
    {
        computer.Open();
        var cfg = new Settings();
        var map = new SensorMap();
        map.Rebuild(computer, cfg);
        if (!computer.Hardware.Any(h => h.Name.Contains("B580", StringComparison.OrdinalIgnoreCase)))
        {
            Console.WriteLine("B580 was not enumerated by LibreHardwareMonitor in this process; B580 hardware verification is unavailable.");
            Environment.ExitCode = 2;
        }
        Console.WriteLine($"Selected: {map.CachedGpu?.Name}");
        foreach (var gpu in computer.Hardware.Where(HardwareScanner.IsGpuHardware))
            Console.WriteLine($"Before Update: {gpu.Name}: {string.Join(", ", gpu.Sensors.Select(s => s.Name))}");
        Console.WriteLine($"Startup mapped GPU.Load: {map.TryGetSensor("GPU.Load", out _)}");
        for (int i = 0; i < 5; i++)
        {
            map.CachedGpu?.Update();
            bool rebuilt = map.EnsureFresh(computer, cfg);
            map.TryGetSensor("GPU.Load", out var load);
            Console.WriteLine($"Update {i + 1}: rebuilt={rebuilt}, GPU.Load={load?.Value}, sensor={load?.Name}");
            Thread.Sleep(300);
        }
        map.Clear();
    }
    finally { computer.Close(); }
    return;
}

foreach (string preference in new[] { "", "/b580", "Intel Arc B580" })
{
    using var f = new Fixture(preference);
    Check(ReferenceEquals(f.Map.CachedGpu, f.Arc), "B580 selected before sensors activate");
    Check(f.Provider.GetValue("GPU.Load") != 37f, "B580 load unavailable before activation");
    f.Tick();
    Check(f.Provider.GetValue("GPU.Load") == 37f, "first Update exposes load without changing preference");
    Check(f.Config.PreferredGpu == preference, "preference preserved");
    Check(!f.Map.EnsureFresh(f.Computer, f.Config), "stable polling does not rebuild");
    Console.WriteLine($"PASS: first update, preference='{preference}'");
}

using (var f = new Fixture("/b580", delayTicks: 4))
{
    for (int i = 0; i < 3; i++)
    {
        f.Tick();
        Check(f.Provider.GetValue("GPU.Load") == null, "wait for delayed activation");
    }
    f.Tick();
    Check(f.Provider.GetValue("GPU.Load") == 37f, "late driver activation refreshes both caches");
    f.Arc.RemoveLoad();
    f.Tick();
    Check(f.Provider.GetValue("GPU.Load") == null, "removed sensor no longer read from cache");
    f.Arc.AddLoad(0f);
    f.Tick();
    Check(f.Provider.GetValue("GPU.Load") == 0f, "idle 0% is valid data");
    Console.WriteLine("PASS: delayed activation, removal, reactivation, idle load");
}

using (var f = new Fixture(""))
{
    f.Tick();
    f.Config.PreferredGpu = "/amd";
    f.Tick();
    Check(ReferenceEquals(f.Map.CachedGpu, f.Amd), "preference switches active card");
    Check(f.Provider.GetValue("GPU.Load") == 12f, "selected card's load reaches provider");
    f.Arc.AddLoad(99f);
    Check(!f.Map.EnsureFresh(f.Computer, f.Config), "old GPU events unsubscribed");
    f.Config.PreferredGpu = "";
    f.Tick();
    Check(f.Provider.GetValue("GPU.Load") == 99f, "returning to auto selects B580");
    f.Map.Clear();
    f.Arc.RemoveLoad();
    f.Map.Rebuild(f.Computer, f.Config);
    f.Provider.PreCacheAllSensors(f.Map);
    f.Arc.AddLoad(51f);
    f.Tick();
    Check(f.Provider.GetValue("GPU.Load") == 51f, "clear/reload restores event subscription");
    Console.WriteLine("PASS: preference switches, event cleanup, reload");
}

using (var f = new Fixture(""))
{
    f.Tick();
    // Simulate a periodic rebuild selecting a better vendor sensor, without a GPU event.
    var d3d = f.Arc.Load!;
    ((SensorProxy)(object)d3d).SensorName = "D3D 3D";
    f.Map.Rebuild(f.Computer, f.Config);
    f.Provider.PreCacheAllSensors(f.Map);
    f.Arc.AddAdditionalLoad("GPU Core", 63f);
    f.Map.Rebuild(f.Computer, f.Config);
    SetField(f.Map, "_gpuSensorsChanged", false);
    SetField(f.Map, "_lastMapBuild", DateTime.Now.AddMinutes(-11));
    f.Tick();
    Check(f.Provider.GetValue("GPU.Load") == 63f, "periodic rebuild refreshes provider copy");
    Console.WriteLine("PASS: periodic rebuild refreshes both caches");
}
foreach (bool renderFirst in new[] { false, true })
{
    using var f = new Fixture("/b580");
    ISensor render;
    if (renderFirst)
    {
        render = f.Arc.AddAdditionalLoad("GPU Render/Compute", 88f);
        f.Arc.AddLoad(9f);
    }
    else
    {
        f.Arc.AddLoad(9f);
        render = f.Arc.AddAdditionalLoad("GPU Render/Compute", 88f);
    }
    f.Tick();
    Check(f.Provider.GetValue("GPU.Load") == 88f, "Arc prefers render/compute regardless of enumeration order");
    Check(f.Map.TryGetSensor("GPU.Load", out var mapped) && ReferenceEquals(mapped, render), "mapping selects render/compute");
    foreach (float? invalid in new float?[] { null, float.NaN, float.PositiveInfinity, -1f, 101f })
    {
        ((SensorProxy)(object)render).Value = invalid;
        f.Tick();
        Check(f.Provider.GetValue("GPU.Load") == 9f, "invalid render/compute falls back to core");
    }
    ((SensorProxy)(object)render).Value = 0f;
    f.Tick();
    Check(f.Provider.GetValue("GPU.Load") == 0f, "valid idle render/compute does not fall back");
    ((SensorProxy)(object)render).Value = 76f;
    f.Tick();
    Check(f.Provider.GetValue("GPU.Load") == 76f, "render/compute recovers without sensor event");
    ((SensorProxy)(object)render).Value = null;
    ((SensorProxy)(object)f.Arc.Load!).Value = null;
    var d3d = f.Arc.AddAdditionalLoad("D3D 3D", 43f);
    f.Tick();
    Check(f.Provider.GetValue("GPU.Load") == 43f, "D3D fallback is used when render and core are invalid");
    f.Arc.RemoveSensor(d3d);
    f.Tick();
    Check(f.Provider.GetValue("GPU.Load") == null, "removed fallback is no longer cached");
    ((SensorProxy)(object)f.Arc.Load!).Value = 9f;
    f.Arc.RemoveSensor(render);
    f.Tick();
    Check(f.Provider.GetValue("GPU.Load") == 9f, "removed render/compute falls back to core");
    Console.WriteLine($"PASS: render/compute preference, invalid fallback, idle and recovery; renderFirst={renderFirst}");
}

using (var f = new Fixture(""))
{
    f.Tick();
    Check(f.Provider.GetValue("GPU.Load") == 37f, "core remains supported before render activation");
    f.Arc.AddAdditionalLoad("GPU Media", 100f);
    var render = f.Arc.AddAdditionalLoad("GPU Render/Compute", 91f);
    f.Tick();
    Check(f.Provider.GetValue("GPU.Load") == 91f, "late render activation replaces core through both caches");
    ((SensorProxy)(object)render).Value = null;
    ((SensorProxy)(object)f.Arc.Load!).Value = null;
    f.Tick();
    Check(f.Provider.GetValue("GPU.Load") == null, "invalid Arc readings do not use AMD or media load");
    f.Config.PreferredGpu = "/amd";
    f.Amd.AddAdditionalLoad("GPU Render/Compute", 99f);
    f.Tick();
    Check(f.Provider.GetValue("GPU.Load") == 12f, "other vendors retain their existing load selection");
    Console.WriteLine("PASS: late render activation, invalid data, GPU selection and vendor isolation");
}

Check(!new Settings().AutoCheckUpdate, "automatic updates disabled by default");
var settingsInstanceField = typeof(Settings).GetField("_instance", BindingFlags.Static | BindingFlags.NonPublic)!;
var previousSettingsInstance = settingsInstanceField.GetValue(null);
try
{
    settingsInstanceField.SetValue(null, new Settings { AutoCheckUpdate = false });
    var updateCheck = UpdateChecker.CheckAsync();
    Check(updateCheck.IsCompletedSuccessfully, "disabled automatic update returns before asynchronous network request");
    Check(!UpdateChecker.IsUpdateFound, "disabled automatic update does not advertise a new version");
    Console.WriteLine("PASS: automatic update disabled before network access");
}
finally { settingsInstanceField.SetValue(null, previousSettingsInstance); }
Console.WriteLine("All regression checks passed.");

static void Check(bool condition, string message)
{
    if (!condition)
    {
        Console.Error.WriteLine("FAIL: " + message);
        Environment.Exit(1);
    }
}

static void SetField(object target, string name, object value) =>
    target.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(target, value);

sealed class Fixture : IDisposable
{
    public Computer Computer { get; } = new();
    public FakeGpu Arc { get; }
    public FakeGpu Amd { get; } = new("AMD Radeon(TM) Graphics", "amd", HardwareType.GpuAmd);
    public Settings Config { get; }
    public SensorMap Map { get; } = new();
    public HardwareValueProvider Provider { get; }
    private readonly HardwareMonitor _monitor;
    private int _notifications;

    public Fixture(string preference, int delayTicks = 1)
    {
        Arc = new FakeGpu("Intel Arc B580", "b580", HardwareType.GpuIntel, delayTicks);
        Amd.AddLoad(12f);
        var groupType = typeof(Computer).Assembly.GetType("LibreHardwareMonitor.Hardware.IGroup")!;
        var group = DispatchProxy.Create(groupType, typeof(GroupProxy));
        ((GroupProxy)group).Hardware = new IHardware[] { Amd, Arc };
        var groups = (IList)typeof(Computer).GetField("_groups", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(Computer)!;
        groups.Add(group);
        Config = new Settings { PreferredGpu = preference, UseWinPerCounters = false };
        Config.MonitorItems.Add(new MonitorItemConfig { Key = "GPU.Load", VisibleInPanel = true });
        Map.Rebuild(Computer, Config);
        var syncLock = new object();
        Provider = new HardwareValueProvider(Computer, Config, Map, null!, null!, null!, null!, syncLock, new());
        Provider.PreCacheAllSensors(Map);
        // Run the actual UpdateAll/Get path without opening drivers or launching the UI.
        _monitor = (HardwareMonitor)RuntimeHelpers.GetUninitializedObject(typeof(HardwareMonitor));
        Set("_computer", Computer);
        Set("_cfg", Config);
        Set("_sensorMap", Map);
        Set("_valueProvider", Provider);
        Set("_lock", syncLock);
        Set("_lastTrafficTime", DateTime.Now);
        _monitor.OnValuesUpdated += () => _notifications++;
    }

    private void Set(string name, object value) => typeof(HardwareMonitor)
        .GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(_monitor, value);

    public void Tick()
    {
        int previous = _notifications;
        _monitor.UpdateAll();
        if (_notifications != previous + 1) throw new InvalidOperationException("UpdateAll failed before notification");
    }

    public void Dispose() => Map.Clear();
}

sealed class FakeGpu : Hardware
{
    private readonly int _delayTicks;
    private int _updates;
    public ISensor? Load { get; private set; }
    public FakeGpu(string name, string id, HardwareType type, int delayTicks = 1) : base(name, new Identifier(id), new TestSettings())
    {
        HardwareType = type;
        _delayTicks = delayTicks;
    }
    public override HardwareType HardwareType { get; }
    public override void Update()
    {
        if (++_updates == _delayTicks && Load == null) AddLoad(37f);
    }
    public void AddLoad(float value)
    {
        RemoveLoad();
        Load = AddAdditionalLoad("GPU Core", value);
    }
    public ISensor AddAdditionalLoad(string name, float value)
    {
        var sensor = DispatchProxy.Create<ISensor, SensorProxy>();
        var proxy = (SensorProxy)(object)sensor;
        proxy.Owner = this;
        proxy.SensorName = name;
        proxy.Value = value;
        ActivateSensor(sensor);
        return sensor;
    }
    public void RemoveLoad()
    {
        if (Load != null) DeactivateSensor(Load);
        Load = null;
    }
    public void RemoveSensor(ISensor sensor) => DeactivateSensor(sensor);
}

public class GroupProxy : DispatchProxy
{
    public IReadOnlyList<IHardware> Hardware { get; set; } = Array.Empty<IHardware>();
    protected override object? Invoke(MethodInfo? method, object?[]? args) =>
        method!.Name == "get_Hardware" ? Hardware : null;
}

public class SensorProxy : DispatchProxy
{
    public IHardware Owner { get; set; } = null!;
    public string SensorName { get; set; } = "GPU Core";
    public float? Value { get; set; }
    protected override object? Invoke(MethodInfo? method, object?[]? args) => method!.Name switch
    {
        "get_Hardware" => Owner,
        "get_Name" => SensorName,
        "get_SensorType" => SensorType.Load,
        "get_Value" => (float?)Value,
        "get_Identifier" => new Identifier(Owner.Identifier, "load", "0"),
        _ => method.ReturnType.IsValueType ? Activator.CreateInstance(method.ReturnType) : null
    };
}

sealed class TestSettings : ISettings
{
    public bool Contains(string name) => false;
    public string GetValue(string name, string value) => value;
    public void SetValue(string name, string value) { }
    public void Remove(string name) { }
}
