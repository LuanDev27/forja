using Forja.Anvil.Catalog;
using Forja.Anvil.Contracts;
using Forja.Anvil.Scene;
using Forja.Core.State;

namespace Forja.Core.Io;

/// <summary>Linha da Tabela de I/O para a UI (RF-05).</summary>
public sealed record IoPointView(
    uint DeviceId,
    string PortName,
    IoAddress Address,
    IoDirection Direction,
    bool Value,
    bool Forced);

/// <summary>
/// Snapshot de I/O por tick (data-model §7). Sensores escrevem inputs no fim
/// do tick; atuadores leem outputs no início do tick seguinte. "Forçar"
/// (RF-05) prevalece sobre driver e sensores até ser liberado.
/// </summary>
public sealed class IoTable
{
    private readonly record struct Point(IoDirection Direction, int Offset);

    private readonly Dictionary<(uint DeviceId, string Port), Point> _byPort = new();
    private readonly Dictionary<IoAddress, Point> _byAddress = new();
    private readonly List<IoTag> _tags;
    private readonly Dictionary<(uint, string), IoDirection> _directions = new();

    private readonly bool[] _inputs;
    private readonly bool[] _outputs;
    private readonly bool?[] _forcedInputs;
    private readonly bool?[] _forcedOutputs;

    public int InputCount => _inputs.Length;
    public int OutputCount => _outputs.Length;

    public IoTable(SceneDocument doc, DeviceCatalog catalog)
    {
        _tags = doc.IoMap.OrderBy(t => t.Address.Offset).ThenBy(t => t.DeviceId).ToList();

        int maxIn = -1, maxOut = -1;
        foreach (var tag in _tags)
        {
            var direction = ResolveDirection(doc, catalog, tag);
            _directions[(tag.DeviceId, tag.PortName)] = direction;
            if (direction == IoDirection.In)
                maxIn = Math.Max(maxIn, tag.Address.Offset);
            else
                maxOut = Math.Max(maxOut, tag.Address.Offset);
        }

        _inputs = new bool[maxIn + 1];
        _outputs = new bool[maxOut + 1];
        _forcedInputs = new bool?[maxIn + 1];
        _forcedOutputs = new bool?[maxOut + 1];

        foreach (var tag in _tags)
        {
            var point = new Point(_directions[(tag.DeviceId, tag.PortName)], tag.Address.Offset);
            _byPort[(tag.DeviceId, tag.PortName)] = point;
            _byAddress[tag.Address] = point;
        }
    }

    private static IoDirection ResolveDirection(SceneDocument doc, DeviceCatalog catalog, IoTag tag)
    {
        var device = doc.Devices.FirstOrDefault(d => d.Id == tag.DeviceId)
            ?? throw new InvalidOperationException(
                $"IoTable: tag órfã (device {tag.DeviceId}) — rode o IoMapValidator antes.");
        var port = catalog.Get(device.TypeId).Ports.FirstOrDefault(p => p.PortName == tag.PortName)
            ?? throw new InvalidOperationException(
                $"IoTable: porta desconhecida '{tag.PortName}' no device {tag.DeviceId}.");
        return port.Direction;
    }

    /// <summary>Sensor grava seu bit de entrada (fim do tick).</summary>
    public void SetInput(uint deviceId, string port, bool value)
    {
        if (_byPort.TryGetValue((deviceId, port), out var p) && p.Direction == IoDirection.In)
            _inputs[p.Offset] = value;
    }

    /// <summary>Atuador lê seu bit de saída (início do tick), com override aplicado.</summary>
    public bool GetOutput(uint deviceId, string port)
    {
        if (_byPort.TryGetValue((deviceId, port), out var p) && p.Direction == IoDirection.Out)
            return _forcedOutputs[p.Offset] ?? _outputs[p.Offset];
        return false;
    }

    /// <summary>Força um bit (RF-05). null libera o override.</summary>
    public void Force(IoAddress address, bool? value)
    {
        if (!_byAddress.TryGetValue(address, out var p))
            return;
        if (p.Direction == IoDirection.In)
            _forcedInputs[p.Offset] = value;
        else
            _forcedOutputs[p.Offset] = value;
    }

    /// <summary>Snapshot dos sensores para o driver, com overrides de entrada.</summary>
    public IoSnapshot BuildInputSnapshot(ulong tick)
    {
        var bits = new bool[_inputs.Length];
        for (int i = 0; i < bits.Length; i++)
            bits[i] = _forcedInputs[i] ?? _inputs[i];
        return new IoSnapshot(tick, bits);
    }

    /// <summary>
    /// Aplica as saídas vindas do driver. Snapshot inválido (falha — C1) é
    /// ignorado: quem pausa a simulação é a transição para Faulted (C2),
    /// nunca valores velhos aplicados silenciosamente.
    /// </summary>
    public void ApplyOutputSnapshot(IoSnapshot snapshot)
    {
        if (!snapshot.Valid)
            return;
        var bits = snapshot.Bits.Span;
        int n = Math.Min(bits.Length, _outputs.Length);
        for (int i = 0; i < n; i++)
            _outputs[i] = bits[i];
    }

    /// <summary>Linhas ordenadas por endereço para a Tabela de I/O (RF-05).</summary>
    public IReadOnlyList<IoPointView> BuildView()
    {
        var rows = new List<IoPointView>(_tags.Count);
        foreach (var tag in _tags)
        {
            var p = _byPort[(tag.DeviceId, tag.PortName)];
            bool forced = p.Direction == IoDirection.In
                ? _forcedInputs[p.Offset].HasValue
                : _forcedOutputs[p.Offset].HasValue;
            bool value = p.Direction == IoDirection.In
                ? _forcedInputs[p.Offset] ?? _inputs[p.Offset]
                : _forcedOutputs[p.Offset] ?? _outputs[p.Offset];
            rows.Add(new IoPointView(tag.DeviceId, tag.PortName, tag.Address, p.Direction, value, forced));
        }
        return rows;
    }

    /// <summary>Zera valores e overrides (transição para Edit).</summary>
    public void Reset()
    {
        Array.Clear(_inputs);
        Array.Clear(_outputs);
        Array.Fill(_forcedInputs, null);
        Array.Fill(_forcedOutputs, null);
    }

    /// <summary>Bits entram no hash em ordem de endereço (Artigo I.3).</summary>
    public void WriteState(ref StateHasher hasher)
    {
        foreach (bool b in _inputs)
            hasher.Add(b);
        foreach (bool b in _outputs)
            hasher.Add(b);
    }
}
