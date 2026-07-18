using System.Collections.Generic;
using Forja.Anvil.Contracts;
using Godot;

namespace Forja.Studio.Headless;

/// <summary>
/// Runner headless (T016, decisão da sessão 1: runner próprio em vez de
/// GdUnit4). Executa os cenários em sequência no SceneTree real e sai com
/// exit code 0/1 — pluga direto no CI. TimeScale acelera o relógio; o
/// timestep físico continua fixo em 1/60 (Artigo I.1 preservado).
/// </summary>
public partial class HeadlessHost : Node
{
    private readonly Main _main;
    private readonly Queue<HeadlessScenario> _pending;
    private HeadlessScenario? _current;
    private int _cooldownFrames;
    private int _failures;

    public HeadlessHost(Main main)
    {
        _main = main;
        _pending = new Queue<HeadlessScenario>(new HeadlessScenario[]
        {
            new SmokeBootScenario(),
            new ConveyorFlowScenario(),
            new SimModeE2EScenario(),
            new SensorActuatorScenario(),
            new HmiScenario(),
            new ConveyorIoScenario(),
            new SensorsScenario(),
            new ActuatorsScenario(),
            new PassivesScenario(),
            new SeparadorDemoScenario(),
            new FailSafeScenario(),
            new DeterminismScenario(),
            new PerfScenario(),
        });
    }

    public override void _Ready()
    {
        // Acelera o relógio SEM tocar no timestep: o Godot passa
        // (1/ticks) * time_scale ao servidor de física, então
        // 1200 ticks/s × 20 mantém dt = 1/60 exato (Artigo I.1) e roda
        // ~20x mais physics frames por segundo de parede.
        Engine.PhysicsTicksPerSecond = 1200;
        Engine.TimeScale = 20.0;
        Engine.MaxPhysicsStepsPerFrame = 4000;
        GD.Print("== Forja headless tests ==");
    }

    // Roda DEPOIS do Main._PhysicsProcess (pai processa antes do filho).
    public override void _PhysicsProcess(double delta)
    {
        if (_current is null)
        {
            // Deixa o loop consumir o SetMode(Edit) do cenário anterior.
            if (_cooldownFrames-- > 0)
                return;

            if (_pending.Count == 0)
            {
                GD.Print(_failures == 0
                    ? "== TODOS OS CENÁRIOS PASSARAM =="
                    : $"== {_failures} CENÁRIO(S) FALHARAM ==");
                GetTree().Quit(_failures == 0 ? 0 : 1);
                SetPhysicsProcess(false);
                return;
            }

            _current = _pending.Dequeue();
            _current.Bind(_main);
            GD.Print($"-- {_current.Name}...");
            _current.Begin();
            return;
        }

        _current.Tick();

        if (_current.Finished)
        {
            if (_current.Failure is null)
            {
                GD.Print($"-- {_current.Name}: PASS");
            }
            else
            {
                GD.PrintErr($"-- {_current.Name}: FAIL — {_current.Failure}");
                _failures++;
            }

            // Volta para Edit (desmonta física/driver) antes do próximo.
            _main.Loop.Enqueue(new SetModeCommand(SimMode.Edit));
            _current = null;
            _cooldownFrames = 2;
        }
    }
}
