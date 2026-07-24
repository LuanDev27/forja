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

    // ---- Spec 003 / T040: a unidade de engenharia mora na camada 2 ----
    //
    // ADR 0005: a conversão EU↔bruto acontece EXATAMENTE na fronteira IoTable.
    // Acima dela o mundo é físico (float, cm, m/s); abaixo dela o mundo é de
    // palavras (ushort). Se um float atravessar o contrato de I/O ou descer até
    // o driver, a fronteira deixou de existir e a conversão vira coisa espalhada
    // — o modo de falha que estes dois testes existem para pegar.

    /// <summary>Tipos que formam o contrato de I/O da camada 1.</summary>
    private static readonly Type[] IoContract =
    {
        typeof(Anvil.Contracts.IoSnapshot),
        typeof(Anvil.Scene.IoTag),
        typeof(Anvil.Scene.IoAddress),
        typeof(Anvil.Scene.AnalogScale),
        typeof(Anvil.Catalog.PortDef),
    };

    [Fact]
    public void ContratoDeIoDaCamada1_NaoCarregaPontoFlutuante()
    {
        var offenders = IoContract.SelectMany(FloatingMembers).ToList();

        Assert.True(
            offenders.Count == 0,
            "EU vazou para o contrato de I/O (camada 1): " + string.Join(", ", offenders));
    }

    [Fact]
    public void Bellows_NaoConhecePontoFlutuante()
    {
        // A camada 3 fala bit e palavra. Nenhum tipo dela deve sequer mencionar
        // float/double: o driver não tem por que saber o que é um centímetro.
        var offenders = Bellows.GetTypes().SelectMany(FloatingMembers).ToList();

        Assert.True(
            offenders.Count == 0,
            "EU vazou para o driver (camada 3): " + string.Join(", ", offenders));
    }

    [Fact]
    public void DetectorDeFloat_NaoEhVazio()
    {
        // Guarda contra teste que passa por acidente: Vec3 é o tipo de mundo
        // físico por excelência e TEM de ser flagrado pelo mesmo detector.
        Assert.NotEmpty(FloatingMembers(typeof(Anvil.Vec3)));
        Assert.True(IsFloating(typeof(ReadOnlyMemory<float>)));
        Assert.False(IsFloating(typeof(ReadOnlyMemory<ushort>)));
    }

    /// <summary>Membros declarados cujo tipo carrega ponto flutuante.</summary>
    private static IEnumerable<string> FloatingMembers(Type type)
    {
        const BindingFlags Declared =
            BindingFlags.Public | BindingFlags.NonPublic |
            BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly;

        foreach (var field in type.GetFields(Declared))
            if (IsFloating(field.FieldType))
                yield return $"{type.Name}.{field.Name}";

        foreach (var property in type.GetProperties(Declared))
            if (IsFloating(property.PropertyType))
                yield return $"{type.Name}.{property.Name}";

        foreach (var method in type.GetMethods(Declared))
        {
            if (IsFloating(method.ReturnType))
                yield return $"{type.Name}.{method.Name}() → {method.ReturnType.Name}";

            foreach (var parameter in method.GetParameters())
                if (IsFloating(parameter.ParameterType))
                    yield return $"{type.Name}.{method.Name}({parameter.Name})";
        }
    }

    /// <summary>float/double/decimal, inclusive embrulhados (nullable, array,
    /// ref, genérico) — <c>ReadOnlyMemory&lt;float&gt;</c> também conta.</summary>
    private static bool IsFloating(Type type)
    {
        if (type.IsByRef || type.IsPointer || type.IsArray)
            return IsFloating(type.GetElementType()!);

        if (type == typeof(float) || type == typeof(double) || type == typeof(decimal))
            return true;

        return type.IsGenericType && type.GetGenericArguments().Any(IsFloating);
    }
}
