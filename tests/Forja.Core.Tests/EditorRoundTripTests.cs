using System.Text.Json;
using Forja.Anvil;
using Forja.Anvil.Scene;
using Forja.Core.Editing;
using Forja.Core.Persistence;
using Xunit;

namespace Forja.Core.Tests;

/// <summary>
/// T035 / RF-08: montar uma cena SÓ por comandos de editor, salvar em disco e
/// recarregar deve dar igualdade estrutural — o que o editor produz é
/// exatamente o que o .forja guarda e devolve.
/// </summary>
public class EditorRoundTripTests
{
    [Fact]
    public void MontarPorComandos_SalvarCarregar_IgualdadeEstrutural()
    {
        // Cena vazia → adiciona sensor e atuador, posiciona, parametriza e mapeia.
        var doc = new SceneDocument { SchemaVersion = SceneDocument.CurrentSchemaVersion, Name = "montada" };
        var stack = new UndoRedoStack(doc);

        stack.Execute(new PlaceDeviceCommand("sensor.test", Pose.Identity));   // id 1
        stack.Execute(new PlaceDeviceCommand("actuator.test", Pose.Identity)); // id 2
        stack.Execute(new MoveDeviceCommand(1, new Vec3(-1.4f, 0.15f, 0)));
        stack.Execute(new RotateDeviceCommand(1, 90f));
        stack.Execute(new EditParamCommand(1, "range", JsonSerializer.SerializeToElement(1.2)));
        stack.Execute(new ReassignAddressCommand(1, "detect", new IoAddress(IoArea.DiscreteInput, 0)));
        var built = stack.Execute(new ReassignAddressCommand(2, "extend", new IoAddress(IoArea.Coil, 0)));

        Assert.Equal(2, built.Devices.Count);

        string path = Path.Combine(Path.GetTempPath(), $"forja-roundtrip-{Guid.NewGuid():N}.forja");
        try
        {
            Assert.True(SceneSerializer.SaveFile(built, path).Ok);
            var reloaded = SceneSerializer.LoadFile(path);
            Assert.True(reloaded.Ok, reloaded.Error);

            Assert.Equal(SceneSerializer.Save(built), SceneSerializer.Save(reloaded.Value!));
        }
        finally
        {
            File.Delete(path);
        }
    }
}
