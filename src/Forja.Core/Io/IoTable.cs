using System.Text.Json;
using Forja.Anvil.Catalog;
using Forja.Anvil.Contracts;
using Forja.Anvil.Scene;
using Forja.Core.State;

namespace Forja.Core.Io;

/// <summary>Linha da Tabela de I/O para a UI (RF-05). Pontos analógicos
/// (<see cref="IsAnalog"/>) trazem valor bruto e em unidade de engenharia;
/// pontos digitais usam <see cref="Value"/> (Fase 2).</summary>
public sealed record IoPointView(
    uint DeviceId,
    string PortName,
    IoAddress Address,
    IoDirection Direction,
    bool Value,
    bool Forced,
    bool IsAnalog = false,
    float AnalogEu = 0f,
    ushort AnalogRaw = 0,
    string Unit = "");

/// <summary>
/// Snapshot de I/O por tick (data-model §7). Sensores escrevem inputs no fim
/// do tick; atuadores leem outputs no início do tick seguinte. "Forçar"
/// (RF-05) prevalece sobre driver e sensores até ser liberado.
///
/// Fase 2 (ADR 0005): canal de palavras paralelo aos bits. O device fala
/// unidade de engenharia (float); o registrador carrega bruto (ushort). A
/// conversão EU↔bruto vive aqui, na fronteira, e em nenhum outro lugar
/// (contrato scaling-eu-raw S1).
/// </summary>
public sealed class IoTable
{
    private readonly record struct Point(IoDirection Direction, int Offset);

    /// <summary>Ponto analógico: offset + faixas para converter EU↔bruto.</summary>
    private readonly record struct AnalogPoint(
        IoDirection Direction, int Offset,
        float EuMin, float EuMax, ushort RawMin, ushort RawMax, string Unit);

    private readonly Dictionary<(uint DeviceId, string Port), Point> _byPort = new();
    private readonly Dictionary<IoAddress, Point> _byAddress = new();
    private readonly Dictionary<(uint DeviceId, string Port), AnalogPoint> _analogByPort = new();
    private readonly Dictionary<IoAddress, AnalogPoint> _analogByAddress = new();
    private readonly List<IoTag> _tags;
    private readonly Dictionary<(uint, string), IoDirection> _directions = new();

    private readonly bool[] _inputs;
    private readonly bool[] _outputs;
    private readonly bool?[] _forcedInputs;
    private readonly bool?[] _forcedOutputs;

    // Canais de palavra (Fase 2), independentes do espaço de endereços dos bits.
    private readonly ushort[] _inputWords;
    private readonly ushort[] _outputWords;
    private readonly ushort?[] _forcedInputWords;
    private readonly ushort?[] _forcedOutputWords;

    // Buffers reusados para os snapshots (contrato W5: sem alocação por tick).
    private readonly bool[] _inputBitsBuffer;
    private readonly ushort[] _inputWordsBuffer;

    public int InputCount => _inputs.Length;
    public int OutputCount => _outputs.Length;
    public int InputWordCount => _inputWords.Length;
    public int OutputWordCount => _outputWords.Length;

    public IoTable(SceneDocument doc, DeviceCatalog catalog)
    {
        _tags = doc.IoMap.OrderBy(t => t.Address.Offset).ThenBy(t => t.DeviceId).ToList();
        var devicesById = doc.Devices.ToDictionary(d => d.Id);

        int maxIn = -1, maxOut = -1, maxInWord = -1, maxOutWord = -1;
        foreach (var tag in _tags)
        {
            var (direction, type) = ResolvePort(doc, catalog, tag);
            _directions[(tag.DeviceId, tag.PortName)] = direction;

            bool isWord = type == PortType.Word;
            if (direction == IoDirection.In)
            {
                if (isWord) maxInWord = Math.Max(maxInWord, tag.Address.Offset);
                else maxIn = Math.Max(maxIn, tag.Address.Offset);
            }
            else
            {
                if (isWord) maxOutWord = Math.Max(maxOutWord, tag.Address.Offset);
                else maxOut = Math.Max(maxOut, tag.Address.Offset);
            }
        }

        _inputs = new bool[maxIn + 1];
        _outputs = new bool[maxOut + 1];
        _forcedInputs = new bool?[maxIn + 1];
        _forcedOutputs = new bool?[maxOut + 1];

        _inputWords = new ushort[maxInWord + 1];
        _outputWords = new ushort[maxOutWord + 1];
        _forcedInputWords = new ushort?[maxInWord + 1];
        _forcedOutputWords = new ushort?[maxOutWord + 1];

        _inputBitsBuffer = new bool[_inputs.Length];
        _inputWordsBuffer = new ushort[_inputWords.Length];

        foreach (var tag in _tags)
        {
            var (direction, portType) = ResolvePort(doc, catalog, tag);
            if (portType == PortType.Word)
            {
                var device = devicesById[tag.DeviceId];
                var type = catalog.Get(device.TypeId);
                float euMin = ReadFloatParam(device, type, "euMin", 0f);
                float euMax = ReadFloatParam(device, type, "euMax", ushort.MaxValue);
                string unit = ReadStringParam(device, type, "euUnit", "");
                var scale = tag.Scale ?? new AnalogScale();
                var ap = new AnalogPoint(direction, tag.Address.Offset,
                    euMin, euMax, scale.RawMin, scale.RawMax, unit);
                _analogByPort[(tag.DeviceId, tag.PortName)] = ap;
                _analogByAddress[tag.Address] = ap;
            }
            else
            {
                var point = new Point(direction, tag.Address.Offset);
                _byPort[(tag.DeviceId, tag.PortName)] = point;
                _byAddress[tag.Address] = point;
            }
        }
    }

    private static (IoDirection Direction, PortType Type) ResolvePort(
        SceneDocument doc, DeviceCatalog catalog, IoTag tag)
    {
        var device = doc.Devices.FirstOrDefault(d => d.Id == tag.DeviceId)
            ?? throw new InvalidOperationException(
                $"IoTable: tag órfã (device {tag.DeviceId}) — rode o IoMapValidator antes.");
        var port = catalog.Get(device.TypeId).Ports.FirstOrDefault(p => p.PortName == tag.PortName)
            ?? throw new InvalidOperationException(
                $"IoTable: porta desconhecida '{tag.PortName}' no device {tag.DeviceId}.");
        return (port.Direction, port.Type);
    }

    private static float ReadFloatParam(DeviceInstance instance, DeviceTypeDef type, string name, float fallback)
    {
        if (instance.Params.TryGetValue(name, out var el) && el.ValueKind == JsonValueKind.Number)
            return el.GetSingle();
        var def = type.ParamDefs.FirstOrDefault(p => p.Name == name);
        if (def?.Default is JsonElement d && d.ValueKind == JsonValueKind.Number)
            return d.GetSingle();
        return fallback;
    }

    private static string ReadStringParam(DeviceInstance instance, DeviceTypeDef type, string name, string fallback)
    {
        if (instance.Params.TryGetValue(name, out var el) && el.ValueKind == JsonValueKind.String)
            return el.GetString() ?? fallback;
        var def = type.ParamDefs.FirstOrDefault(p => p.Name == name);
        if (def?.Default is JsonElement d && d.ValueKind == JsonValueKind.String)
            return d.GetString() ?? fallback;
        return fallback;
    }

    // ---- Conversão EU ↔ bruto (contrato scaling-eu-raw S2–S4) ----

    /// <summary>EU → bruto, linear e saturante, arredondamento ToEven (determinístico).</summary>
    internal static ushort EuToRaw(float eu, float euMin, float euMax, ushort rawMin, ushort rawMax)
    {
        float span = euMax - euMin;
        if (span == 0f) return rawMin; // validação já barra, mas nunca dividir por zero
        float t = (eu - euMin) / span;
        float rawF = rawMin + t * (rawMax - rawMin);
        float rounded = MathF.Round(rawF, MidpointRounding.ToEven);
        float lo = Math.Min(rawMin, rawMax);
        float hi = Math.Max(rawMin, rawMax);
        return (ushort)Math.Clamp(rounded, lo, hi);
    }

    /// <summary>Bruto → EU, inversa da <see cref="EuToRaw"/>.</summary>
    internal static float RawToEu(ushort raw, float euMin, float euMax, ushort rawMin, ushort rawMax)
    {
        int rawSpan = rawMax - rawMin;
        if (rawSpan == 0) return euMin;
        float t = (raw - rawMin) / (float)rawSpan;
        return euMin + t * (euMax - euMin);
    }

    // ---- Bits (inalterado da Fase 1) ----

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

    // ---- Palavras (Fase 2) ----

    /// <summary>Sensor grava sua palavra de entrada em EU; a fronteira converte
    /// para bruto (fim do tick).</summary>
    public void SetInputWord(uint deviceId, string port, float eu)
    {
        if (_analogByPort.TryGetValue((deviceId, port), out var a) && a.Direction == IoDirection.In)
            _inputWords[a.Offset] = EuToRaw(eu, a.EuMin, a.EuMax, a.RawMin, a.RawMax);
    }

    /// <summary>Atuador lê seu setpoint de saída em EU (início do tick), com
    /// override aplicado; a fronteira converte do bruto do registrador.</summary>
    public float GetOutputWord(uint deviceId, string port)
    {
        if (_analogByPort.TryGetValue((deviceId, port), out var a) && a.Direction == IoDirection.Out)
        {
            ushort raw = _forcedOutputWords[a.Offset] ?? _outputWords[a.Offset];
            return RawToEu(raw, a.EuMin, a.EuMax, a.RawMin, a.RawMax);
        }
        return 0f;
    }

    /// <summary>Força uma palavra bruta (RF-05). null libera o override.</summary>
    public void ForceWord(IoAddress address, ushort? raw)
    {
        if (!_analogByAddress.TryGetValue(address, out var a))
            return;
        if (a.Direction == IoDirection.In)
            _forcedInputWords[a.Offset] = raw;
        else
            _forcedOutputWords[a.Offset] = raw;
    }

    // ---- Snapshots ----

    /// <summary>Snapshot dos sensores para o driver, com overrides de entrada.
    /// Bits e palavras num só snapshot (contrato W2). Buffers reusados (W5).</summary>
    public IoSnapshot BuildInputSnapshot(ulong tick)
    {
        for (int i = 0; i < _inputBitsBuffer.Length; i++)
            _inputBitsBuffer[i] = _forcedInputs[i] ?? _inputs[i];
        for (int i = 0; i < _inputWordsBuffer.Length; i++)
            _inputWordsBuffer[i] = _forcedInputWords[i] ?? _inputWords[i];
        return new IoSnapshot(tick, _inputBitsBuffer, _inputWordsBuffer);
    }

    /// <summary>
    /// Aplica as saídas vindas do driver. Snapshot inválido (falha — C1) é
    /// ignorado: quem pausa a simulação é a transição para Faulted (C2),
    /// nunca valores velhos aplicados silenciosamente. Vale para bits E palavras
    /// (contrato W4).
    /// </summary>
    public void ApplyOutputSnapshot(IoSnapshot snapshot)
    {
        if (!snapshot.Valid)
            return;
        var bits = snapshot.Bits.Span;
        int nb = Math.Min(bits.Length, _outputs.Length);
        for (int i = 0; i < nb; i++)
            _outputs[i] = bits[i];

        var words = snapshot.Words.Span;
        int nw = Math.Min(words.Length, _outputWords.Length);
        for (int i = 0; i < nw; i++)
            _outputWords[i] = words[i];
    }

    /// <summary>Linhas ordenadas por endereço para a Tabela de I/O (RF-05).</summary>
    public IReadOnlyList<IoPointView> BuildView()
    {
        var rows = new List<IoPointView>(_tags.Count);
        foreach (var tag in _tags)
        {
            if (_analogByPort.TryGetValue((tag.DeviceId, tag.PortName), out var a))
            {
                bool forced = a.Direction == IoDirection.In
                    ? _forcedInputWords[a.Offset].HasValue
                    : _forcedOutputWords[a.Offset].HasValue;
                ushort raw = a.Direction == IoDirection.In
                    ? _forcedInputWords[a.Offset] ?? _inputWords[a.Offset]
                    : _forcedOutputWords[a.Offset] ?? _outputWords[a.Offset];
                float eu = RawToEu(raw, a.EuMin, a.EuMax, a.RawMin, a.RawMax);
                rows.Add(new IoPointView(tag.DeviceId, tag.PortName, tag.Address, a.Direction,
                    Value: false, forced, IsAnalog: true, AnalogEu: eu, AnalogRaw: raw, Unit: a.Unit));
                continue;
            }

            var p = _byPort[(tag.DeviceId, tag.PortName)];
            bool bForced = p.Direction == IoDirection.In
                ? _forcedInputs[p.Offset].HasValue
                : _forcedOutputs[p.Offset].HasValue;
            bool value = p.Direction == IoDirection.In
                ? _forcedInputs[p.Offset] ?? _inputs[p.Offset]
                : _forcedOutputs[p.Offset] ?? _outputs[p.Offset];
            rows.Add(new IoPointView(tag.DeviceId, tag.PortName, tag.Address, p.Direction, value, bForced));
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
        Array.Clear(_inputWords);
        Array.Clear(_outputWords);
        Array.Fill(_forcedInputWords, null);
        Array.Fill(_forcedOutputWords, null);
    }

    /// <summary>Bits e palavras entram no hash em ordem de endereço (Artigo I.4,
    /// contrato W6). Palavra é bruto ushort — determinístico, sem float no estado.</summary>
    public void WriteState(ref StateHasher hasher)
    {
        foreach (bool b in _inputs)
            hasher.Add(b);
        foreach (bool b in _outputs)
            hasher.Add(b);
        foreach (ushort w in _inputWords)
            hasher.Add(w);
        foreach (ushort w in _outputWords)
            hasher.Add(w);
    }
}
