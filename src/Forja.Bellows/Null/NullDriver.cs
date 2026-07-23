using Forja.Anvil.Contracts;
using Forja.Anvil.Scene;

namespace Forja.Bellows.Null;

/// <summary>
/// Driver sem PLC (chave "null", regras C1–C5): as saídas vêm apenas do que
/// for forçado — pela Tabela de I/O (RF-07) ou por <see cref="SetCoil"/> em
/// testes. Nunca entra em Faulted; usado em todos os testes headless.
/// </summary>
public sealed class NullDriver : PlcDriverBase
{
    private readonly bool[] _coils;
    private readonly ushort[] _holdings;

    public NullDriver(int outputCount = 256, int outputWordCount = 0)
    {
        if (outputCount < 0)
            throw new ArgumentOutOfRangeException(nameof(outputCount));
        if (outputWordCount < 0)
            throw new ArgumentOutOfRangeException(nameof(outputWordCount));
        _coils = new bool[outputCount];
        _holdings = new ushort[outputWordCount];
    }

    /// <summary>Último snapshot de sensores publicado (inspeção em testes).</summary>
    public IoSnapshot LastInputs { get; private set; } = IoSnapshot.Empty(0);

    public override void Start(ConnectionConfig config) => SetState(DriverState.Ready);

    public override void Stop() => SetState(DriverState.Stopped);

    /// <summary>Força uma coil local (equivale ao master escrever o bit).</summary>
    public void SetCoil(int offset, bool value) => _coils[offset] = value;

    /// <summary>Força um holding register local (equivale ao master escrever o setpoint).</summary>
    public void SetHolding(int offset, ushort value) => _holdings[offset] = value;

    public override IoSnapshot Exchange(IoSnapshot inputs)
    {
        LastInputs = inputs;
        return new IoSnapshot(inputs.TickNumber, _coils, _holdings);
    }
}
