using System.Diagnostics.CodeAnalysis;
using System.Net;
using System.Net.Sockets;
using Forja.Anvil.Contracts;
using Forja.Anvil.Scene;
using NModbus;

namespace Forja.Bellows.Modbus;

/// <summary>
/// Servidor Modbus TCP (chave "modbus-tcp-server") — o PLC é o master
/// (research R3, demo OpenPLC). Sensores → discrete inputs locais (FC02);
/// coils locais ← escritas do master (FC05/FC15) → atuadores.
///
/// Estados: Starting = escutando, aguardando master; Ready = master ativo;
/// Faulted = master calado por mais que TimeoutMs (C1–C3). A thread de rede
/// do NModbus fica desacoplada do tick via <see cref="MirrorDataStore"/>.
/// </summary>
public sealed class ModbusTcpServerDriver : PlcDriverBase
{
    private readonly object _gate = new();
    private readonly MirrorDataStore _store = new();
    private readonly bool[] _outputs;

    private TcpListener? _listener;
    private IModbusSlaveNetwork? _network;
    private CancellationTokenSource? _cts;
    private int _timeoutMs = 1000;

    /// <param name="outputCount">Bits de saída (coils) que o core consome —
    /// tipicamente IoTable.OutputCount.</param>
    public ModbusTcpServerDriver(int outputCount = 256)
    {
        if (outputCount < 0)
            throw new ArgumentOutOfRangeException(nameof(outputCount));
        _outputs = new bool[outputCount];
    }

    [SuppressMessage("Design", "CA1031:Do not catch general exception types",
        Justification = "Fronteira de rede: qualquer falha vira Faulted com motivo (regra C1, Artigo VII).")]
    public override void Start(ConnectionConfig config)
    {
        lock (_gate)
        {
            if (State is DriverState.Starting or DriverState.Ready)
                return;

            _timeoutMs = config.TimeoutMs;
            try
            {
                var factory = new ModbusFactory();
                _listener = new TcpListener(IPAddress.Parse(config.BindAddress), config.Port);
                _network = factory.CreateSlaveNetwork(_listener);
                _network.AddSlave(factory.CreateSlave(config.UnitId, _store));
                _listener.Start();
                _cts = new CancellationTokenSource();

                var token = _cts.Token;
                var network = _network;
                _ = Task.Run(async () =>
                {
                    try
                    {
                        await network.ListenAsync(token).ConfigureAwait(false);
                    }
                    catch (OperationCanceledException)
                    {
                        // Stop() — encerramento normal.
                    }
                    catch (Exception ex) when (!token.IsCancellationRequested)
                    {
                        Fault($"servidor Modbus caiu: {ex.Message}");
                    }
                });

                SetState(DriverState.Starting, $"escutando em {config.BindAddress}:{config.Port}, aguardando master");
            }
            catch (Exception ex)
            {
                CleanupNetwork();
                Fault($"não foi possível escutar em {config.BindAddress}:{config.Port} — {ex.Message}");
            }
        }
    }

    public override void Stop()
    {
        lock (_gate)
        {
            CleanupNetwork();
            SetState(DriverState.Stopped);
        }
    }

    [SuppressMessage("Design", "CA1031:Do not catch general exception types",
        Justification = "Encerramento melhor-esforço de sockets já derrubados; o estado final é Stopped (C4).")]
    private void CleanupNetwork()
    {
        _cts?.Cancel();
        _cts?.Dispose();
        _cts = null;

        try
        {
            _network?.Dispose();
        }
        catch (Exception)
        {
            // Dispose de socket já fechado pelo peer — nada a preservar aqui.
        }

        try
        {
            _listener?.Stop();
        }
        catch (Exception)
        {
            // Idem: encerramento é melhor-esforço, o estado final é Stopped.
        }

        _network = null;
        _listener = null;
    }

    public override IoSnapshot Exchange(IoSnapshot inputs)
    {
        var state = State;
        if (state is DriverState.Stopped)
            return new IoSnapshot(inputs.TickNumber, _outputs, Valid: false);

        // Publica sensores mesmo antes do master chegar: o primeiro FC02 já
        // deve enxergar o estado corrente (< 20 ms — RNF-02).
        _store.PublishInputs(inputs.Bits);

        long now = Environment.TickCount64;
        long idle = now - _store.LastMasterActivity;

        switch (state)
        {
            case DriverState.Starting when _store.MasterSeen:
                SetState(DriverState.Ready, "master conectado");
                break;

            case DriverState.Ready when idle > _timeoutMs:
                Fault($"master sem atividade há {idle} ms (timeout {_timeoutMs} ms)");
                return new IoSnapshot(inputs.TickNumber, _outputs, Valid: false);

            case DriverState.Faulted when _store.MasterSeen && idle <= _timeoutMs:
                // Master voltou a falar — recupera sem exigir Edit→Run.
                SetState(DriverState.Ready, "master reconectado");
                break;

            case DriverState.Faulted:
                return new IoSnapshot(inputs.TickNumber, _outputs, Valid: false);
        }

        _store.CopyCoils(_outputs);
        return new IoSnapshot(inputs.TickNumber, _outputs);
    }
}
