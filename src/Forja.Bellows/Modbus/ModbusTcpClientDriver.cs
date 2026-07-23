using System.Diagnostics.CodeAnalysis;
using System.Net.Sockets;
using Forja.Anvil.Contracts;
using Forja.Anvil.Scene;
using NModbus;

namespace Forja.Bellows.Modbus;

/// <summary>
/// Cliente Modbus TCP (chave "modbus-tcp-client") — a Forja é o master.
/// Sensores → FC15 (WriteMultipleCoils) nas coils do PLC a partir de
/// ConnectionConfig.InputBaseOffset; atuadores ← FC01 (ReadCoils) em 0..N-1.
/// Com InputBaseOffset > N as duas janelas nunca colidem
/// (contracts/modbus-mapping.md).
///
/// Conexão em background com backoff (500 ms → 5 s); queda no Exchange ⇒
/// Faulted com motivo (C1/C2) e reconexão automática ⇒ Ready.
/// </summary>
public sealed class ModbusTcpClientDriver : PlcDriverBase
{
    private readonly object _gate = new();
    private readonly int _outputCount;
    private readonly int _outputWordCount;
    private readonly bool[] _lastOutputs;
    private readonly ushort[] _lastOutputWords;
    private bool[] _inputBuffer = Array.Empty<bool>();
    private ushort[] _inputWordBuffer = Array.Empty<ushort>();

    private ConnectionConfig _config = new();
    private CancellationTokenSource? _cts;
    private TcpClient? _client;
    private volatile IModbusMaster? _master;
    private int _reconnecting;

    /// <param name="outputCount">Quantas coils remotas ler por tick —
    /// tipicamente IoTable.OutputCount.</param>
    /// <param name="outputWordCount">Quantos holding registers remotos ler por
    /// tick (setpoints de atuadores) — tipicamente IoTable.OutputWordCount.</param>
    public ModbusTcpClientDriver(int outputCount = 256, int outputWordCount = 0)
    {
        if (outputCount is < 0 or > 2000)
            throw new ArgumentOutOfRangeException(
                nameof(outputCount), "FC01 lê no máximo 2000 coils por request.");
        if (outputWordCount is < 0 or > 125)
            throw new ArgumentOutOfRangeException(
                nameof(outputWordCount), "FC03 lê no máximo 125 registers por request.");
        _outputCount = outputCount;
        _outputWordCount = outputWordCount;
        _lastOutputs = new bool[outputCount];
        _lastOutputWords = new ushort[outputWordCount];
    }

    public override void Start(ConnectionConfig config)
    {
        lock (_gate)
        {
            if (State is DriverState.Starting or DriverState.Ready)
                return;

            _config = config;
            _cts = new CancellationTokenSource();
            SetState(DriverState.Starting, $"conectando em {config.Host}:{config.Port}");
            BeginConnect(_cts.Token);
        }
    }

    public override void Stop()
    {
        lock (_gate)
        {
            _cts?.Cancel();
            _cts?.Dispose();
            _cts = null;
            DisposeConnection();
            SetState(DriverState.Stopped);
        }
    }

    [SuppressMessage("Design", "CA1031:Do not catch general exception types",
        Justification = "Encerramento melhor-esforço de socket já derrubado pelo peer (C4).")]
    private void DisposeConnection()
    {
        try
        {
            _master?.Dispose();
            _client?.Dispose();
        }
        catch (Exception)
        {
            // Socket já derrubado pelo peer — o objetivo (desconectar) foi atingido.
        }

        _master = null;
        _client = null;
    }

    /// <summary>Loop de (re)conexão em background com backoff exponencial.</summary>
    [SuppressMessage("Design", "CA1031:Do not catch general exception types",
        Justification = "PLC fora do ar não pode derrubar o loop de reconexão; a falha já foi sinalizada (C1).")]
    private void BeginConnect(CancellationToken token)
    {
        if (Interlocked.Exchange(ref _reconnecting, 1) == 1)
            return;

        _ = Task.Run(async () =>
        {
            int delayMs = 500;
            try
            {
                while (!token.IsCancellationRequested)
                {
                    try
                    {
                        var client = new TcpClient
                        {
                            ReceiveTimeout = _config.TimeoutMs,
                            SendTimeout = _config.TimeoutMs,
                        };
                        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(token);
                        timeout.CancelAfter(_config.TimeoutMs);
                        await client.ConnectAsync(_config.Host, _config.Port, timeout.Token)
                            .ConfigureAwait(false);

                        var master = new ModbusFactory().CreateMaster(client);
                        master.Transport.ReadTimeout = _config.TimeoutMs;
                        master.Transport.WriteTimeout = _config.TimeoutMs;

                        lock (_gate)
                        {
                            if (token.IsCancellationRequested)
                            {
                                master.Dispose();
                                client.Dispose();
                                return;
                            }

                            _client = client;
                            _master = master;
                        }

                        SetState(DriverState.Ready, $"conectado em {_config.Host}:{_config.Port}");
                        return;
                    }
                    catch (OperationCanceledException) when (token.IsCancellationRequested)
                    {
                        return;
                    }
                    catch (Exception)
                    {
                        // PLC fora do ar — backoff e tenta de novo (Starting/Faulted
                        // já sinalizado; o motivo detalhado veio da falha original).
                    }

                    await Task.Delay(delayMs, token).ConfigureAwait(false);
                    delayMs = Math.Min(delayMs * 2, 5000);
                }
            }
            catch (OperationCanceledException)
            {
                // Stop() durante o Delay — encerramento normal.
            }
            finally
            {
                Interlocked.Exchange(ref _reconnecting, 0);
            }
        }, token);
    }

    [SuppressMessage("Design", "CA1031:Do not catch general exception types",
        Justification = "Regra C1: Exchange nunca lança por falha de rede — sinaliza Faulted com motivo.")]
    public override IoSnapshot Exchange(IoSnapshot inputs)
    {
        var master = _master;
        if (State != DriverState.Ready || master is null)
            return new IoSnapshot(inputs.TickNumber, _lastOutputs, _lastOutputWords, Valid: false);

        try
        {
            if (inputs.Bits.Length > 0)
            {
                if (_inputBuffer.Length != inputs.Bits.Length)
                    _inputBuffer = new bool[inputs.Bits.Length];
                inputs.Bits.Span.CopyTo(_inputBuffer);
                master.WriteMultipleCoils(_config.UnitId, _config.InputBaseOffset, _inputBuffer);
            }

            // Palavras de sensores → holding registers remotos no base offset
            // (FC16). Input registers do slave são read-only para o master.
            if (inputs.Words.Length > 0)
            {
                if (_inputWordBuffer.Length != inputs.Words.Length)
                    _inputWordBuffer = new ushort[inputs.Words.Length];
                inputs.Words.Span.CopyTo(_inputWordBuffer);
                master.WriteMultipleRegisters(_config.UnitId, _config.InputBaseOffset, _inputWordBuffer);
            }

            if (_outputCount > 0)
            {
                var coils = master.ReadCoils(_config.UnitId, 0, (ushort)_outputCount);
                int n = Math.Min(coils.Length, _lastOutputs.Length);
                Array.Copy(coils, _lastOutputs, n);
            }

            // Setpoints de atuadores ← holding registers remotos 0..N (FC03).
            if (_outputWordCount > 0)
            {
                var regs = master.ReadHoldingRegisters(_config.UnitId, 0, (ushort)_outputWordCount);
                int n = Math.Min(regs.Length, _lastOutputWords.Length);
                Array.Copy(regs, _lastOutputWords, n);
            }

            return new IoSnapshot(inputs.TickNumber, _lastOutputs, _lastOutputWords);
        }
        catch (Exception ex)
        {
            // C1: nunca lançar por falha de rede — Faulted + snapshot inválido.
            DisposeConnection();
            Fault($"conexão com o PLC {_config.Host}:{_config.Port} perdida — {ex.Message}");
            var cts = _cts;
            if (cts is not null && !cts.IsCancellationRequested)
                BeginConnect(cts.Token);
            return new IoSnapshot(inputs.TickNumber, _lastOutputs, _lastOutputWords, Valid: false);
        }
    }
}
