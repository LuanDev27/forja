using Forja.Anvil.Contracts;
using Forja.Anvil.Scene;

namespace Forja.Bellows;

/// <summary>
/// Infraestrutura comum dos drivers: máquina de estados thread-safe com
/// motivo obrigatório em Faulted (Artigo VII.3) e Dispose ⇒ Stop (regra C4).
/// </summary>
public abstract class PlcDriverBase : IPlcDriver
{
    private readonly object _stateGate = new();
    private DriverState _state = DriverState.Stopped;

    public DriverState State
    {
        get { lock (_stateGate) return _state; }
    }

    public event Action<DriverState, string?>? StateChanged;

    public abstract void Start(ConnectionConfig config);

    public abstract void Stop();

    public abstract IoSnapshot Exchange(IoSnapshot inputs);

    /// <summary>Muda o estado e notifica. Sem efeito se já estiver nele.</summary>
    protected void SetState(DriverState state, string? reason = null)
    {
        lock (_stateGate)
        {
            if (_state == state)
                return;
            if (state == DriverState.Faulted && string.IsNullOrWhiteSpace(reason))
                reason = "falha no driver (sem motivo informado)";
            _state = state;
        }

        StateChanged?.Invoke(state, reason);
    }

    protected void Fault(string reason) => SetState(DriverState.Faulted, reason);

    public void Dispose()
    {
        Stop();
        GC.SuppressFinalize(this);
    }
}
