using System.Collections.Generic;
using System.Diagnostics;
using System.Text.Json;
using Forja.Anvil;
using Forja.Anvil.Contracts;
using Forja.Anvil.Scene;
using Forja.Core.Persistence;
using Godot;

namespace Forja.Studio.Headless;

/// <summary>
/// T057 / RNF-04 e RNF-05: a Forja abre em menos de 5 s e uma cena de 100
/// dispositivos carrega em menos de 2 s.
///
/// "Carregar" aqui é o que o usuário sente ao clicar Abrir e mandar Rodar:
/// desserializar o `.forja` MAIS montar a cena (corpos de física, tabela de
/// I/O, comportamentos). Medir só o parse esconderia o custo real.
/// </summary>
public sealed class StartupLoadScenario : HeadlessScenario
{
    private const int DeviceCount = 100;
    private const long StartupBudgetMs = 5_000;
    private const long LoadBudgetMs = 2_000;

    private enum Phase { Load, Build, Done }

    private Phase _phase = Phase.Load;
    private Stopwatch _buildTimer = new();
    private long _parseMs;
    private long _buildMs;
    private long _safety;

    public override void Begin()
    {
        string path = ProjectSettings.GlobalizePath("user://perf-100-devices.forja");
        SceneSerializer.SaveFile(BuildScene(), path).Require();

        var timer = Stopwatch.StartNew();
        var loaded = SceneSerializer.LoadFile(path).Require();
        timer.Stop();
        _parseMs = timer.ElapsedMilliseconds;

        _buildTimer = Stopwatch.StartNew();
        StartRun(loaded); // o Build acontece no próximo tick, ao entrar em Run
    }

    public override void Tick()
    {
        if (++_safety > 3_000)
        {
            Fail($"timeout na fase {_phase}.");
            return;
        }

        switch (_phase)
        {
            case Phase.Load:
                if (Loop.Mode != SimMode.Run)
                    return; // comando de modo ainda na fila
                _buildTimer.Stop();
                _buildMs = _buildTimer.ElapsedMilliseconds;
                _phase = Phase.Build;
                break;

            case Phase.Build:
                // Um punhado de ticks com a cena cheia para provar que ela
                // não só carregou, como roda.
                if (Loop.TickNumber < 30)
                    return;
                _phase = Phase.Done;
                break;

            case Phase.Done:
                Evaluate();
                break;
        }
    }

    private void Evaluate()
    {
        long total = _parseMs + _buildMs;
        GD.Print($"   startup {Main.StartupMs} ms (limite {StartupBudgetMs}) · " +
                 $"cena de {DeviceCount} dispositivos: parse {_parseMs} ms + " +
                 $"montagem {_buildMs} ms = {total} ms (limite {LoadBudgetMs}) · " +
                 $"{Loop.Devices.Count} comportamentos ativos");

        if (Main.StartupMs > StartupBudgetMs)
            Fail($"startup levou {Main.StartupMs} ms (limite {StartupBudgetMs} ms).");
        else if (total > LoadBudgetMs)
            Fail($"cena de {DeviceCount} dispositivos levou {total} ms " +
                 $"(limite {LoadBudgetMs} ms).");
        else if (Loop.Devices.Count != DeviceCount)
            Fail($"montou {Loop.Devices.Count} comportamentos, esperava {DeviceCount}.");
        else
            Pass();
    }

    private static JsonElement N(double v) => JsonSerializer.SerializeToElement(v);

    /// <summary>
    /// 100 dispositivos numa grade: 20 esteiras de I/O, 20 sensores, 10
    /// pistões, 10 luzes e 40 passivos — proporção parecida com a de uma
    /// planta real, com 60 pontos de I/O mapeados.
    /// </summary>
    private static SceneDocument BuildScene()
    {
        var devices = new List<DeviceInstance>(DeviceCount);
        var ioMap = new List<IoTag>();
        string[] passives = { "floor", "guide.side", "rack.frame", "chute" };
        uint id = 0;
        ushort coil = 0;
        ushort di = 0;

        Pose PoseFor(int index, float y) =>
            new(new Vec3(-12f + index % 20 * 1.3f, y, -6f + index / 20 * 2.5f), 0f);

        for (int i = 0; i < 20; i++)
        {
            devices.Add(new DeviceInstance
            {
                Id = ++id, TypeId = "conveyor.belt.io", Transform = PoseFor(i, 0.2f),
                Params = new() { ["length"] = N(1.2), ["width"] = N(0.5), ["speed"] = N(0.5) },
            });
            ioMap.Add(new IoTag(id, "run", new IoAddress(IoArea.Coil, coil++)));
        }

        for (int i = 0; i < 20; i++)
        {
            devices.Add(new DeviceInstance
            {
                Id = ++id, TypeId = "sensor.proximity", Transform = PoseFor(i, 0.9f),
                Params = new() { ["range"] = N(1.0) },
            });
            ioMap.Add(new IoTag(id, "detect", new IoAddress(IoArea.DiscreteInput, di++)));
        }

        for (int i = 0; i < 10; i++)
        {
            devices.Add(new DeviceInstance
            {
                Id = ++id, TypeId = "actuator.piston", Transform = PoseFor(40 + i, 0.5f),
                Params = new() { ["stroke"] = N(0.5), ["speed"] = N(2.0) },
            });
            ioMap.Add(new IoTag(id, "extend", new IoAddress(IoArea.Coil, coil++)));
            ioMap.Add(new IoTag(id, "extended", new IoAddress(IoArea.DiscreteInput, di++)));
        }

        for (int i = 0; i < 10; i++)
        {
            devices.Add(new DeviceInstance
            {
                Id = ++id, TypeId = "hmi.light", Transform = PoseFor(60 + i, 0.1f),
                Params = new(),
            });
            ioMap.Add(new IoTag(id, "on", new IoAddress(IoArea.Coil, coil++)));
        }

        for (int i = 0; i < 40; i++)
        {
            devices.Add(new DeviceInstance
            {
                Id = ++id, TypeId = passives[i % passives.Length],
                Transform = PoseFor(70 + i, i % passives.Length == 0 ? -0.1f : 0.3f),
                Params = new(),
            });
        }

        return new SceneDocument
        {
            SchemaVersion = SceneDocument.CurrentSchemaVersion,
            Name = $"{DeviceCount} dispositivos",
            Seed = 57,
            Devices = devices,
            IoMap = ioMap,
            Connection = new ConnectionConfig { Driver = "null" },
        };
    }
}
