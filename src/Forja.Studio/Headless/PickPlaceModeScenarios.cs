using System;
using System.Diagnostics;
using System.Net.Sockets;
using System.Text.Json;
using Forja.Anvil;
using Forja.Anvil.Contracts;
using Forja.Anvil.Scene;
using Forja.Core.Devices;
using Forja.Core.Physics;
using NModbus;

namespace Forja.Studio.Headless;

/// <summary>
/// Geometria compartilhada dos dois cenários de transição do pick-and-place
/// (V-H e V-I do quickstart 002). É a mesma cena do <see cref="PickPlaceScenario"/>:
/// piso em y=0, uma peça S que assenta com topo em 0,2, e a unidade em y=0,68
/// com curso 0,4 — a ventosa para logo acima do topo da peça. O que muda entre
/// os dois é só o driver da conexão.
/// </summary>
internal static class PickPlaceModeScene
{
    public const ushort AdvanceCoil = 0;
    public const ushort LowerCoil = 1;
    public const ushort GripCoil = 2;

    public const ushort DiAdvanced = 0;
    public const ushort DiRetracted = 1;
    public const ushort DiLowered = 2;
    public const ushort DiRaised = 3;
    public const ushort DiHolding = 4;

    public const uint PickPlaceId = 3;
    public const float StrokeX = 0.8f;
    public const float RestY = 0.1f;

    private static JsonElement N(double v) => JsonSerializer.SerializeToElement(v);

    private static JsonElement S(string v) => JsonSerializer.SerializeToElement(v);

    private static JsonElement I(int v) => JsonSerializer.SerializeToElement(v);

    public static SceneDocument Build(ConnectionConfig connection) => new()
    {
        SchemaVersion = SceneDocument.CurrentSchemaVersion,
        Name = "pick-and-place — transições",
        Seed = 7,
        Devices = new()
        {
            new DeviceInstance
            {
                Id = 1, TypeId = "floor",
                Transform = new Pose(new Vec3(0.4f, 0, 0), 0),
                Params = new()
                {
                    ["sizeX"] = N(6), ["sizeY"] = N(0.2), ["sizeZ"] = N(4),
                    ["friction"] = N(0.6),
                },
            },
            new DeviceInstance
            {
                Id = 2, TypeId = "emitter",
                Transform = new Pose(new Vec3(0, 0.5f, 0), 0),
                Params = new()
                {
                    ["interval"] = N(0.1), ["maxParts"] = I(1),
                    ["sizes"] = S("S"), ["material"] = S("plastic"),
                },
            },
            new DeviceInstance
            {
                Id = PickPlaceId, TypeId = "actuator.pickplace",
                Transform = new Pose(new Vec3(0, 0.68f, 0), 0),
                Params = new()
                {
                    ["strokeX"] = N(StrokeX), ["strokeY"] = N(0.4),
                    ["speedX"] = N(1.2), ["speedY"] = N(1.0),
                    ["gripRange"] = N(0.14),
                },
            },
        },
        IoMap = new()
        {
            new IoTag(PickPlaceId, "advance", new IoAddress(IoArea.Coil, AdvanceCoil)),
            new IoTag(PickPlaceId, "lower", new IoAddress(IoArea.Coil, LowerCoil)),
            new IoTag(PickPlaceId, "grip", new IoAddress(IoArea.Coil, GripCoil)),
            new IoTag(PickPlaceId, "advanced", new IoAddress(IoArea.DiscreteInput, DiAdvanced)),
            new IoTag(PickPlaceId, "retracted", new IoAddress(IoArea.DiscreteInput, DiRetracted)),
            new IoTag(PickPlaceId, "lowered", new IoAddress(IoArea.DiscreteInput, DiLowered)),
            new IoTag(PickPlaceId, "raised", new IoAddress(IoArea.DiscreteInput, DiRaised)),
            new IoTag(PickPlaceId, "holding", new IoAddress(IoArea.DiscreteInput, DiHolding)),
        },
        Connection = connection,
    };
}

/// <summary>
/// Spec 002 / T038 / V-H (FR-009): voltar de Rodar para Editar COM uma peça
/// presa na garra não pode deixar peça órfã. A garra agarra e ergue a peça
/// (cinemática), o cenário pede Edit, e prova que o mundo é desmontado sem
/// sobra — nada fica flutuando preso a um dispositivo que deixou de existir.
/// Ao voltar para Rodar, a peça nova cai sob gravidade como qualquer outra.
///
/// Complementa os testes unitários <c>Teardown_DesfazVinculo</c> e
/// <c>PecaRemovidaDuranteOTransporte</c>: aqui o caminho é o REAL do loop
/// (Run→Edit ⇒ <c>Parts.Clear()</c> antes de <c>device.Teardown</c>), com a
/// física Jolt de verdade.
/// </summary>
public sealed class PickPlaceEditReturnScenario : DeviceScenario
{
    private enum Phase { Settle, Lower, Grip, CarryUp, GoEdit, InEdit, BackToRun, Landed, Done }

    private Phase _phase = Phase.Settle;
    private long _safety;

    public override void Begin() =>
        StartRun(PickPlaceModeScene.Build(new ConnectionConfig { Driver = "null" }));

    public override void Tick()
    {
        if (++_safety > 8000)
        {
            Fail($"timeout na fase {_phase} (modo={Loop.Mode}).");
            return;
        }

        switch (_phase)
        {
            case Phase.Settle:
            {
                var box = PartNear(0.4f, 0f, radius: 3f);
                if (box is null)
                    return;
                if (box.Body.LinearVelocity.Length() < 0.02f && box.Body.Pose.Pos.Y < 0.2f)
                {
                    Force(PickPlaceModeScene.LowerCoil, true);
                    _phase = Phase.Lower;
                }
                break;
            }

            case Phase.Lower:
                if (Di(PickPlaceModeScene.DiLowered))
                {
                    Force(PickPlaceModeScene.GripCoil, true);
                    _phase = Phase.Grip;
                }
                break;

            case Phase.Grip:
                if (Di(PickPlaceModeScene.DiHolding))
                {
                    Force(PickPlaceModeScene.LowerCoil, false); // ergue a peça
                    _phase = Phase.CarryUp;
                }
                break;

            case Phase.CarryUp:
            {
                var box = PartNear(0.4f, 0f, radius: 3f);
                // Precondição de V-H: a peça está PRESA e no alto, não no piso.
                if (Di(PickPlaceModeScene.DiRaised) && box is not null
                    && box.Body.Pose.Pos.Y > PickPlaceModeScene.RestY + 0.2f)
                {
                    Loop.Enqueue(new SetModeCommand(SimMode.Edit));
                    _phase = Phase.GoEdit;
                }
                break;
            }

            case Phase.GoEdit:
                // O loop desmonta em Edit: Parts.Clear() e Teardown de cada
                // dispositivo. Se o Teardown tocasse um corpo já liberado
                // (a peça presa), estouraria aqui.
                if (Loop.Mode == SimMode.Edit)
                {
                    if (Loop.Parts is not null)
                    {
                        Fail("em Edit o mundo físico deveria estar desmontado, "
                             + "mas ainda há PartsManager ativo (peça órfã?).");
                        return;
                    }
                    Loop.Enqueue(new SetModeCommand(SimMode.Run));
                    _phase = Phase.BackToRun;
                }
                break;

            case Phase.BackToRun:
                // Reconstruiu: emissor solta uma peça nova bem acima do piso.
                if (Loop.Mode == SimMode.Run && PartNear(0.4f, 0f, radius: 3f) is { } fresh
                    && fresh.Body.Pose.Pos.Y > PickPlaceModeScene.RestY + 0.2f)
                {
                    _phase = Phase.InEdit; // reusa como "caindo"
                }
                break;

            case Phase.InEdit:
            {
                // A peça nova cai sob gravidade e assenta — prova de que o mundo
                // reconstruiu limpo, sem corpo cinemático fantasma segurando nada.
                var box = PartNear(0.4f, 0f, radius: 3f);
                if (box is null)
                    return;
                if (box.Body.LinearVelocity.Length() < 0.05f
                    && MathF.Abs(box.Body.Pose.Pos.Y - PickPlaceModeScene.RestY) < 0.12f)
                {
                    _phase = Phase.Landed;
                }
                break;
            }

            case Phase.Landed:
                Godot.GD.Print("   peça presa · Run→Edit desmontou sem órfã · "
                    + "Run reconstruiu e a peça nova caiu sob gravidade");
                Pass();
                _phase = Phase.Done;
                break;
        }
    }

    private void Force(ushort coil, bool value) =>
        Loop.Enqueue(new ForceIoCommand(new IoAddress(IoArea.Coil, coil), value));
}

/// <summary>
/// Spec 002 / T038 / V-I (R4 / Artigo VII.1): falha do master Modbus COM uma
/// peça presa congela, não derruba. Um master NModbus em loopback comanda a
/// garra a agarrar a peça (como o <see cref="FailSafeScenario"/> comanda a luz),
/// depois some. O watchdog do driver derruba Run→Pause; a peça continua PRESA e
/// PARADA — não cai — e o pick-and-place mantém <c>HeldPartId</c>.
/// </summary>
public sealed class PickPlaceDriverFaultScenario : DeviceScenario
{
    private const ushort Port = 15503;
    private const int TimeoutMs = 2000;

    private enum Phase { WaitListen, Connect, Settle, Lower, Grip, WaitFault, Frozen, Done }

    private readonly Stopwatch _clock = Stopwatch.StartNew();
    private Phase _phase = Phase.WaitListen;
    private TcpClient? _tcp;
    private IModbusMaster? _master;
    private Action<string>? _onFault;
    private volatile string? _faultReason;
    private float _frozenY;
    private int _wait;

    public override void Begin()
    {
        _onFault = reason => _faultReason = reason;
        Loop.DriverFault += _onFault;
        StartRun(PickPlaceModeScene.Build(new ConnectionConfig
        {
            Driver = ConnectionConfig.ModbusTcpServerKey,
            BindAddress = "127.0.0.1",
            Port = Port,
            UnitId = 1,
            TimeoutMs = TimeoutMs,
        }));
    }

    public override void Tick()
    {
        // As fases de rede dependem do relógio de parede — guarda wall-clock.
        if (_clock.Elapsed > TimeSpan.FromSeconds(30))
        {
            Finish($"timeout de parede na fase {_phase} (driver={Loop.DriverState}, modo={Loop.Mode}).");
            return;
        }

        switch (_phase)
        {
            case Phase.WaitListen:
                if (Loop.DriverState == DriverState.Starting)
                    _phase = Phase.Connect;
                else if (Loop.DriverState == DriverState.Faulted)
                    Finish("driver caiu antes de escutar (porta ocupada?).");
                break;

            case Phase.Connect:
                _tcp = new TcpClient();
                _tcp.Connect("127.0.0.1", Port);
                _master = new ModbusFactory().CreateMaster(_tcp);
                _master.Transport.ReadTimeout = 3000;
                _master.Transport.WriteTimeout = 3000;
                _master.ReadInputs(1, 0, 1); // fixa atividade e a conexão
                _phase = Phase.Settle;
                break;

            case Phase.Settle:
            {
                var box = PartNear(0.4f, 0f, radius: 3f);
                if (box is null)
                    return;
                if (box.Body.LinearVelocity.Length() < 0.02f && box.Body.Pose.Pos.Y < 0.2f)
                {
                    _master!.WriteSingleCoil(1, PickPlaceModeScene.LowerCoil, true);
                    _wait = 2;
                    _phase = Phase.Lower;
                }
                break;
            }

            case Phase.Lower:
                if (--_wait > 0)
                    return;
                if (_master!.ReadInputs(1, PickPlaceModeScene.DiLowered, 1)[0])
                {
                    _master.WriteSingleCoil(1, PickPlaceModeScene.GripCoil, true);
                    _wait = 2;
                    _phase = Phase.Grip;
                }
                else
                {
                    _wait = 2; // segue baixando; mantém o master ativo
                }
                break;

            case Phase.Grip:
                if (--_wait > 0)
                    return;
                if (!_master!.ReadInputs(1, PickPlaceModeScene.DiHolding, 1)[0])
                {
                    _wait = 2;
                    return;
                }
                // Peça presa. Master some sem aviso — o watchdog tem de derrubar Run.
                _master.Dispose();
                _tcp!.Close();
                _master = null;
                _tcp = null;
                _phase = Phase.WaitFault;
                break;

            case Phase.WaitFault:
                if (Loop.Mode == SimMode.Pause && _faultReason is { } reason)
                {
                    if (!reason.Contains("timeout", StringComparison.OrdinalIgnoreCase)
                        && !reason.Contains("atividade", StringComparison.OrdinalIgnoreCase))
                    {
                        Finish($"pausou, mas com motivo inesperado: '{reason}'.");
                        return;
                    }

                    var pp = FindPickPlace();
                    if (pp is null || pp.HeldPartId == 0)
                    {
                        Finish("pausou por falha, mas a garra soltou a peça (HeldPartId==0).");
                        return;
                    }
                    if (Loop.Parts is null || !Loop.Parts.TryGet(pp.HeldPartId, out var held))
                    {
                        Finish("a peça presa sumiu do mundo ao pausar.");
                        return;
                    }
                    _frozenY = held.Body.Pose.Pos.Y;
                    _wait = 30;
                    _phase = Phase.Frozen;
                }
                else if (Loop.Mode == SimMode.Pause)
                {
                    Finish("caiu para Pause sem emitir DriverFault com o motivo.");
                }
                break;

            case Phase.Frozen:
            {
                // Congelado: por ~30 ticks a peça segue presa e não se move.
                var pp = FindPickPlace();
                if (pp is null || pp.HeldPartId == 0)
                {
                    Finish("durante a pausa a garra largou a peça.");
                    return;
                }
                if (Loop.Parts is null || !Loop.Parts.TryGet(pp.HeldPartId, out var held))
                {
                    Finish("a peça presa sumiu durante a pausa.");
                    return;
                }
                if (MathF.Abs(held.Body.Pose.Pos.Y - _frozenY) > 0.02f)
                {
                    Finish($"a peça se moveu na pausa (y {_frozenY:0.###} → "
                           + $"{held.Body.Pose.Pos.Y:0.###}) — deveria congelar, não cair.");
                    return;
                }
                if (--_wait <= 0)
                {
                    Godot.GD.Print($"   master caiu com peça presa · Run→Pause · "
                        + $"peça segue presa e parada em y={_frozenY:0.###} (não caiu)");
                    Finish(null);
                    _phase = Phase.Done;
                }
                break;
            }
        }
    }

    private PickPlace? FindPickPlace()
    {
        foreach (var device in Loop.Devices)
            if (device is PickPlace pp)
                return pp;
        return null;
    }

    private void Finish(string? failure)
    {
        if (_onFault is not null)
        {
            Loop.DriverFault -= _onFault;
            _onFault = null;
        }
        _master?.Dispose();
        _tcp?.Close();

        if (failure is null)
            Pass();
        else
            Fail(failure);
    }
}
