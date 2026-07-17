using Forja.Anvil.Catalog;
using Forja.Anvil.Scene;

namespace Forja.Anvil.Tests;

/// <summary>Catálogo mínimo em memória para os testes de validação.</summary>
internal static class TestCatalog
{
    public static DeviceCatalog Build() => DeviceCatalog.FromDefs(new[]
    {
        new DeviceTypeDef
        {
            TypeId = "sensor.test",
            Category = DeviceCategory.Sensor,
            DisplayName = "Sensor de teste",
            Behavior = "photo-sensor",
            Ports = new() { new PortDef("detect", IoDirection.In) },
        },
        new DeviceTypeDef
        {
            TypeId = "actuator.test",
            Category = DeviceCategory.Actuator,
            DisplayName = "Atuador de teste",
            Behavior = "piston",
            Ports = new() { new PortDef("extend", IoDirection.Out) },
        },
        new DeviceTypeDef
        {
            TypeId = "passive.test",
            Category = DeviceCategory.Passive,
            DisplayName = "Passivo de teste",
            Behavior = "static-body",
        },
    }).Require();

    public static DeviceInstance Sensor(uint id) => new() { Id = id, TypeId = "sensor.test" };

    public static DeviceInstance Actuator(uint id) => new() { Id = id, TypeId = "actuator.test" };

    public static IoTag Di(uint deviceId, ushort offset) =>
        new(deviceId, "detect", new IoAddress(IoArea.DiscreteInput, offset));

    public static IoTag Coil(uint deviceId, ushort offset) =>
        new(deviceId, "extend", new IoAddress(IoArea.Coil, offset));
}
