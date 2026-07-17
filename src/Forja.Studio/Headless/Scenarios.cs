using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using Forja.Anvil;
using Forja.Anvil.Contracts;
using Forja.Anvil.Scene;

namespace Forja.Studio.Headless;

/// <summary>Boot mínimo: cena vazia entra em Run e ticka (parte do T016).</summary>
public sealed class SmokeBootScenario : HeadlessScenario
{
    private int _frames;

    public override void Begin() => StartRun(new SceneDocument
    {
        SchemaVersion = SceneDocument.CurrentSchemaVersion,
        Name = "vazia",
    });

    public override void Tick()
    {
        if (++_frames < 60)
            return;

        if (Loop.Mode != SimMode.Run)
            Fail($"esperava Run após 60 frames, está em {Loop.Mode}.");
        else if (Loop.TickNumber < 55)
            Fail($"só {Loop.TickNumber} ticks em 60 frames — física não está a 60 Hz?");
        else
            Pass();
    }
}

/// <summary>
/// T017 / RF-04-a: emissor → esteira → calha → sink. Em 600 ticks as caixas
/// emitidas devem ser transportadas e removidas NO SINK (não na kill-zone).
/// </summary>
public sealed class ConveyorFlowScenario : HeadlessScenario
{
    private const int Ticks = 600;
    private const float SinkMinX = 2.5f;

    private readonly Dictionary<uint, Vec3> _lastPos = new();
    private readonly HashSet<uint> _alive = new();
    private int _spawned;
    private int _sunk;
    private int _lostElsewhere;
    private int _frames;

    public override void Begin() => StartRun(LoadDemoScene());

    public override void Tick()
    {
        if (++_frames > Ticks + 120)
        {
            Fail($"timeout: {_frames} frames (spawned={_spawned}, sunk={_sunk}).");
            return;
        }

        var parts = Loop.Parts;
        if (parts is null)
            return;

        _alive.Clear();
        foreach (var part in parts.All)
        {
            _alive.Add(part.Id);
            if (_lastPos.TryAdd(part.Id, part.Body.Pose.Pos))
                _spawned++;
            else
                _lastPos[part.Id] = part.Body.Pose.Pos;
        }

        foreach (var (id, pos) in _lastPos)
        {
            if (_alive.Contains(id))
                continue;
            if (pos.X > SinkMinX && pos.Y > -2f)
                _sunk++;
            else
                _lostElsewhere++;
        }

        foreach (uint id in _lastPos.Keys.Where(id => !_alive.Contains(id)).ToList())
            _lastPos.Remove(id);

        if (Loop.TickNumber < Ticks)
            return;

        if (_spawned < 2)
            Fail($"emissor só criou {_spawned} caixa(s) em {Ticks} ticks.");
        else if (_lostElsewhere > 0)
            Fail($"{_lostElsewhere} caixa(s) morreram fora do sink (kill-zone).");
        else if (_sunk < _spawned)
            Fail($"só {_sunk}/{_spawned} caixas chegaram ao sink em {Ticks} ticks " +
                 $"({_alive.Count} ainda em trânsito).");
        else
            Pass();
    }
}

/// <summary>
/// T018 / Artigo I.4 / RNF-03: mesma cena + seed, 10.000 ticks, duas
/// execuções → hash idêntico tick a tick; divergência reporta o 1º tick.
/// </summary>
public sealed class DeterminismScenario : HeadlessScenario
{
    private const int Ticks = 10_000;

    private SceneDocument _doc = null!;
    private ulong[] _run1 = Array.Empty<ulong>();
    private int _phase;
    private int _cooldown;
    private int _firstDivergence = -1;
    private ulong _lastSeen;
    private long _safety;

    public override void Begin()
    {
        _doc = LoadDemoScene();

        // Fluxo contínuo: emissor sem limite exercita física o tempo todo.
        var emitter = _doc.Devices.First(d => d.TypeId == "emitter");
        emitter.Params["maxParts"] = JsonSerializer.SerializeToElement(0);

        _run1 = new ulong[Ticks + 1];
        StartRun(_doc);
    }

    public override void Tick()
    {
        if (++_safety > 4L * Ticks)
        {
            Fail("timeout — a simulação não avançou como esperado.");
            return;
        }

        if (_cooldown > 0)
        {
            if (--_cooldown == 0)
                StartRun(_doc);
            return;
        }

        ulong t = Loop.TickNumber;
        if (Loop.Mode != SimMode.Run || t == 0 || t == _lastSeen)
            return;
        _lastSeen = t;

        if (t <= Ticks)
        {
            ulong hash = Loop.ComputeStateHash();
            if (_phase == 0)
                _run1[t] = hash;
            else if (_firstDivergence < 0 && _run1[t] != hash)
                _firstDivergence = (int)t;
        }

        if (t < Ticks)
            return;

        if (_phase == 0)
        {
            _phase = 1;
            _lastSeen = 0;
            Loop.Enqueue(new SetModeCommand(SimMode.Edit));
            _cooldown = 3;
            return;
        }

        if (_firstDivergence >= 0)
            Fail($"hashes divergem — primeiro tick divergente: {_firstDivergence} " +
                 $"(run1={_run1[_firstDivergence]:x16}).");
        else
            Pass();
    }
}
