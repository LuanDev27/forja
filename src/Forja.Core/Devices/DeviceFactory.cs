using Forja.Anvil.Catalog;
using Forja.Anvil.Scene;

namespace Forja.Core.Devices;

/// <summary>
/// Fábrica data-driven (Artigo III.2): o catálogo aponta uma chave de
/// comportamento; tipos novos que reutilizam comportamentos existentes não
/// exigem recompilar nada. Chave desconhecida = erro explícito (Artigo VII.3).
/// </summary>
public sealed class DeviceFactory
{
    private readonly Dictionary<string, Func<DeviceBehavior>> _behaviors = new(StringComparer.Ordinal);

    public void Register(string behaviorKey, Func<DeviceBehavior> create) =>
        _behaviors[behaviorKey] = create;

    public DeviceBehavior Create(DeviceInstance instance, DeviceTypeDef type)
    {
        if (!_behaviors.TryGetValue(type.Behavior, out var create))
        {
            throw new InvalidOperationException(
                $"Comportamento desconhecido '{type.Behavior}' (tipo '{type.TypeId}', device {instance.Id}). " +
                $"Comportamentos registrados: {string.Join(", ", _behaviors.Keys.OrderBy(k => k, StringComparer.Ordinal))}.");
        }

        var behavior = create();
        behavior.Bind(instance, type);
        return behavior;
    }

    /// <summary>Todos os comportamentos built-in da v1 (RF-03).</summary>
    public static DeviceFactory CreateDefault()
    {
        var factory = new DeviceFactory();
        factory.Register("static-body", () => new StaticBodyDevice());
        factory.Register("conveyor", () => new ConveyorBelt());
        factory.Register("conveyor-io", () => new ConveyorBeltIo());
        factory.Register("conveyor-vspeed", () => new VariableSpeedConveyor());
        factory.Register("emitter", () => new Emitter());
        factory.Register("sink", () => new Sink());
        factory.Register("photo-sensor", () => new PhotoSensor());
        factory.Register("proximity-sensor", () => new ProximitySensor());
        factory.Register("height-sensor", () => new HeightSensor());
        factory.Register("level-sensor", () => new LevelSensor());
        factory.Register("weigh-scale", () => new WeighScale());
        factory.Register("piston", () => new Piston());
        factory.Register("stopper", () => new Stopper());
        factory.Register("pick-place", () => new PickPlace());
        factory.Register("push-button", () => new PushButton());
        factory.Register("selector-switch", () => new SelectorSwitch());
        factory.Register("indicator-light", () => new IndicatorLight());
        return factory;
    }
}
