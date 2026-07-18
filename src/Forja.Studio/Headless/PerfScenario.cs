using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Text.Json;
using Forja.Anvil;
using Forja.Anvil.Contracts;
using Forja.Anvil.Scene;
using Forja.Core.Devices;

namespace Forja.Studio.Headless;

/// <summary>
/// T055 / RNF-01: cena de estresse com 200 peças — o tick tem de caber no
/// orçamento de 60 Hz (16,6 ms).
///
/// Método de medição: o runner roda com TimeScale alto, ou seja, a engine
/// fica SATURADA (nunca ociosa entre passos de física). Nessa condição o
/// intervalo de parede entre dois passos consecutivos é o custo real do
/// passo — física do Jolt + Tick() do core — que é justamente o que o
/// RNF-01 limita. Medir só o Tick() ignoraria o solver, que é o caro.
///
/// O gate é o p95 (e não o máximo): uma pausa de GC isolada não quebra o
/// tempo real percebido, mas uma cauda pesada quebra.
/// </summary>
public sealed class PerfScenario : HeadlessScenario
{
    private const int TargetParts = 200;
    private const int SettleTicks = 300;
    private const int WarmupSamples = 30;
    private const int SampleCount = 300;
    private const double BudgetMs = 16.6;

    /// <summary>Peças em repouso precisam dormir, senão 200 peças paradas
    /// custam o mesmo que 200 em movimento (parte do RNF-01).</summary>
    private const int MinAsleepAtRest = 150;

    private const int BeltCount = 4;

    private enum Phase { Spawn, Settle, Measure, Done }

    private readonly List<double> _samples = new(SampleCount + WarmupSamples);
    private Phase _phase = Phase.Spawn;
    private int _wait;
    private long _lastStamp;
    private int _asleepAtRest;
    private int _partsAtRest;
    private long _safety;

    public override void Begin() => StartRun(BuildScene());

    public override void Tick()
    {
        if (++_safety > 20_000)
        {
            Fail($"timeout na fase {_phase} (peças={Loop.Parts?.Count ?? -1}).");
            return;
        }

        switch (_phase)
        {
            case Phase.Spawn:
                if (Loop.Parts is not { } parts)
                    return; // ainda montando a cena
                SpawnStressParts(parts);
                _wait = SettleTicks;
                _phase = Phase.Settle;
                break;

            case Phase.Settle:
                if (--_wait > 0)
                    return;
                CountSleepers();
                for (ushort coil = 0; coil < BeltCount; coil++)
                    Loop.Enqueue(new ForceIoCommand(new IoAddress(IoArea.Coil, coil), true));
                _lastStamp = Stopwatch.GetTimestamp();
                _phase = Phase.Measure;
                break;

            case Phase.Measure:
                long now = Stopwatch.GetTimestamp();
                _samples.Add((now - _lastStamp) * 1000.0 / Stopwatch.Frequency);
                _lastStamp = now;
                if (_samples.Count >= WarmupSamples + SampleCount)
                    _phase = Phase.Done;
                break;

            case Phase.Done:
                Evaluate();
                break;
        }
    }

    private void Evaluate()
    {
        int alive = Loop.Parts?.Count ?? 0;
        if (alive < TargetParts)
        {
            Fail($"perdeu peças durante a medição: {alive}/{TargetParts} vivas.");
            return;
        }

        var measured = _samples.GetRange(WarmupSamples, _samples.Count - WarmupSamples);
        measured.Sort();
        double p50 = measured[measured.Count / 2];
        double p95 = measured[(int)(measured.Count * 0.95)];
        double max = measured[^1];

        Godot.GD.Print($"   {TargetParts} peças · tick p50={p50:0.00} ms · p95={p95:0.00} ms · " +
                       $"max={max:0.00} ms · orçamento={BudgetMs} ms · " +
                       $"dormindo em repouso={_asleepAtRest}/{_partsAtRest}");

        if (p95 >= BudgetMs)
            Fail($"tick p95 {p95:0.00} ms >= orçamento {BudgetMs} ms com {TargetParts} peças.");
        else if (_asleepAtRest < MinAsleepAtRest)
            Fail($"só {_asleepAtRest}/{_partsAtRest} peças paradas dormiram " +
                 $"(mínimo {MinAsleepAtRest}) — rigidbody parado não está indo dormir.");
        else
            Pass();
    }

    private void CountSleepers()
    {
        _asleepAtRest = 0;
        _partsAtRest = 0;
        foreach (var part in Loop.Parts!.All)
        {
            if (part.Body.LinearVelocity.Length() > 0.01f)
                continue;
            _partsAtRest++;
            if (part.Body.Asleep)
                _asleepAtRest++;
        }
    }

    /// <summary>
    /// 80 peças enfileiradas nas 4 esteiras (acordam e se empurram quando as
    /// esteiras ligam) + 120 paradas no piso (têm de dormir) = 200.
    /// </summary>
    private static void SpawnStressParts(PartsManager parts)
    {
        var small = new PartKind("S", "plastic");
        var medium = new PartKind("M", "metal");

        for (int belt = 0; belt < BeltCount; belt++)
        {
            float z = BeltZ(belt);
            for (int i = 0; i < 20; i++)
                parts.SpawnBox(small, new Pose(new Vec3(-4.5f + i * 0.25f, 0.45f, z), 0f));
        }

        for (int zone = 0; zone < 2; zone++)
        {
            float z0 = zone == 0 ? -6.5f : 4.0f;
            for (int row = 0; row < 6; row++)
            {
                for (int col = 0; col < 10; col++)
                {
                    var kind = (row + col) % 3 == 0 ? medium : small;
                    parts.SpawnBox(kind, new Pose(
                        new Vec3(-5f + col * 0.5f, 0.5f, z0 + row * 0.5f), 0f));
                }
            }
        }
    }

    private static float BeltZ(int belt) => -3f + belt * 2f;

    private static JsonElement N(double v) => JsonSerializer.SerializeToElement(v);

    private static SceneDocument BuildScene()
    {
        var devices = new List<DeviceInstance>
        {
            new()
            {
                Id = 1, TypeId = "floor",
                Transform = new Pose(new Vec3(0f, -0.1f, 0f), 0f),
                Params = new() { ["sizeX"] = N(24), ["sizeY"] = N(0.2), ["sizeZ"] = N(16) },
            },
        };

        var ioMap = new List<IoTag>();
        for (int belt = 0; belt < BeltCount; belt++)
        {
            uint beltId = (uint)(2 + belt);
            devices.Add(new DeviceInstance
            {
                Id = beltId, TypeId = "conveyor.belt.io",
                Transform = new Pose(new Vec3(0f, 0.2f, BeltZ(belt)), 0f),
                Params = new() { ["length"] = N(10), ["width"] = N(0.6), ["speed"] = N(0.5) },
            });
            ioMap.Add(new IoTag(beltId, "run", new IoAddress(IoArea.Coil, (ushort)belt)));

            uint sensorId = (uint)(6 + belt);
            devices.Add(new DeviceInstance
            {
                Id = sensorId, TypeId = "sensor.height",
                Transform = new Pose(new Vec3(2f, 1.05f, BeltZ(belt)), 0f),
                Params = new() { ["range"] = N(2), ["threshold"] = N(0.4) },
            });
            ioMap.Add(new IoTag(sensorId, "detect", new IoAddress(IoArea.DiscreteInput, (ushort)belt)));
        }

        return new SceneDocument
        {
            SchemaVersion = SceneDocument.CurrentSchemaVersion,
            Name = "estresse 200 peças",
            Seed = 55,
            Devices = devices,
            IoMap = ioMap,
            Connection = new ConnectionConfig { Driver = "null" },
        };
    }
}
