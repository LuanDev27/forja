using System.Reflection;
using NetArchTest.Rules;
using Xunit;

namespace Forja.Architecture.Tests;

/// <summary>
/// T004 / Artigo II: violação de camada é build vermelho. Camadas:
/// 1 Anvil (domínio puro) → 2 Core (simulação) → 3 Bellows (IO) → 4 Studio.
/// </summary>
public class LayerRulesTests
{
    private static readonly Assembly Anvil = typeof(Anvil.Contracts.IPlcDriver).Assembly;
    private static readonly Assembly Core = typeof(Core.Loop.SimulationLoop).Assembly;
    private static readonly Assembly Bellows = typeof(Bellows.DriverRegistry).Assembly;

    private static void AssertClean(TestResult result)
    {
        Assert.True(
            result.IsSuccessful,
            "Violação de camada: " + string.Join(", ", result.FailingTypeNames ?? Array.Empty<string>()));
    }

    [Fact]
    public void Anvil_NaoDependeDeNada()
    {
        AssertClean(Types.InAssembly(Anvil)
            .ShouldNot()
            .HaveDependencyOnAny("Godot", "Forja.Core", "Forja.Bellows", "Forja.Studio", "NModbus")
            .GetResult());
    }

    [Fact]
    public void Core_NaoUsaUiNemInputDoGodot_NemCamadasAcima()
    {
        // GodotPhysicsWorld pode usar nós de física do Godot (Artigo II.3),
        // mas UI/entrada/render são exclusivos da camada 4.
        AssertClean(Types.InAssembly(Core)
            .ShouldNot()
            .HaveDependencyOnAny(
                "Godot.Control",
                "Godot.Input",
                "Godot.CanvasItem",
                "Godot.Window",
                "Forja.Bellows",
                "Forja.Studio",
                "NModbus")
            .GetResult());
    }

    [Fact]
    public void Bellows_NaoUsaGodotNemStudio()
    {
        AssertClean(Types.InAssembly(Bellows)
            .ShouldNot()
            .HaveDependencyOnAny("Godot", "Forja.Studio")
            .GetResult());
    }
}
