using System;
using System.Text.Json;
using Forja.Anvil;
using Forja.Anvil.Contracts;
using Forja.Anvil.Scene;
using Forja.Core.Devices;

namespace Forja.Studio.Headless;

/// <summary>Base dos cenários de dispositivo da US4: helpers de cena em
/// código, leitura de DI pela IoTable e busca de peça por zona (as cenas
/// usam zonas afastadas para testar vários dispositivos de uma vez).</summary>
public abstract class DeviceScenario : HeadlessScenario
{
    protected static JsonElement N(double v) => JsonSerializer.SerializeToElement(v);

    protected static JsonElement S(string v) => JsonSerializer.SerializeToElement(v);

    protected static JsonElement I(int v) => JsonSerializer.SerializeToElement(v);

    protected bool Di(ushort offset)
    {
        foreach (var row in Loop.Io!.BuildView())
        {
            if (row.Address.Area == IoArea.DiscreteInput && row.Address.Offset == offset)
                return row.Value;
        }
        return false;
    }

    /// <summary>Peça dentro do raio da zona (cada zona tem no máximo uma).</summary>
    protected Part? PartNear(float x, float z, float radius = 1.2f)
    {
        if (Loop.Parts is not { } parts)
            return null;
        foreach (var part in parts.All)
        {
            var pos = part.Body.Pose.Pos;
            if (MathF.Abs(pos.X - x) <= radius && MathF.Abs(pos.Z - z) <= radius)
                return part;
        }
        return null;
    }
}

/// <summary>
/// T042 / RF-03: esteira acionada por I/O. Com a coil "run" desligada a peça
/// fica parada sobre a esteira; forçando a coil, a peça é transportada (+X).
/// </summary>
public sealed class ConveyorIoScenario : DeviceScenario
{
    private const ushort RunCoil = 0;

    private enum Phase { Settle, OffCheck, Running, Done }

    private Phase _phase = Phase.Settle;
    private int _wait;
    private float _xRef;
    private long _safety;

    public override void Begin() => StartRun(BuildScene());

    public override void Tick()
    {
        if (++_safety > 3000)
        {
            Fail($"timeout na fase {_phase}.");
            return;
        }

        var box = PartNear(-1.2f, 0f, radius: 3f);

        switch (_phase)
        {
            case Phase.Settle:
                if (box is null)
                    return;
                if (box.Body.LinearVelocity.Length() < 0.05f && box.Body.Pose.Pos.Y < 0.4f)
                {
                    _xRef = box.Body.Pose.Pos.X;
                    _wait = 90;
                    _phase = Phase.OffCheck;
                }
                break;

            case Phase.OffCheck:
                if (box is null)
                {
                    Fail("peça sumiu com a esteira desligada.");
                    return;
                }
                if (--_wait > 0)
                    return;
                if (MathF.Abs(box.Body.Pose.Pos.X - _xRef) > 0.05f)
                {
                    Fail($"esteira desligada moveu a peça (Δx={box.Body.Pose.Pos.X - _xRef:0.###}).");
                    return;
                }
                Loop.Enqueue(new ForceIoCommand(new IoAddress(IoArea.Coil, RunCoil), true));
                _wait = 120;
                _phase = Phase.Running;
                break;

            case Phase.Running:
                if (box is null)
                {
                    Fail("peça sumiu durante o transporte.");
                    return;
                }
                if (--_wait > 0)
                    return;
                float dx = box.Body.Pose.Pos.X - _xRef;
                if (dx < 0.3f)
                    Fail($"coil ligada mas a peça andou só {dx:0.###} m em 2 s.");
                else
                    _phase = Phase.Done;
                break;

            case Phase.Done:
                Pass();
                break;
        }
    }

    private static SceneDocument BuildScene() => new()
    {
        SchemaVersion = SceneDocument.CurrentSchemaVersion,
        Name = "esteira acionada",
        Seed = 11,
        Devices = new()
        {
            new DeviceInstance
            {
                Id = 1, TypeId = "conveyor.belt.io",
                Transform = new Pose(new Vec3(0, 0.2f, 0), 0),
                Params = new() { ["length"] = N(3), ["width"] = N(0.5), ["speed"] = N(0.8) },
            },
            new DeviceInstance
            {
                Id = 2, TypeId = "emitter",
                Transform = new Pose(new Vec3(-1.2f, 0.6f, 0), 0),
                Params = new()
                {
                    ["interval"] = N(0.1), ["maxParts"] = I(1),
                    ["sizes"] = S("S"), ["material"] = S("plastic"),
                },
            },
        },
        IoMap = new() { new IoTag(1, "run", new IoAddress(IoArea.Coil, RunCoil)) },
        Connection = new ConnectionConfig { Driver = "null" },
    };
}

/// <summary>
/// T043 / RF-03: sensores em 4 zonas de um piso comprido —
///  DI0 proximidade capacitiva vê peça plástica;
///  DI1 proximidade indutiva NÃO vê plástico;
///  DI2 proximidade indutiva vê metal;
///  DI3 sensor de altura NÃO vê peça baixa (S além do threshold);
///  DI4 sensor de altura vê peça alta (L dentro do threshold).
/// </summary>
public sealed class SensorsScenario : DeviceScenario
{
    private long _safety;

    public override void Begin() => StartRun(BuildScene());

    public override void Tick()
    {
        if (++_safety > 3000)
        {
            Fail("timeout aguardando as peças assentarem.");
            return;
        }
        if (Loop.Io is null || Loop.Parts is not { Count: 4 } || Loop.TickNumber < 150)
            return;

        if (!Di(0))
            Fail("capacitivo não detectou peça plástica (DI0 == 0).");
        else if (Di(1))
            Fail("indutivo detectou peça plástica (DI1 == 1).");
        else if (!Di(2))
            Fail("indutivo não detectou peça metálica (DI2 == 0).");
        else if (Di(3))
            Fail("sensor de altura detectou peça baixa (DI3 == 1 — threshold ignorado?).");
        else if (!Di(4))
            Fail("sensor de altura não detectou peça alta (DI4 == 0).");
        else
            Pass();
    }

    private static SceneDocument BuildScene()
    {
        static DeviceInstance Emitter(uint id, float x, float y, string size, string material) => new()
        {
            Id = id, TypeId = "emitter",
            Transform = new Pose(new Vec3(x, y, 0), 0),
            Params = new()
            {
                ["interval"] = N(0.1), ["maxParts"] = I(1),
                ["sizes"] = S(size), ["material"] = S(material),
            },
        };

        static DeviceInstance Proximity(uint id, float x, float rotY, string mode) => new()
        {
            Id = id, TypeId = "sensor.proximity",
            Transform = new Pose(new Vec3(x, 0.1f, 0), rotY),
            Params = new() { ["range"] = N(0.5), ["mode"] = S(mode) },
        };

        static DeviceInstance Height(uint id, float x) => new()
        {
            Id = id, TypeId = "sensor.height",
            Transform = new Pose(new Vec3(x, 1.0f, 0), 0),
            Params = new() { ["range"] = N(2), ["threshold"] = N(0.5) },
        };

        return new SceneDocument
        {
            SchemaVersion = SceneDocument.CurrentSchemaVersion,
            Name = "sensores",
            Seed = 13,
            Devices = new()
            {
                new DeviceInstance
                {
                    Id = 1, TypeId = "floor",
                    Transform = new Pose(new Vec3(4.5f, -0.1f, 0), 0),
                    Params = new() { ["sizeX"] = N(12), ["sizeY"] = N(0.2), ["sizeZ"] = N(2) },
                },
                Emitter(2, 0f, 0.4f, "S", "plastic"),
                Emitter(3, 3f, 0.4f, "S", "metal"),
                Emitter(4, 6f, 0.4f, "S", "plastic"),
                Emitter(5, 9f, 0.5f, "L", "plastic"),
                Proximity(6, -0.4f, 0, "capacitive"),
                Proximity(7, 0.4f, 180, "inductive"),
                Proximity(8, 2.6f, 0, "inductive"),
                Height(9, 6f),
                Height(10, 9f),
            },
            IoMap = new()
            {
                new IoTag(6, "detect", new IoAddress(IoArea.DiscreteInput, 0)),
                new IoTag(7, "detect", new IoAddress(IoArea.DiscreteInput, 1)),
                new IoTag(8, "detect", new IoAddress(IoArea.DiscreteInput, 2)),
                new IoTag(9, "detect", new IoAddress(IoArea.DiscreteInput, 3)),
                new IoTag(10, "detect", new IoAddress(IoArea.DiscreteInput, 4)),
            },
            Connection = new ConnectionConfig { Driver = "null" },
        };
    }
}

/// <summary>
/// T044 / RF-03: stopper e pusher.
///  Zona A (z=0): esteira sempre ligada + stopper fechado segura a peça;
///  liberar a coil deixa a peça seguir.
///  Zona B (z=3): pusher (tipo do catálogo sem porta "extended") empurra a
///  peça no piso quando a coil é forçada.
/// </summary>
public sealed class ActuatorsScenario : DeviceScenario
{
    private const ushort CloseCoil = 0;
    private const ushort ExtendCoil = 1;
    private const float GateFaceX = 0.45f;

    private enum Phase { Hold, HoldVerify, PassGate, PusherSettle, Push, Done }

    private Phase _phase = Phase.Hold;
    private int _wait;
    private float _lastAx;
    private float _xbRef;
    private long _safety;

    public override void Begin()
    {
        StartRun(BuildScene());
        // Stopper já fechado antes da peça chegar.
        Loop.Enqueue(new ForceIoCommand(new IoAddress(IoArea.Coil, CloseCoil), true));
    }

    public override void Tick()
    {
        if (++_safety > 6000)
        {
            Fail($"timeout na fase {_phase}.");
            return;
        }

        var boxA = PartNear(0f, 0f, radius: 2.5f);
        var boxB = PartNear(0f, 3f, radius: 2f);

        switch (_phase)
        {
            case Phase.Hold:
                if (boxA is null)
                    return;
                _lastAx = boxA.Body.Pose.Pos.X;
                // Chegou perto do stopper e parou = está sendo segurada.
                if (_lastAx > 0.2f && boxA.Body.LinearVelocity.Length() < 0.05f)
                {
                    _wait = 60;
                    _phase = Phase.HoldVerify;
                }
                break;

            case Phase.HoldVerify:
                if (boxA is null)
                {
                    Fail("peça sumiu enquanto o stopper segurava.");
                    return;
                }
                if (boxA.Body.Pose.Pos.X > GateFaceX)
                {
                    Fail($"stopper fechado mas a peça passou (x={boxA.Body.Pose.Pos.X:0.###}).");
                    return;
                }
                if (--_wait > 0)
                    return;
                Loop.Enqueue(new ForceIoCommand(new IoAddress(IoArea.Coil, CloseCoil), false));
                _wait = 600;
                _phase = Phase.PassGate;
                break;

            case Phase.PassGate:
                if (boxA is not null)
                    _lastAx = boxA.Body.Pose.Pos.X;
                if (_lastAx > 1.2f)
                {
                    _phase = Phase.PusherSettle;
                    return;
                }
                if (boxA is null)
                {
                    Fail($"peça sumiu antes de passar do stopper (último x={_lastAx:0.###}).");
                    return;
                }
                if (--_wait <= 0)
                    Fail($"stopper liberado mas a peça não seguiu (x={_lastAx:0.###}).");
                break;

            case Phase.PusherSettle:
                if (boxB is null)
                    return;
                if (boxB.Body.LinearVelocity.Length() < 0.05f && boxB.Body.Pose.Pos.Y < 0.2f)
                {
                    _xbRef = boxB.Body.Pose.Pos.X;
                    Loop.Enqueue(new ForceIoCommand(new IoAddress(IoArea.Coil, ExtendCoil), true));
                    _wait = 90;
                    _phase = Phase.Push;
                }
                break;

            case Phase.Push:
                if (boxB is null)
                {
                    Fail("peça da zona do pusher sumiu durante o empurrão.");
                    return;
                }
                if (--_wait > 0)
                    return;
                float dx = boxB.Body.Pose.Pos.X - _xbRef;
                if (dx < 0.2f)
                    Fail($"pusher não empurrou a peça (Δx={dx:0.###} m).");
                else
                    _phase = Phase.Done;
                break;

            case Phase.Done:
                Pass();
                break;
        }
    }

    private static SceneDocument BuildScene() => new()
    {
        SchemaVersion = SceneDocument.CurrentSchemaVersion,
        Name = "atuadores",
        Seed = 17,
        Devices = new()
        {
            new DeviceInstance
            {
                Id = 1, TypeId = "conveyor.belt",
                Transform = new Pose(new Vec3(0, 0.2f, 0), 0),
                Params = new() { ["length"] = N(4), ["width"] = N(0.5), ["speed"] = N(0.5) },
            },
            new DeviceInstance
            {
                Id = 2, TypeId = "emitter",
                Transform = new Pose(new Vec3(-1.5f, 0.6f, 0), 0),
                Params = new()
                {
                    ["interval"] = N(0.1), ["maxParts"] = I(1),
                    ["sizes"] = S("S"), ["material"] = S("plastic"),
                },
            },
            new DeviceInstance
            {
                Id = 3, TypeId = "actuator.stopper",
                Transform = new Pose(new Vec3(0.5f, 0.44f, 0), 0),
                Params = new() { ["width"] = N(0.5) },
            },
            new DeviceInstance
            {
                Id = 4, TypeId = "floor",
                Transform = new Pose(new Vec3(0, -0.1f, 3), 0),
                Params = new() { ["sizeX"] = N(4), ["sizeY"] = N(0.2), ["sizeZ"] = N(2) },
            },
            new DeviceInstance
            {
                Id = 5, TypeId = "emitter",
                Transform = new Pose(new Vec3(0, 0.4f, 3), 0),
                Params = new()
                {
                    ["interval"] = N(0.1), ["maxParts"] = I(1),
                    ["sizes"] = S("S"), ["material"] = S("plastic"),
                },
            },
            new DeviceInstance
            {
                Id = 6, TypeId = "actuator.pusher",
                Transform = new Pose(new Vec3(-0.5f, 0.1f, 3), 0),
                Params = new()
                {
                    ["stroke"] = N(0.6), ["speed"] = N(1.5),
                    ["rodLength"] = N(0.3), ["rodWidth"] = N(0.4),
                },
            },
        },
        IoMap = new()
        {
            new IoTag(3, "close", new IoAddress(IoArea.Coil, CloseCoil)),
            new IoTag(6, "extend", new IoAddress(IoArea.Coil, ExtendCoil)),
        },
        Connection = new ConnectionConfig { Driver = "null" },
    };
}

/// <summary>
/// T045 / RF-03: passivos colidem.
///  Zona A (z=0): peça cai sobre a grade estrutural e fica em cima (não
///  atravessa).
///  Zona B (z=3): guia lateral atravessada no fim da esteira segura a peça
///  sobre a esteira (não cai da borda).
/// </summary>
public sealed class PassivesScenario : DeviceScenario
{
    private enum Phase { RackSettle, RackVerify, GuideReach, GuideVerify, Done }

    private Phase _phase = Phase.RackSettle;
    private int _wait;
    private long _safety;

    public override void Begin() => StartRun(BuildScene());

    public override void Tick()
    {
        if (++_safety > 6000)
        {
            Fail($"timeout na fase {_phase}.");
            return;
        }

        var boxA = PartNear(0f, 0f, radius: 1.5f);
        var boxB = PartNear(3.5f, 3f, radius: 2.5f);

        switch (_phase)
        {
            case Phase.RackSettle:
                if (boxA is null)
                    return;
                if (boxA.Body.Pose.Pos.Y < 0.9f)
                {
                    Fail($"peça atravessou a grade estrutural (y={boxA.Body.Pose.Pos.Y:0.###}).");
                    return;
                }
                if (boxA.Body.LinearVelocity.Length() < 0.05f)
                {
                    _wait = 60;
                    _phase = Phase.RackVerify;
                }
                break;

            case Phase.RackVerify:
                if (boxA is null || boxA.Body.Pose.Pos.Y < 0.9f)
                {
                    Fail("peça não ficou apoiada sobre a grade.");
                    return;
                }
                if (--_wait <= 0)
                    _phase = Phase.GuideReach;
                break;

            case Phase.GuideReach:
                if (boxB is null)
                    return;
                if (boxB.Body.Pose.Pos.X > 4.12f)
                {
                    Fail($"peça passou da guia lateral (x={boxB.Body.Pose.Pos.X:0.###}).");
                    return;
                }
                if (boxB.Body.Pose.Pos.X > 3.9f)
                {
                    _wait = 120;
                    _phase = Phase.GuideVerify;
                }
                break;

            case Phase.GuideVerify:
                if (boxB is null)
                {
                    Fail("peça caiu da esteira apesar da guia lateral.");
                    return;
                }
                var pos = boxB.Body.Pose.Pos;
                if (pos.X > 4.12f || pos.Y < 0.28f)
                {
                    Fail($"guia não segurou a peça (x={pos.X:0.###}, y={pos.Y:0.###}).");
                    return;
                }
                if (--_wait <= 0)
                    _phase = Phase.Done;
                break;

            case Phase.Done:
                Pass();
                break;
        }
    }

    private static SceneDocument BuildScene() => new()
    {
        SchemaVersion = SceneDocument.CurrentSchemaVersion,
        Name = "passivos",
        Seed = 19,
        Devices = new()
        {
            new DeviceInstance
            {
                Id = 1, TypeId = "rack.frame",
                Transform = new Pose(new Vec3(0, 0.5f, 0), 0),
                Params = new() { ["sizeX"] = N(1), ["sizeY"] = N(1), ["sizeZ"] = N(1) },
            },
            new DeviceInstance
            {
                Id = 2, TypeId = "emitter",
                Transform = new Pose(new Vec3(0, 1.8f, 0), 0),
                Params = new()
                {
                    ["interval"] = N(0.1), ["maxParts"] = I(1),
                    ["sizes"] = S("S"), ["material"] = S("plastic"),
                },
            },
            new DeviceInstance
            {
                Id = 3, TypeId = "conveyor.belt",
                Transform = new Pose(new Vec3(3, 0.2f, 3), 0),
                Params = new() { ["length"] = N(3), ["width"] = N(0.5), ["speed"] = N(0.5) },
            },
            new DeviceInstance
            {
                Id = 4, TypeId = "emitter",
                Transform = new Pose(new Vec3(2, 0.6f, 3), 0),
                Params = new()
                {
                    ["interval"] = N(0.1), ["maxParts"] = I(1),
                    ["sizes"] = S("S"), ["material"] = S("plastic"),
                },
            },
            new DeviceInstance
            {
                Id = 5, TypeId = "guide.side",
                Transform = new Pose(new Vec3(4.2f, 0.45f, 3), 90),
                Params = new()
                {
                    ["sizeX"] = N(0.8), ["sizeY"] = N(0.4),
                    ["sizeZ"] = N(0.05), ["friction"] = N(0.1),
                },
            },
        },
        IoMap = new(),
        Connection = new ConnectionConfig { Driver = "null" },
    };
}
