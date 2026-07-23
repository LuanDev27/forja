using System;
using System.IO;
using Forja.Anvil.Catalog;
using Forja.Anvil.Contracts;
using Forja.Anvil.Scene;
using Forja.Bellows;
using Forja.Core.Devices;
using Forja.Core.Loop;
using Forja.Core.Persistence;
using Forja.Core.Physics;
using Forja.Studio.Editor;
using Forja.Studio.Headless;
using Forja.Studio.Rendering;
using Forja.Studio.UI;
using Godot;

namespace Forja.Studio;

/// <summary>
/// Bootstrap do Studio (T015): única ponte entre o Godot e o núcleo.
/// _PhysicsProcess (60 Hz, project.godot) é a ÚNICA fonte de tick (Artigo I.1).
///
/// Linha de comando (depois de "--"):
///   --scene caminho.forja   carrega cena e entra em Run
///   --forja-tests           roda os cenários headless e sai com exit code
/// </summary>
public partial class Main : Node3D
{
    private SimulationLoop? _loop;
    private GodotPhysicsWorld? _physics;

    public SimulationLoop Loop => _loop
        ?? throw new InvalidOperationException("Main ainda não inicializou o SimulationLoop.");

    public bool HasLoop => _loop is not null;

    public DeviceCatalog Catalog { get; private set; } = null!;

    /// <summary>Tempo do boot da engine até a Forja pronta, em ms (RNF-04).
    /// Medido de dentro para não depender de cronometrar o processo por fora;
    /// StartupLoadScenario cobra o limite.</summary>
    public static long StartupMs { get; private set; }

    public override void _Ready()
    {
        string[] args = OS.GetCmdlineUserArgs();

        string catalogDir = ResolveCatalogDir();
        var catalogResult = DeviceCatalog.LoadFromDirectory(catalogDir);
        if (!catalogResult.Ok)
        {
            GD.PushError($"Falha ao carregar catálogo de '{catalogDir}': {catalogResult.Error}");
            GetTree().Quit(1);
            return;
        }

        Catalog = catalogResult.Value!;

        var doc = new SceneDocument
        {
            SchemaVersion = SceneDocument.CurrentSchemaVersion,
            Name = "Nova cena",
        };

        string? scenePath = ArgValue(args, "--scene");
        if (scenePath is not null)
        {
            var loaded = SceneSerializer.LoadFile(ProjectSettings.GlobalizePath(scenePath));
            if (!loaded.Ok)
            {
                GD.PushError($"Falha ao carregar cena: {loaded.Error}");
                GetTree().Quit(1);
                return;
            }

            doc = loaded.Value!;
        }

        var physics = new GodotPhysicsWorld(GetWorld3D().Space);
        _physics = physics;

        // O registry dimensiona buffers pelo OutputCount da IoTable, que só
        // existe depois do Build (Edit→Run) — o closure resolve na hora certa.
        SimulationLoop loop = null!;
        loop = new SimulationLoop(
            doc,
            Catalog,
            DeviceFactory.CreateDefault(),
            physics,
            config => DriverRegistry.Create(config, loop.Io?.OutputCount ?? 0, loop.Io?.OutputWordCount ?? 0));
        _loop = loop;

        loop.DriverFault += reason =>
            GD.PushWarning($"Driver em falha: {reason} — simulação pausada (Artigo VII.1).");
        loop.ValidationFailed += errors =>
        {
            foreach (var error in errors)
                GD.PushError($"Validação de I/O: {error.Message}");
        };

        // Camada visual sempre presente (também no headless — exercita o
        // caminho de leitura sem custo de render); toolbar/editor só interativo.
        var view = new SceneView(this) { Name = "SceneView" };
        AddChild(view);

        if (Array.IndexOf(args, "--forja-tests") >= 0)
        {
            AddChild(new HeadlessHost(this));
        }
        else
        {
            AddChild(new ModeToolbar(this) { Name = "ModeToolbar" });
            AddChild(new IoTablePanel(this) { Name = "IoTablePanel" });
            AddChild(new ValidationDialog(this) { Name = "ValidationDialog" });
            AddChild(new HmiInteraction(this) { Name = "HmiInteraction" });

            // Editor (US3): câmera orbital + seleção/gizmos + catálogo +
            // propriedades + arquivo. Todos dirigem o modelo de edição do
            // core via EditorContext; ficam ocultos fora do modo Edit.
            var ctx = new EditorContext(this, view);
            AddChild(new EditorGrid(ctx) { Name = "EditorGrid" });
            AddChild(new EditorCamera { Name = "EditorCamera" });
            AddChild(new SelectionManager(ctx) { Name = "SelectionManager" });
            AddChild(new CatalogPanel(ctx) { Name = "CatalogPanel" });
            AddChild(new ParamsPanel(ctx) { Name = "ParamsPanel" });
            AddChild(new FileDialogs(ctx) { Name = "FileDialogs" });
            AddChild(new ConnectionPanel(ctx) { Name = "ConnectionPanel" });

            if (scenePath is not null)
                loop.Enqueue(new SetModeCommand(SimMode.Run));
        }

        StartupMs = (long)Time.GetTicksMsec();
        GD.Print($"Forja pronta em {StartupMs} ms — cena '{doc.Name}', " +
                 $"{doc.Devices.Count} dispositivo(s), driver '{doc.Connection.Driver}'.");
    }

    public override void _PhysicsProcess(double delta) => _loop?.Tick();

    public override void _ExitTree()
    {
        _loop?.Dispose();
        _physics?.Dispose();
    }

    /// <summary>
    /// Onde estão os JSON do catálogo. O loader é da camada 1 (System.IO
    /// puro, sem Godot — Artigo II), então precisa de um caminho de disco
    /// DE VERDADE: rodando do projeto isso é `res://`, mas no build exportado
    /// `res://` mora dentro do `.pck` e não existe no sistema de arquivos.
    /// Lá o catálogo viaja solto ao lado do executável (e o usuário pode
    /// inspecionar/estender os tipos — cena é dado, catálogo também).
    /// </summary>
    private static string ResolveCatalogDir()
    {
        string fromProject = ProjectSettings.GlobalizePath("res://catalog/devices");
        if (Directory.Exists(fromProject))
            return fromProject;

        string exeDir = Path.GetDirectoryName(OS.GetExecutablePath()) ?? ".";
        return Path.Combine(exeDir, "catalog", "devices");
    }

    private static string? ArgValue(string[] args, string name)
    {
        int i = Array.IndexOf(args, name);
        return i >= 0 && i + 1 < args.Length ? args[i + 1] : null;
    }
}
