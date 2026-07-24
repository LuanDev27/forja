using System.Text.Json;
using Forja.Anvil;
using Forja.Anvil.Catalog;
using Forja.Anvil.Scene;
using Forja.Core.Devices;
using Forja.Core.Io;
using Forja.Core.Physics;
using Forja.Core.State;
using Xunit;

namespace Forja.Core.Tests;

/// <summary>
/// T038 (spec 003): a balança. Existe para provar que o canal de palavras é
/// GENÉRICO — o mesmo `SetInputWord` que serve ao sensor de nível serve a uma
/// grandeza somada de outra fonte, sem tocar em nada da camada 1 ou 3
/// (research R8). Física falsa (Artigo V.1).
/// </summary>
public class WeighScaleTests
{
    private const uint DeviceId = 1;

    // Cartão 0..65535 sobre 0..50 kg: uma peça S de plástico (0,5 kg) vale 655.
    private static DeviceTypeDef TypeDef() => new()
    {
        TypeId = "sensor.scale",
        Category = DeviceCategory.Sensor,
        DisplayName = "Balança",
        Behavior = "weigh-scale",
        Ports = new() { new PortDef("weight", IoDirection.In, PortType.Word) },
        ParamDefs = new()
        {
            new ParamDef { Name = "sizeX", Type = "float", Default = Num(0.6) },
            new ParamDef { Name = "sizeZ", Type = "float", Default = Num(0.6) },
            new ParamDef { Name = "height", Type = "float", Default = Num(0.3) },
            new ParamDef { Name = "euMin", Type = "float", Default = Num(0) },
            new ParamDef { Name = "euMax", Type = "float", Default = Num(50) },
        },
    };

    private static JsonElement Num(double v) => JsonSerializer.SerializeToElement(v);

    private sealed class Rig
    {
        public required FakePhysicsWorld Physics { get; init; }
        public required PartsManager Parts { get; init; }
        public required IoTable Io { get; init; }
        public required WeighScale Device { get; init; }
        public required SimContext Ctx { get; init; }

        public ushort Raw()
        {
            Device.Tick(Ctx);
            return Io.BuildInputSnapshot(1).Words.Span[0];
        }

        /// <summary>Põe uma peça na plataforma e devolve o id dela.</summary>
        public uint Apoiar(string size, string material)
        {
            var part = Parts.SpawnBox(new PartKind(size, material), new Pose(Vec3.Zero, 0));
            Physics.QueryResult.Add(part.Id);
            return part.Id;
        }
    }

    private static Rig Build()
    {
        var catalog = DeviceCatalog.FromDefs(new[] { TypeDef() }).Require();
        var instance = new DeviceInstance { Id = DeviceId, TypeId = "sensor.scale" };
        var doc = new SceneDocument
        {
            SchemaVersion = SceneDocument.CurrentSchemaVersion,
            Name = "balança",
            Devices = new() { instance },
            IoMap = new()
            {
                new IoTag(DeviceId, "weight", new IoAddress(IoArea.InputRegister, 0), new AnalogScale()),
            },
        };

        var physics = new FakePhysicsWorld();
        var device = (WeighScale)DeviceFactory.CreateDefault().Create(instance, TypeDef());
        var ctx = new SimContext
        {
            Io = new IoTable(doc, catalog),
            Physics = physics,
            Parts = new PartsManager(physics),
            Random = new SeededRandom(1),
        };
        device.Build(ctx);
        return new Rig
        {
            Physics = physics, Parts = ctx.Parts, Io = ctx.Io, Device = device, Ctx = ctx,
        };
    }

    /// <summary>Bruto esperado para um peso em kg, no cartão 0..65535 / 0..50 kg.</summary>
    private static ushort Esperado(float kg) => (ushort)MathF.Round(kg / 50f * 65535f);

    [Fact]
    public void PlataformaVazia_PesaZero()
    {
        var rig = Build();

        Assert.Equal(0, rig.Raw());
        Assert.Equal(0f, rig.Device.Weight);
    }

    [Theory]
    [InlineData("S", "plastic", 0.5f)]
    [InlineData("M", "plastic", 1.0f)]
    [InlineData("L", "plastic", 1.8f)]
    [InlineData("S", "metal", 1.5f)]
    [InlineData("L", "metal", 5.4f)]
    public void UmaPeca_PesaAMassaDela(string size, string material, float kg)
    {
        var rig = Build();
        rig.Apoiar(size, material);

        Assert.Equal(Esperado(kg), rig.Raw());
        Assert.Equal(kg, rig.Device.Weight, 3);
    }

    [Fact]
    public void VariasPecas_PesamASoma()
    {
        var rig = Build();
        rig.Apoiar("S", "plastic"); // 0,5
        rig.Apoiar("M", "metal");   // 3,0
        rig.Apoiar("L", "plastic"); // 1,8

        Assert.Equal(Esperado(5.3f), rig.Raw());
        Assert.Equal(5.3f, rig.Device.Weight, 3);
    }

    [Fact]
    public void OrdemDaVarreduraDaFisica_NaoMudaOPeso()
    {
        // A QueryBox dos testes devolve a lista INVERTIDA de propósito: soma de
        // float não é associativa, e quem somar na ordem que a física entregou
        // quebra o Artigo I.3 sem nenhum sintoma visível.
        var direta = Build();
        direta.Apoiar("S", "plastic");
        direta.Apoiar("M", "metal");
        direta.Apoiar("L", "plastic");

        var invertida = Build();
        invertida.Apoiar("L", "plastic");
        invertida.Apoiar("M", "metal");
        invertida.Apoiar("S", "plastic");

        Assert.Equal(direta.Raw(), invertida.Raw());
    }

    [Fact]
    public void PesoAcimaDaFaixaDoCartao_Satura()
    {
        var rig = Build();
        for (int i = 0; i < 40; i++)
            rig.Apoiar("L", "metal"); // 40 × 5,4 = 216 kg num cartão de 50

        Assert.Equal(65535, rig.Raw());
    }

    [Fact]
    public void EcoQueNaoEPeca_NaoPesa()
    {
        var rig = Build();
        rig.Physics.QueryResult.Add(999u); // id que não é peça

        Assert.Equal(0, rig.Raw());
    }

    [Fact]
    public void PesoEntraNoHash()
    {
        var leve = Build();
        leve.Apoiar("S", "plastic");
        leve.Device.Tick(leve.Ctx);

        var pesada = Build();
        pesada.Apoiar("L", "metal");
        pesada.Device.Tick(pesada.Ctx);

        var a = StateHasher.Create();
        leve.Device.WriteState(ref a);
        var b = StateHasher.Create();
        pesada.Device.WriteState(ref b);

        Assert.NotEqual(a.Hash, b.Hash);
    }

    [Fact]
    public void CatalogoDaBalanca_CarregaEUsaOMesmoCanalDePalavras()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !Directory.Exists(Path.Combine(dir.FullName, "catalog", "devices")))
            dir = dir.Parent;
        Assert.True(dir is not null, "raiz do repositório não encontrada.");

        var catalog = DeviceCatalog
            .LoadFromDirectory(Path.Combine(dir!.FullName, "catalog", "devices")).Require();
        var def = catalog.Get("sensor.scale");

        var port = Assert.Single(def.Ports);
        Assert.Equal("weight", port.PortName);
        Assert.Equal(PortType.Word, port.Type);
        Assert.Equal(IoDirection.In, port.Direction);
    }
}
