using System.Collections.Generic;
using System.Linq;
using Forja.Anvil;
using Forja.Anvil.Contracts;
using Forja.Anvil.Scene;
using Forja.Core.Persistence;
using Godot;

namespace Forja.Studio.Headless;

/// <summary>
/// T052 / RF-09: ensaio da cena demo do separador SEM PLC — este cenário
/// reproduz a lógica do separador.st forçando as coils (driver nulo):
/// esteira roda; peça ALTA no sensor → para esteira, estende pistão, recolhe
/// após o fim de curso; peça baixa segue direto. Aprova quando pelo menos
/// uma peça saiu por CADA rota (calha lateral e fim da esteira) e nenhuma
/// se perdeu fora dos sinks. Valida a geometria da cena antes do aceite
/// manual com OpenPLC (T054).
/// </summary>
public sealed class SeparadorDemoScenario : HeadlessScenario
{
    private const ushort DetectDi = 0;
    private const ushort ExtendedDi = 1;
    private const ushort RunCoil = 0;
    private const ushort ExtendCoil = 1;

    private readonly Dictionary<uint, Vec3> _lastPos = new();
    private readonly HashSet<uint> _alive = new();

    private bool _pushing;
    private bool _lastDetect;
    private bool _runForced;
    private bool _extendForced;
    private int _retractCountdown;
    private int _tall;
    private int _low;
    private int _lost;
    private long _safety;

    public override void Begin()
    {
        string path = ProjectSettings.GlobalizePath("res://demo/separador-altura.forja");
        var doc = SceneSerializer.LoadFile(path).Require();

        // O ensaio dispensa o PLC: driver nulo + coils forçadas abaixo.
        StartRun(doc with { Connection = new ConnectionConfig { Driver = ConnectionConfig.NullDriverKey } });
    }

    public override void Tick()
    {
        if (++_safety > 12000)
        {
            Fail($"timeout: altas={_tall}, baixas={_low}, perdidas={_lost}.");
            return;
        }
        if (Loop.Io is null || Loop.Parts is null)
            return;

        PlcLogic();
        TrackParts();

        if (_tall >= 1 && _low >= 1 && _lost == 0)
            Pass();
    }

    /// <summary>Mesma lógica do separador.st, coil a coil.</summary>
    private void PlcLogic()
    {
        bool detect = Di(DetectDi);
        bool extended = Di(ExtendedDi);

        if (detect && !_lastDetect)
            _pushing = true;
        _lastDetect = detect;

        if (_pushing && extended && _retractCountdown == 0)
            _retractCountdown = 36; // ~600 ms no fim de curso
        if (_retractCountdown > 0 && --_retractCountdown == 0)
            _pushing = false;

        ForceCoil(ExtendCoil, _pushing, ref _extendForced);
        ForceCoil(RunCoil, !_pushing, ref _runForced);
    }

    private void ForceCoil(ushort offset, bool value, ref bool current)
    {
        if (current == value && _safety > 1)
            return;
        current = value;
        Loop.Enqueue(new ForceIoCommand(new IoAddress(IoArea.Coil, offset), value));
    }

    /// <summary>Classifica cada peça removida pela última posição vista:
    /// calha lateral (+Z), fim da esteira (+X) ou perdida (kill-zone).</summary>
    private void TrackParts()
    {
        _alive.Clear();
        foreach (var part in Loop.Parts!.All)
        {
            _alive.Add(part.Id);
            _lastPos[part.Id] = part.Body.Pose.Pos;
        }

        List<uint>? gone = null;
        foreach (var (id, pos) in _lastPos)
        {
            if (_alive.Contains(id))
                continue;
            if (pos.Z > 1.0f && pos.Y > -2f)
                _tall++;
            else if (pos.X > 2.4f && pos.Y > -2f)
                _low++;
            else
                _lost++;
            (gone ??= new List<uint>()).Add(id);
        }
        if (gone is not null)
        {
            foreach (uint id in gone)
                _lastPos.Remove(id);
        }
    }

    private bool Di(ushort offset)
    {
        foreach (var row in Loop.Io!.BuildView())
        {
            if (row.Address.Area == IoArea.DiscreteInput && row.Address.Offset == offset)
                return row.Value;
        }
        return false;
    }
}
