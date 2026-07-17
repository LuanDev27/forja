using Forja.Anvil.Catalog;
using Forja.Anvil.Contracts;
using Forja.Anvil.Scene;
using Forja.Anvil.Validation;
using Forja.Core.Devices;
using Forja.Core.Io;
using Forja.Core.Physics;
using Forja.Core.State;

namespace Forja.Core.Loop;

/// <summary>
/// Núcleo determinístico da simulação (Artigo I). Deve ser tickado
/// EXCLUSIVAMENTE de _PhysicsProcess a 60 Hz — nunca de _Process.
///
/// Máquina de modos (RF-01 / data-model §8):
///   Edit ──validar──▶ Run ◀──▶ Pause ──Step──▶ (1 tick) ──▶ Pause
///   qualquer ──▶ Edit (descarta peças, para driver)
///   driver Faulted ──▶ Pause + sinal (Artigo VII.1)
/// </summary>
public sealed class SimulationLoop : IDisposable
{
    private readonly DeviceCatalog _catalog;
    private readonly DeviceFactory _factory;
    private readonly IPhysicsWorld _physics;
    private readonly Func<ConnectionConfig, IPlcDriver> _driverFactory;

    private readonly Queue<ISimCommand> _commands = new();
    private readonly List<DeviceBehavior> _devices = new();
    private readonly Dictionary<uint, DeviceBehavior> _devicesById = new();

    private SimContext? _ctx;
    private IPlcDriver? _driver;
    private IoSnapshot _lastOutputs;
    private ulong _tick;
    private bool _built;
    private bool _deactivatePhysicsNextFrame;
    private volatile string? _driverFaultReason;

    public SceneDocument Document { get; private set; }

    public SimMode Mode { get; private set; } = SimMode.Edit;

    public ulong TickNumber => _tick;

    public IoTable? Io => _ctx?.Io;

    public IReadOnlyList<DeviceBehavior> Devices => _devices;

    public PartsManager? Parts => _ctx?.Parts;

    public DriverState DriverState => _driver?.State ?? DriverState.Stopped;

    public event Action<SimMode>? ModeChanged;
    public event Action<IReadOnlyList<ValidationError>>? ValidationFailed;
    public event Action<string>? DriverFault;

    public SimulationLoop(
        SceneDocument document,
        DeviceCatalog catalog,
        DeviceFactory factory,
        IPhysicsWorld physics,
        Func<ConnectionConfig, IPlcDriver> driverFactory)
    {
        Document = document;
        _catalog = catalog;
        _factory = factory;
        _physics = physics;
        _driverFactory = driverFactory;
        _physics.SetActive(false);
    }

    /// <summary>Troca o documento (editor). Só permitido em Edit.</summary>
    public bool ReplaceDocument(SceneDocument document)
    {
        if (Mode != SimMode.Edit)
            return false;
        Document = document;
        return true;
    }

    /// <summary>Comando da UI — consumido no próximo tick (Artigo II.2).</summary>
    public void Enqueue(ISimCommand command) => _commands.Enqueue(command);

    /// <summary>Chamar uma vez por physics frame (60 Hz).</summary>
    public void Tick()
    {
        if (_deactivatePhysicsNextFrame)
        {
            _deactivatePhysicsNextFrame = false;
            if (Mode is SimMode.Pause or SimMode.Edit)
                _physics.SetActive(false);
        }

        while (_commands.Count > 0)
            Handle(_commands.Dequeue());

        // Falha do driver derruba Run → Pause (Artigo VII.1) — nunca segue
        // com valores velhos silenciosamente.
        if (_driverFaultReason is { } reason && Mode == SimMode.Run)
        {
            _driverFaultReason = null;
            TransitionTo(SimMode.Pause);
            DriverFault?.Invoke(reason);
        }

        if (Mode == SimMode.Run)
        {
            DoTick();
        }
        else if (Mode == SimMode.Step)
        {
            DoTick();
            TransitionTo(SimMode.Pause);
            _deactivatePhysicsNextFrame = true;
            _physics.SetActive(true); // a engine integra ESTE frame; próximo desliga
        }
    }

    private void Handle(ISimCommand command)
    {
        switch (command)
        {
            case SetModeCommand set:
                RequestMode(set.Target);
                break;

            case ForceIoCommand force:
                _ctx?.Io.Force(force.Address, force.Value);
                break;

            case HmiCommand hmi:
                if (_devicesById.TryGetValue(hmi.DeviceId, out var device))
                    device.OnHmi(hmi.PortName, hmi.Value);
                break;
        }
    }

    private void RequestMode(SimMode target)
    {
        switch (Mode, target)
        {
            case (SimMode.Edit, SimMode.Run):
                var errors = IoMapValidator.Validate(Document, _catalog);
                if (errors.Count > 0)
                {
                    ValidationFailed?.Invoke(errors);
                    return;
                }
                Build();
                StartDriver();
                _physics.SetActive(true);
                TransitionTo(SimMode.Run);
                break;

            case (SimMode.Pause, SimMode.Run):
                _physics.SetActive(true); // sem salto: timestep fixo, nada acumula
                TransitionTo(SimMode.Run);
                break;

            case (SimMode.Run, SimMode.Pause):
                _physics.SetActive(false);
                TransitionTo(SimMode.Pause);
                break;

            case (SimMode.Pause, SimMode.Step):
                TransitionTo(SimMode.Step);
                break;

            case (_, SimMode.Edit) when Mode != SimMode.Edit:
                TearDown();
                TransitionTo(SimMode.Edit);
                break;

            default:
                // Transição inválida é ignorada (guardas do data-model §8).
                break;
        }
    }

    private void TransitionTo(SimMode mode)
    {
        Mode = mode;
        ModeChanged?.Invoke(mode);
    }

    private void Build()
    {
        if (_built)
            TearDown();

        var io = new IoTable(Document, _catalog);
        _ctx = new SimContext
        {
            Io = io,
            Physics = _physics,
            Parts = new PartsManager(_physics),
            Random = new SeededRandom(Document.Seed),
        };

        _devices.Clear();
        _devicesById.Clear();
        foreach (var instance in Document.Devices.OrderBy(d => d.Id))
        {
            var behavior = _factory.Create(instance, _catalog.Get(instance.TypeId));
            _devices.Add(behavior);
            _devicesById[instance.Id] = behavior;
        }

        foreach (var device in _devices)
            device.Build(_ctx);

        _tick = 0;
        _lastOutputs = IoSnapshot.Empty(0);
        _built = true;
    }

    private void StartDriver()
    {
        _driver?.Dispose();
        _driverFaultReason = null;
        _driver = _driverFactory(Document.Connection);
        _driver.StateChanged += OnDriverStateChanged;
        _driver.Start(Document.Connection);
    }

    private void OnDriverStateChanged(DriverState state, string? reason)
    {
        if (state == DriverState.Faulted)
            _driverFaultReason = reason ?? "falha no driver (sem motivo informado)";
        else if (state == DriverState.Ready)
            _driverFaultReason = null; // recuperou antes do fault ser consumido
    }

    private void TearDown()
    {
        if (_ctx is not null)
        {
            _ctx.Parts.Clear();
            foreach (var device in _devices)
                device.Teardown(_ctx);
            _ctx.Io.Reset();
        }

        _devices.Clear();
        _devicesById.Clear();

        if (_driver is not null)
        {
            _driver.StateChanged -= OnDriverStateChanged;
            _driver.Stop();
            _driver.Dispose();
            _driver = null;
        }

        _physics.SetActive(false);
        _ctx = null;
        _built = false;
    }

    private void DoTick()
    {
        if (_ctx is null || _driver is null)
            return;

        _tick++;
        _ctx.Tick = _tick;

        // Saídas do PLC chegadas no fim do tick anterior valem agora
        // (contracts/modbus-mapping.md — nunca no meio de um tick).
        _ctx.Io.ApplyOutputSnapshot(_lastOutputs);

        foreach (var device in _devices)
            device.Tick(_ctx);

        _ctx.Parts.Tick();

        var inputs = _ctx.Io.BuildInputSnapshot(_tick);
        _lastOutputs = _driver.Exchange(inputs);
    }

    /// <summary>Hash canônico do estado (Artigo I.4 / RNF-03).</summary>
    public ulong ComputeStateHash()
    {
        var hasher = StateHasher.Create();
        hasher.Add(_tick);
        foreach (var device in _devices)
        {
            hasher.Add(device.Id);
            device.WriteState(ref hasher);
        }
        _ctx?.Parts.WriteState(ref hasher);
        if (_ctx is not null)
            _ctx.Io.WriteState(ref hasher);
        return hasher.Hash;
    }

    public void Dispose() => TearDown();
}
