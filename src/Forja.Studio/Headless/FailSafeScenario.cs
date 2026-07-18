using System;
using System.Diagnostics;
using System.Net.Sockets;
using Forja.Anvil.Contracts;
using Forja.Anvil.Scene;
using Forja.Core.Devices;
using NModbus;

namespace Forja.Studio.Headless;

/// <summary>
/// T050 / RF-06 / Artigo VII: falha segura ponta a ponta com o driver Modbus
/// REAL rodando dentro do Godot e um master NModbus em loopback:
///  (a) Forja escuta (Starting) → master conecta e lê → Ready;
///  (b) sensor→master: HMI pressionado vira DI=1 no FC02;
///  (c) master→atuador: FC05 na coil acende a luz indicadora;
///  (d) master some → watchdog do driver (TimeoutMs) → Faulted → o loop cai
///      de Run para Pause e emite DriverFault com o motivo.
/// </summary>
public sealed class FailSafeScenario : DeviceScenario
{
    private const ushort Port = 15502;
    private const int TimeoutMs = 2000;
    private const uint ButtonId = 1;
    private const uint LightId = 2;

    private enum Phase { WaitListen, Connect, SensorToMaster, CoilToActuator, WaitFault, Done }

    private readonly Stopwatch _clock = Stopwatch.StartNew();
    private Phase _phase = Phase.WaitListen;
    private TcpClient? _tcp;
    private IModbusMaster? _master;
    private Action<string>? _onFault;
    private volatile string? _faultReason;
    private int _wait;

    public override void Begin()
    {
        _onFault = reason => _faultReason = reason;
        Loop.DriverFault += _onFault;
        StartRun(BuildScene());
    }

    public override void Tick()
    {
        // Fases dependem de rede/relógio de parede — o guarda é wall-clock.
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

                if (_master.ReadInputs(1, 0, 1)[0])
                {
                    Finish("DI0 já estava em 1 antes do botão ser pressionado.");
                    return;
                }

                Loop.Enqueue(new HmiCommand(ButtonId, "pressed", true));
                _wait = 3;
                _phase = Phase.SensorToMaster;
                break;

            case Phase.SensorToMaster:
                if (--_wait > 0)
                    return;
                if (Loop.DriverState != DriverState.Ready)
                {
                    Finish($"master leu mas o driver não ficou Ready (está {Loop.DriverState}).");
                    return;
                }
                if (!_master!.ReadInputs(1, 0, 1)[0])
                {
                    Finish("botão pressionado mas o master não viu DI0 == 1 (FC02).");
                    return;
                }

                _master.WriteSingleCoil(1, 0, true);
                _wait = 4;
                _phase = Phase.CoilToActuator;
                break;

            case Phase.CoilToActuator:
                if (--_wait > 0)
                    return;
                if (!LightOn())
                {
                    Finish("master escreveu a coil mas a luz indicadora não acendeu.");
                    return;
                }

                // Master some sem aviso — o watchdog tem de derrubar Run.
                _master!.Dispose();
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
                    _phase = Phase.Done;
                }
                else if (Loop.Mode == SimMode.Pause)
                {
                    Finish("caiu para Pause sem emitir DriverFault com o motivo.");
                }
                break;

            case Phase.Done:
                Finish(null);
                break;
        }
    }

    /// <summary>Encerra soltando o event handler e os sockets (o loop é
    /// compartilhado com os próximos cenários).</summary>
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

    private bool LightOn()
    {
        foreach (var device in Loop.Devices)
        {
            if (device.Id == LightId && device is IndicatorLight light)
                return light.On;
        }
        return false;
    }

    private static SceneDocument BuildScene() => new()
    {
        SchemaVersion = SceneDocument.CurrentSchemaVersion,
        Name = "fail-safe",
        Devices = new()
        {
            new DeviceInstance { Id = ButtonId, TypeId = "hmi.button" },
            new DeviceInstance { Id = LightId, TypeId = "hmi.light" },
        },
        IoMap = new()
        {
            new IoTag(ButtonId, "pressed", new IoAddress(IoArea.DiscreteInput, 0)),
            new IoTag(LightId, "on", new IoAddress(IoArea.Coil, 0)),
        },
        Connection = new ConnectionConfig
        {
            Driver = ConnectionConfig.ModbusTcpServerKey,
            BindAddress = "127.0.0.1",
            Port = Port,
            UnitId = 1,
            TimeoutMs = TimeoutMs,
        },
    };
}
