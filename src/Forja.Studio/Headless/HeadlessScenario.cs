using System;
using Forja.Anvil.Contracts;
using Forja.Anvil.Scene;
using Forja.Core.Loop;
using Forja.Core.Persistence;
using Godot;

namespace Forja.Studio.Headless;

/// <summary>
/// Cenário do runner headless custom (substitui GdUnit4 — research R4):
/// roda dentro do godot --headless com a física Jolt REAL, um Tick() por
/// physics frame. Begin() é chamado com o loop em Edit; Tick() a cada frame
/// após o SimulationLoop.Tick(); Pass()/Fail() encerram.
/// </summary>
public abstract class HeadlessScenario
{
    protected Main Main { get; private set; } = null!;

    protected SimulationLoop Loop => Main.Loop;

    public string Name => GetType().Name;

    public bool Finished { get; private set; }

    public string? Failure { get; private set; }

    internal void Bind(Main main) => Main = main;

    public abstract void Begin();

    public abstract void Tick();

    protected void Pass() => Finished = true;

    protected void Fail(string why)
    {
        Failure = why;
        Finished = true;
    }

    /// <summary>Carrega a cena demo validando carga por arquivo (T025).</summary>
    protected static SceneDocument LoadDemoScene()
    {
        string path = ProjectSettings.GlobalizePath("res://demo/esteira-minima.forja");
        return SceneSerializer.LoadFile(path).Require();
    }

    protected void StartRun(SceneDocument doc)
    {
        if (!Loop.ReplaceDocument(doc))
            throw new InvalidOperationException($"{Name}: loop não estava em Edit no Begin().");
        Loop.Enqueue(new SetModeCommand(SimMode.Run));
    }
}
