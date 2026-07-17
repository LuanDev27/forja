using System.Text.Json;
using Forja.Anvil;
using Forja.Anvil.Contracts;
using Forja.Anvil.Scene;
using Forja.Core.Editing;
using Forja.Core.Loop;
using Forja.Core.Persistence;
using Xunit;

namespace Forja.Core.Tests;

/// <summary>
/// T034 / RF-02: comandos de editor via <see cref="UndoRedoStack"/> — cada um
/// vai e volta restaurando o documento EXATO; a pilha guarda 100 níveis; e
/// editar cena é rejeitado fora do modo Edit.
/// </summary>
public class EditorCommandTests
{
    /// <summary>Igualdade estrutural = mesmo JSON canônico (Save normaliza).</summary>
    private static void AssertSameDoc(SceneDocument a, SceneDocument b) =>
        Assert.Equal(SceneSerializer.Save(a), SceneSerializer.Save(b));

    private static SceneDocument BaseDoc() => TestWorld.SensorAtuadorDoc() with { Name = "editor" };

    public static IEnumerable<object[]> Commands()
    {
        yield return new object[] { new PlaceDeviceCommand("sensor.test", new Pose(new Vec3(1, 0, 2), 90)) };
        yield return new object[] { new MoveDeviceCommand(1, new Vec3(3, 0, 4)) };
        yield return new object[] { new RotateDeviceCommand(2, 45f) };
        yield return new object[] { new DeleteSelectionCommand(new uint[] { 1 }) };
        yield return new object[] { new DuplicateSelectionCommand(new uint[] { 1, 2 }, new Vec3(0, 0, 1)) };
        yield return new object[] { new EditParamCommand(1, "range", JsonSerializer.SerializeToElement(2.5)) };
        yield return new object[] { new ReassignAddressCommand(1, "detect", new IoAddress(IoArea.DiscreteInput, 7)) };
    }

    [Theory]
    [MemberData(nameof(Commands))]
    public void Comando_DoEUndo_RestauraDocumentoExato(IEditorCommand command)
    {
        var original = BaseDoc();
        var stack = new UndoRedoStack(original);

        var afterDo = stack.Execute(command);
        Assert.NotEqual(SceneSerializer.Save(original), SceneSerializer.Save(afterDo)); // mudou algo

        var afterUndo = stack.Undo();
        AssertSameDoc(original, afterUndo);

        // E o redo devolve exatamente o estado pós-comando.
        var afterRedo = stack.Redo();
        AssertSameDoc(afterDo, afterRedo);
    }

    [Fact]
    public void PlaceDevice_UsaProximoIdLivre()
    {
        var stack = new UndoRedoStack(BaseDoc());
        var doc = stack.Execute(new PlaceDeviceCommand("actuator.test", Pose.Identity));

        Assert.Equal(3, doc.Devices.Count);
        Assert.Contains(doc.Devices, d => d.Id == 3); // 1 e 2 já existiam
    }

    [Fact]
    public void DeleteSelection_RemoveTagsOrfas()
    {
        var stack = new UndoRedoStack(BaseDoc());
        var doc = stack.Execute(new DeleteSelectionCommand(new uint[] { 1 }));

        Assert.DoesNotContain(doc.Devices, d => d.Id == 1);
        Assert.DoesNotContain(doc.IoMap, t => t.DeviceId == 1); // tag do sensor sumiu junto
        Assert.Contains(doc.IoMap, t => t.DeviceId == 2);       // a do atuador ficou
    }

    [Fact]
    public void Pilha_Guarda100Niveis_NaoMais()
    {
        var stack = new UndoRedoStack(BaseDoc());

        // 150 edições distintas: move o device 1 para X crescente.
        for (int i = 1; i <= 150; i++)
            stack.Execute(new MoveDeviceCommand(1, new Vec3(i, 0, 0)));

        Assert.Equal(UndoRedoStack.MaxUndoLevels, stack.UndoDepth);

        // Desfaz o máximo possível; não deve passar de 100 e não quebra.
        int undone = 0;
        while (stack.CanUndo)
        {
            stack.Undo();
            undone++;
        }
        Assert.Equal(UndoRedoStack.MaxUndoLevels, undone);
    }

    [Fact]
    public void EdicaoNovaAposUndo_DescartaRedo()
    {
        var stack = new UndoRedoStack(BaseDoc());
        stack.Execute(new MoveDeviceCommand(1, new Vec3(1, 0, 0)));
        stack.Execute(new MoveDeviceCommand(1, new Vec3(2, 0, 0)));
        stack.Undo(); // volta para X=1

        Assert.True(stack.CanRedo);
        stack.Execute(new MoveDeviceCommand(1, new Vec3(9, 0, 0))); // ramo novo
        Assert.False(stack.CanRedo);
    }

    [Fact]
    public void ExecuteEdit_RejeitadoForaDeEdit()
    {
        var physics = new FakePhysicsWorld();
        var loop = new SimulationLoop(
            BaseDoc(), TestWorld.Catalog(), TestWorld.Factory(), physics, _ => new FakeDriver());

        // Em Edit: aceita e muda o documento (mover preserva a I/O válida).
        Assert.True(loop.ExecuteEdit(new MoveDeviceCommand(1, new Vec3(1, 0, 0))));
        var docInEdit = SceneSerializer.Save(loop.Document);

        // Entra em Run (cena continua válida) e tenta editar: rejeitado.
        loop.Enqueue(new SetModeCommand(SimMode.Run));
        loop.Tick();
        Assert.Equal(SimMode.Run, loop.Mode);

        Assert.False(loop.ExecuteEdit(new MoveDeviceCommand(1, new Vec3(5, 0, 0))));
        Assert.False(loop.Undo());
        Assert.Equal(docInEdit, SceneSerializer.Save(loop.Document));
    }
}
