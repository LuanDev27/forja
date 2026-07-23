using System.Text.Json;
using Forja.Anvil.Catalog;
using Forja.Anvil.Contracts;
using Forja.Anvil.Scene;
using Forja.Core.Io;
using Forja.Core.State;
using Xunit;

namespace Forja.Core.Tests;

/// <summary>
/// Gate da camada 1 (Fase 2, spec 003 T017/T019): a conversão EU↔bruto na
/// fronteira IoTable e a entrada das palavras no hash de determinismo. Sem GPU,
/// sem PLC (Artigo V).
/// </summary>
public class AnalogIoTests
{
    // Cartão 0..65535 sobre engenharia 0..100 (ex.: sensor de nível 0–100 cm).
    private static DeviceCatalog AnalogCatalog() => DeviceCatalog.FromDefs(new[]
    {
        new DeviceTypeDef
        {
            TypeId = "sensor.level",
            Category = DeviceCategory.Sensor,
            DisplayName = "Sensor de nível",
            Behavior = "noop",
            Ports = new() { new PortDef("level", IoDirection.In, PortType.Word) },
            ParamDefs = new()
            {
                new ParamDef { Name = "euMin", Type = "float", Default = Num(0) },
                new ParamDef { Name = "euMax", Type = "float", Default = Num(100) },
            },
        },
        new DeviceTypeDef
        {
            TypeId = "actuator.vspeed",
            Category = DeviceCategory.Actuator,
            DisplayName = "Esteira de velocidade variável",
            Behavior = "noop",
            Ports = new() { new PortDef("speed", IoDirection.Out, PortType.Word) },
            ParamDefs = new()
            {
                new ParamDef { Name = "euMin", Type = "float", Default = Num(0) },
                new ParamDef { Name = "euMax", Type = "float", Default = Num(2) },
            },
        },
    }).Require();

    private static JsonElement Num(double v) =>
        JsonSerializer.SerializeToElement(v);

    private static SceneDocument LevelDoc(AnalogScale? scale = null) => new()
    {
        SchemaVersion = SceneDocument.CurrentSchemaVersion,
        Name = "nível",
        Devices = new()
        {
            new DeviceInstance { Id = 1, TypeId = "sensor.level" },
            new DeviceInstance { Id = 2, TypeId = "actuator.vspeed" },
        },
        IoMap = new()
        {
            new IoTag(1, "level", new IoAddress(IoArea.InputRegister, 0), scale ?? new AnalogScale()),
            new IoTag(2, "speed", new IoAddress(IoArea.HoldingRegister, 0), scale ?? new AnalogScale()),
        },
    };

    private static IoTable Table(AnalogScale? scale = null) => new(LevelDoc(scale), AnalogCatalog());

    // ---- T017: escala EU → bruto (fundo, meio, topo) ----

    [Theory]
    [InlineData(0f, 0)]        // fundo de escala
    [InlineData(50f, 32768)]   // meio — round ToEven de 32767.5
    [InlineData(100f, 65535)]  // topo de escala
    [InlineData(25f, 16384)]   // um quarto
    public void SensorEscreveEu_RegistradorCarregaBruto(float eu, int rawEsperado)
    {
        var io = Table();
        io.SetInputWord(1, "level", eu);

        var snapshot = io.BuildInputSnapshot(1);

        Assert.Equal((ushort)rawEsperado, snapshot.Words.Span[0]);
    }

    [Theory]
    [InlineData(-10f, 0)]      // abaixo do fundo satura em rawMin
    [InlineData(150f, 65535)]  // acima do topo satura em rawMax
    public void EuForaDaFaixa_Satura_NaoEstoura(float eu, int rawEsperado)
    {
        var io = Table();
        io.SetInputWord(1, "level", eu);

        Assert.Equal((ushort)rawEsperado, io.BuildInputSnapshot(1).Words.Span[0]);
    }

    [Fact]
    public void SaidaBruta_VoltaParaEu_NoAtuador()
    {
        var io = Table();
        // Master escreveu ~meio da escala bruta no holding register.
        io.ApplyOutputSnapshot(new IoSnapshot(1, ReadOnlyMemory<bool>.Empty, new ushort[] { 32768 }));

        float eu = io.GetOutputWord(2, "speed"); // faixa 0–2 m/s
        Assert.InRange(eu, 0.99f, 1.01f);
    }

    [Fact]
    public void DoisCartoesDiferentes_MesmaGrandeza_BrutosDiferentes()
    {
        // AS4/US1: o escalonamento é por instância (cartão da cena), não do tipo.
        var cheio = Table(new AnalogScale(0, 65535));
        var metade = Table(new AnalogScale(0, 32767));

        cheio.SetInputWord(1, "level", 50f);
        metade.SetInputWord(1, "level", 50f);

        Assert.NotEqual(
            cheio.BuildInputSnapshot(1).Words.Span[0],
            metade.BuildInputSnapshot(1).Words.Span[0]);
    }

    // ---- T019: determinismo — palavras entram no hash ----

    private static ulong Hash(IoTable io)
    {
        var hasher = StateHasher.Create();
        io.WriteState(ref hasher);
        return hasher.Hash;
    }

    [Fact]
    public void MesmoValorAnalogico_MesmoHash()
    {
        var a = Table();
        var b = Table();
        a.SetInputWord(1, "level", 42f);
        b.SetInputWord(1, "level", 42f);

        Assert.Equal(Hash(a), Hash(b));
    }

    [Fact]
    public void ValorAnalogicoDiferente_HashDiferente()
    {
        // Sem o hash das palavras (Artigo I.4), duas execuções com sensores
        // analógicos divergentes teriam o MESMO hash — o modo de falha
        // silenciosa que o Artigo V existe para pegar.
        var a = Table();
        var b = Table();
        a.SetInputWord(1, "level", 40f);
        b.SetInputWord(1, "level", 60f);

        Assert.NotEqual(Hash(a), Hash(b));
    }

    [Fact]
    public void HashDeterministicoEntreExecucoes_NoHotPath()
    {
        // Repete a mesma sequência de escritas N vezes: hash idêntico sempre.
        ulong? primeiro = null;
        for (int rep = 0; rep < 5; rep++)
        {
            var io = Table();
            for (int t = 0; t < 100; t++)
                io.SetInputWord(1, "level", t % 101);
            ulong h = Hash(io);
            primeiro ??= h;
            Assert.Equal(primeiro, h);
        }
    }
}
