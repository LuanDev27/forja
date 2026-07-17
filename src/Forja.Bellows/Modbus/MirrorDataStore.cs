using NModbus;

namespace Forja.Bellows.Modbus;

/// <summary>
/// Data-store NModbus espelhando a IoTable (modo servidor). Handoff sem lock
/// no caminho quente: sensores entram por troca de referência imutável
/// (double-buffer) e coils usam escrita por bit (atômica em bool). A thread
/// de rede do NModbus nunca bloqueia o tick.
/// </summary>
internal sealed class MirrorDataStore : ISlaveDataStore
{
    private const int BitSpace = ushort.MaxValue + 1;

    private readonly InputSource _inputs;
    private readonly CoilSource _coils;
    private long _lastMasterActivity = long.MinValue;
    private int _masterSeen;

    public MirrorDataStore()
    {
        _inputs = new InputSource(Touch);
        _coils = new CoilSource(Touch);
    }

    public IPointSource<bool> CoilDiscretes => _coils;

    public IPointSource<bool> CoilInputs => _inputs;

    // v1 é digital-only (contracts/modbus-mapping.md); registradores existem
    // apenas para o master não receber exceção ao sondá-los.
    public IPointSource<ushort> HoldingRegisters { get; } = new RegisterSource();

    public IPointSource<ushort> InputRegisters { get; } = new RegisterSource();

    /// <summary>Algum request do master já chegou desde o Start?</summary>
    public bool MasterSeen => Volatile.Read(ref _masterSeen) != 0;

    /// <summary>Environment.TickCount64 do último request do master.</summary>
    public long LastMasterActivity => Interlocked.Read(ref _lastMasterActivity);

    private void Touch()
    {
        Interlocked.Exchange(ref _lastMasterActivity, Environment.TickCount64);
        Volatile.Write(ref _masterSeen, 1);
    }

    /// <summary>Publica o snapshot dos sensores (fim do tick, thread da sim).</summary>
    public void PublishInputs(ReadOnlyMemory<bool> bits) => _inputs.Publish(bits);

    /// <summary>Copia as coils escritas pelo master para o buffer do tick.</summary>
    public void CopyCoils(bool[] destination) => _coils.CopyTo(destination);

    /// <summary>Discrete inputs: master lê, simulação publica (double-buffer).</summary>
    private sealed class InputSource : IPointSource<bool>
    {
        private readonly Action _touch;
        private bool[] _front = Array.Empty<bool>();
        private bool[] _back = Array.Empty<bool>();

        public InputSource(Action touch) => _touch = touch;

        public void Publish(ReadOnlyMemory<bool> bits)
        {
            if (_back.Length != bits.Length)
                _back = new bool[bits.Length];
            bits.Span.CopyTo(_back);
            _back = Interlocked.Exchange(ref _front, _back);
        }

        public bool[] ReadPoints(ushort startAddress, ushort numberOfPoints)
        {
            _touch();
            var snapshot = Volatile.Read(ref _front);
            var result = new bool[numberOfPoints];
            for (int i = 0; i < numberOfPoints; i++)
            {
                int address = startAddress + i;
                result[i] = address < snapshot.Length && snapshot[address];
            }

            return result;
        }

        // Discrete input é somente-leitura para o master; o NModbus não gera
        // escrita aqui em operação normal — ignorar é seguro (falha explícita
        // não se aplica: não há estado a corromper).
        public void WritePoints(ushort startAddress, bool[] points) => _touch();
    }

    /// <summary>Coils: master escreve (FC05/FC15), simulação copia por tick.</summary>
    private sealed class CoilSource : IPointSource<bool>
    {
        private readonly Action _touch;
        private readonly bool[] _bits = new bool[BitSpace];

        public CoilSource(Action touch) => _touch = touch;

        public void CopyTo(bool[] destination)
        {
            int n = Math.Min(destination.Length, _bits.Length);
            Array.Copy(_bits, destination, n);
        }

        public bool[] ReadPoints(ushort startAddress, ushort numberOfPoints)
        {
            _touch();
            var result = new bool[numberOfPoints];
            for (int i = 0; i < numberOfPoints; i++)
            {
                int address = startAddress + i;
                if (address < _bits.Length)
                    result[i] = _bits[address];
            }

            return result;
        }

        public void WritePoints(ushort startAddress, bool[] points)
        {
            _touch();
            for (int i = 0; i < points.Length; i++)
            {
                int address = startAddress + i;
                if (address < _bits.Length)
                    _bits[address] = points[i];
            }
        }
    }

    /// <summary>Registradores tolerados mas fora do escopo v1 (digital-only).</summary>
    private sealed class RegisterSource : IPointSource<ushort>
    {
        private readonly ushort[] _values = new ushort[BitSpace];

        public ushort[] ReadPoints(ushort startAddress, ushort numberOfPoints)
        {
            var result = new ushort[numberOfPoints];
            for (int i = 0; i < numberOfPoints; i++)
            {
                int address = startAddress + i;
                if (address < _values.Length)
                    result[i] = _values[address];
            }

            return result;
        }

        public void WritePoints(ushort startAddress, ushort[] points)
        {
            for (int i = 0; i < points.Length; i++)
            {
                int address = startAddress + i;
                if (address < _values.Length)
                    _values[address] = points[i];
            }
        }
    }
}
