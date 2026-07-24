using Forja.Anvil;
using Forja.Anvil.Catalog;
using Forja.Anvil.Contracts;
using Forja.Anvil.Scene;
using Forja.Anvil.Validation;
using Forja.Core.Devices;
using Forja.Core.Loop;
using Forja.Core.Persistence;
using Forja.Core.Physics;
using Xunit;

namespace Forja.Core.Tests;

/// <summary>
/// US3 (spec 003): a malha de nível ponta a ponta — sensor em %IW0, decisão
/// sobre número, atuador em %QW0. O cenário é o 07 da biblioteca
/// (<c>plc/07-controle-de-nivel/</c>): estes testes carregam a MESMA cena que o
/// OpenPLC vai comandar e rodam contra um CLP de mentira que espelha, linha por
/// linha, a lógica do <c>controle-nivel.st</c>. Sem GPU e sem PLC (Artigo V).
/// </summary>
public class LevelControlLoopTests
{
    // Constantes do controle-nivel.st. Se elas mudarem lá, mudam aqui — é de
    // propósito: o teste é o espelho do programa, não uma segunda invenção.
    private const int SpNivel = 60;
    private const int Banda = 5;
    private const ushort VelLenta = 16384;
    private const ushort VelRapida = 49151;

    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !Directory.Exists(Path.Combine(dir.FullName, "catalog", "devices")))
            dir = dir.Parent;
        Assert.True(dir is not null, "raiz do repositório não encontrada acima do bin de teste.");
        return dir!.FullName;
    }

    private static string ScenePath() => Path.Combine(
        RepoRoot(), "plc", "07-controle-de-nivel", "controle-nivel.forja");

    private static DeviceCatalog Catalog() =>
        DeviceCatalog.LoadFromDirectory(Path.Combine(RepoRoot(), "catalog", "devices")).Require();

    /// <summary>CLP de mentira: espelha o controle-nivel.st (reescala, banda
    /// morta com memória, duas velocidades).</summary>
    private sealed class PlcDeNivel : IPlcDriver
    {
        private bool _drenando;

        public DriverState State { get; private set; } = DriverState.Stopped;

        public event Action<DriverState, string?>? StateChanged;

        /// <summary>Último bruto lido do %IW0 — o que o CLP enxergou.</summary>
        public ushort NivelLido { get; private set; }

        /// <summary>Último bruto escrito no %QW0.</summary>
        public ushort ComandoEscrito { get; private set; } = VelLenta;

        public void Start(ConnectionConfig config)
        {
            State = DriverState.Ready;
            StateChanged?.Invoke(State, null);
        }

        public void Stop() => State = DriverState.Stopped;

        public IoSnapshot Exchange(IoSnapshot inputs)
        {
            NivelLido = inputs.Words.Length > 0 ? inputs.Words.Span[0] : (ushort)0;

            int nivelPct = NivelLido * 100 / 65535;          // reescala (seção 1 do .st)
            if (nivelPct >= SpNivel + Banda) _drenando = true;
            else if (nivelPct <= SpNivel - Banda) _drenando = false;

            ComandoEscrito = _drenando ? VelRapida : VelLenta;
            return new IoSnapshot(
                inputs.TickNumber, ReadOnlyMemory<bool>.Empty, new[] { ComandoEscrito });
        }

        public void Dispose() => Stop();
    }

    private sealed class Malha : IDisposable
    {
        public required SimulationLoop Loop { get; init; }
        public required FakePhysicsWorld Physics { get; init; }
        public required PlcDeNivel Plc { get; init; }
        public required uint PartId { get; init; }

        /// <summary>Velocidade em m/s que a esteira aplicou de fato.</summary>
        public float VelocidadeDaEsteira =>
            Loop.Devices.OfType<VariableSpeedConveyor>().Single().Setpoint;

        /// <summary>Põe a superfície do material numa altura que vale
        /// <paramref name="pct"/>% do alcance do sensor (sensor em Y=1, alcance 1).</summary>
        public void Nivel(int pct) =>
            Physics.RayResult = new RayHit(PartId, new Vec3(0, pct / 100f, 0));

        public void Ticks(int n)
        {
            for (int i = 0; i < n; i++)
                Loop.Tick();
        }

        public void Dispose() => Loop.Dispose();
    }

    private static Malha Subir()
    {
        var doc = SceneSerializer.LoadFile(ScenePath()).Require();
        var physics = new FakePhysicsWorld();
        var plc = new PlcDeNivel();
        var loop = new SimulationLoop(doc, Catalog(), DeviceFactory.CreateDefault(), physics, _ => plc);

        loop.Enqueue(new SetModeCommand(SimMode.Run));
        loop.Tick();
        Assert.Equal(SimMode.Run, loop.Mode);

        // Uma peça serve de "superfície do material": mover o eco do raycast é
        // como encher e esvaziar o pulmão, sem depender da física de verdade.
        var part = loop.Parts!.SpawnBox(new PartKind("S", "plastic"), new Pose(new Vec3(0, 0, 0), 0));
        return new Malha { Loop = loop, Physics = physics, Plc = plc, PartId = part.Id };
    }

    // ---- T031: a cena da biblioteca é válida e usa os endereços do .st ----

    [Fact]
    public void CenaDoCenario07_CarregaEValida()
    {
        var doc = SceneSerializer.LoadFile(ScenePath()).Require();

        Assert.Empty(IoMapValidator.Validate(doc, Catalog()));
    }

    [Theory]
    [InlineData("sensor.level", "level", IoArea.InputRegister, (ushort)0)]
    [InlineData("conveyor.belt.vspeed", "speed", IoArea.HoldingRegister, (ushort)0)]
    public void EnderecosBatemComOProgramaSt(string typeId, string port, IoArea area, ushort offset)
    {
        var doc = SceneSerializer.LoadFile(ScenePath()).Require();
        var device = Assert.Single(doc.Devices, d => d.TypeId == typeId);
        var tag = Assert.Single(doc.IoMap, t => t.DeviceId == device.Id && t.PortName == port);

        Assert.Equal(new IoAddress(area, offset), tag.Address);
    }

    // ---- T033: a malha corrige na direção certa (AS1) ----

    [Fact]
    public void NivelAcimaDoSetpoint_EsteiraDrenaRapido()
    {
        using var malha = Subir();

        malha.Nivel(90);
        malha.Ticks(5);

        Assert.Equal(VelRapida, malha.Plc.ComandoEscrito);
        Assert.Equal(1.5f, malha.VelocidadeDaEsteira, 2);
    }

    [Fact]
    public void NivelAbaixoDoSetpoint_EsteiraDrenaDevagar()
    {
        using var malha = Subir();

        malha.Nivel(90);
        malha.Ticks(5);
        malha.Nivel(20);
        malha.Ticks(5);

        Assert.Equal(VelLenta, malha.Plc.ComandoEscrito);
        Assert.Equal(0.5f, malha.VelocidadeDaEsteira, 2);
    }

    [Fact]
    public void ComandoDoPlcChegaNaEsteiraEmNoMaximoUmTick()
    {
        using var malha = Subir();
        malha.Nivel(90);
        malha.Ticks(5);
        Assert.Equal(1.5f, malha.VelocidadeDaEsteira, 2);

        malha.Nivel(20);
        malha.Ticks(2); // 1 tick para o CLP ver o novo nível, 1 para a esteira aplicar

        Assert.Equal(0.5f, malha.VelocidadeDaEsteira, 2);
    }

    // ---- T033: banda morta — não oscila por quantização (AS2) ----

    [Fact]
    public void DentroDaBandaMorta_SaidaNaoMuda()
    {
        using var malha = Subir();
        malha.Nivel(20);
        malha.Ticks(5);
        Assert.Equal(VelLenta, malha.Plc.ComandoEscrito);

        // Sobe até DENTRO da banda (55..65): ainda não é motivo para chavear.
        foreach (int pct in new[] { 56, 60, 62, 64, 58, 61 })
        {
            malha.Nivel(pct);
            malha.Ticks(3);
            Assert.Equal(VelLenta, malha.Plc.ComandoEscrito);
        }
    }

    [Fact]
    public void BandaMortaTemMemoria_CaminhoDeIdaDiferenteDoDeVolta()
    {
        using var malha = Subir();

        // Ida: só chaveia ao ultrapassar 65.
        malha.Nivel(64);
        malha.Ticks(3);
        Assert.Equal(VelLenta, malha.Plc.ComandoEscrito);
        malha.Nivel(70);
        malha.Ticks(3);
        Assert.Equal(VelRapida, malha.Plc.ComandoEscrito);

        // Volta: no MESMO 64 de antes a saída agora é a outra — a resposta
        // depende de onde o processo veio (é isso que a banda morta compra).
        malha.Nivel(64);
        malha.Ticks(3);
        Assert.Equal(VelRapida, malha.Plc.ComandoEscrito);

        malha.Nivel(50);
        malha.Ticks(3);
        Assert.Equal(VelLenta, malha.Plc.ComandoEscrito);
    }

    [Fact]
    public void TremorDeUmaContagemNoSetpoint_NaoChaveiaNada()
    {
        using var malha = Subir();
        malha.Nivel(20);
        malha.Ticks(5);

        // Ruído de UMA contagem bruta em cima da fronteira do setpoint — o
        // tremor que todo sinal analógico real tem. 39321 é a menor contagem
        // que reescala para 60 % (65535 * 0,6); 39320 ainda cai em 59 %.
        const ushort Fronteira = 39321;
        int chaveamentosIngenuos = 0;
        bool ingenuo = false;
        var esteira = malha.Loop.Devices.OfType<VariableSpeedConveyor>().Single();

        for (int i = 0; i < 40; i++)
        {
            ushort raw = (ushort)(Fronteira - (i % 2));
            malha.Physics.RayResult = new RayHit(malha.PartId, new Vec3(0, raw / 65535f, 0));
            malha.Ticks(1);

            // O controlador SEM banda morta, alimentado pelo mesmo sinal.
            bool ingenuoAgora = raw * 100 / 65535 >= SpNivel;
            if (ingenuoAgora != ingenuo)
                chaveamentosIngenuos++;
            ingenuo = ingenuoAgora;

            Assert.Equal(VelLenta, malha.Plc.ComandoEscrito);
        }

        // Sem a guarda abaixo o teste passaria mesmo com um sinal parado —
        // é ela que prova que havia tremor de verdade para filtrar.
        Assert.True(
            chaveamentosIngenuos > 10,
            $"o sinal de teste não tremeu o bastante (chaveamentos ingênuos: {chaveamentosIngenuos}).");
        Assert.Equal(0.5f, esteira.Setpoint, 2);
    }

    // ---- T034: determinismo da malha completa (AS3 / Artigo I.4) ----

    [Fact]
    public void MesmaSequencia_MesmoHashNasDuasExecucoes()
    {
        Assert.Equal(RodarSequencia(), RodarSequencia());
    }

    private static ulong RodarSequencia()
    {
        using var malha = Subir();

        // Enche, cruza a banda, esvazia, volta — 4 travessias em 480 ticks.
        foreach (int pct in new[] { 10, 40, 58, 66, 80, 62, 54, 30 })
        {
            malha.Nivel(pct);
            malha.Ticks(60);
        }

        return malha.Loop.ComputeStateHash();
    }

    [Fact]
    public void HashDaMalha_MudaQuandoOPercursoMuda()
    {
        // Guarda contra hash constante: dois percursos diferentes não podem
        // colidir, senão o teste de determinismo acima não prova nada.
        using var a = Subir();
        a.Nivel(80);
        a.Ticks(60);

        using var b = Subir();
        b.Nivel(20);
        b.Ticks(60);

        Assert.NotEqual(a.Loop.ComputeStateHash(), b.Loop.ComputeStateHash());
    }
}
