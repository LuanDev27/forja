using System;
using System.Text.Json;
using Forja.Anvil;
using Forja.Anvil.Scene;
using Godot;

namespace Forja.Studio.Headless;

/// <summary>
/// T056 / RNF-07: soak automatizado — proxy do soak manual de 8 h.
///
/// Emissor contínuo despeja peças numa esteira curta; elas caem do fim e
/// morrem na kill-zone. São 30 min de tempo SIMULADO (108 000 ticks a
/// 60 Hz), que o TimeScale do runner comprime em dezenas de segundos de
/// parede — milhares de ciclos criar/destruir, que é o que expõe
/// vazamento: objeto gerenciado (Part/behaviors) ou RID de física.
///
/// Gates: memória gerenciada e memória nativa da engine no fim ≤ 105% da
/// linha de base, e população de peças estável (a kill-zone tem de estar
/// removendo no mesmo ritmo em que o emissor cria).
/// </summary>
public sealed class SoakScenario : HeadlessScenario
{
    private const int WarmupTicks = 1_200;
    private const int SoakTicks = 108_000; // 30 min a 60 Hz
    private const double MemoryBudget = 1.05;
    private const int MaxAlive = 60;

    private enum Phase { Warmup, Soak, Done }

    private Phase _phase = Phase.Warmup;
    private int _wait = WarmupTicks;
    private int _remaining = SoakTicks;
    private long _managedBase;
    private long _nativeBase;
    private int _peakAlive;

    public override void Begin() => StartRun(BuildScene());

    public override void Tick()
    {
        if (Loop.Parts is not { } parts)
            return;

        _peakAlive = Math.Max(_peakAlive, parts.Count);

        switch (_phase)
        {
            case Phase.Warmup:
                if (--_wait > 0)
                    return;
                _managedBase = SampleManaged();
                _nativeBase = (long)OS.GetStaticMemoryUsage();
                _phase = Phase.Soak;
                break;

            case Phase.Soak:
                if (--_remaining > 0)
                    return;
                _phase = Phase.Done;
                break;

            case Phase.Done:
                Evaluate(parts.Count);
                break;
        }
    }

    private void Evaluate(int alive)
    {
        long managed = SampleManaged();
        long native = (long)OS.GetStaticMemoryUsage();
        double managedRatio = (double)managed / _managedBase;
        double nativeRatio = (double)native / _nativeBase;

        GD.Print($"   {SoakTicks / 3600} min simulados · " +
                 $"gerenciada {_managedBase / 1024} KiB → {managed / 1024} KiB ({managedRatio:P1}) · " +
                 $"nativa {_nativeBase / 1024} KiB → {native / 1024} KiB ({nativeRatio:P1}) · " +
                 $"peças vivas {alive} (pico {_peakAlive})");

        if (managedRatio > MemoryBudget)
            Fail($"memória gerenciada cresceu para {managedRatio:P1} da linha de base " +
                 $"(limite {MemoryBudget:P0}) — vazamento de objeto.");
        else if (nativeRatio > MemoryBudget)
            Fail($"memória nativa cresceu para {nativeRatio:P1} da linha de base " +
                 $"(limite {MemoryBudget:P0}) — RID de física vazando.");
        else if (alive > MaxAlive)
            Fail($"{alive} peças vivas no fim (limite {MaxAlive}) — kill-zone não " +
                 "está acompanhando o emissor.");
        else
            Pass();
    }

    private static long SampleManaged()
    {
        GC.Collect(2, GCCollectionMode.Forced, blocking: true);
        GC.WaitForPendingFinalizers();
        GC.Collect(2, GCCollectionMode.Forced, blocking: true);
        return GC.GetTotalMemory(forceFullCollection: false);
    }

    private static JsonElement N(double v) => JsonSerializer.SerializeToElement(v);

    private static JsonElement S(string v) => JsonSerializer.SerializeToElement(v);

    /// <summary>Emissor → esteira curta → queda livre → kill-zone.</summary>
    private static SceneDocument BuildScene() => new()
    {
        SchemaVersion = SceneDocument.CurrentSchemaVersion,
        Name = "soak",
        Seed = 56,
        Devices = new()
        {
            new DeviceInstance
            {
                Id = 1, TypeId = "conveyor.belt",
                Transform = new Pose(new Vec3(0f, 0.2f, 0f), 0f),
                Params = new() { ["length"] = N(3), ["width"] = N(0.5), ["speed"] = N(1.0) },
            },
            new DeviceInstance
            {
                Id = 2, TypeId = "emitter",
                Transform = new Pose(new Vec3(-1.2f, 0.6f, 0f), 0f),
                Params = new()
                {
                    ["interval"] = N(0.5), ["maxParts"] = N(0),
                    ["sizes"] = S("S,M,L"), ["material"] = S("mix"),
                },
            },
        },
        IoMap = new(),
        Connection = new ConnectionConfig { Driver = "null" },
    };
}
