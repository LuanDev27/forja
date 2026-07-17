using System.Text.Json;

namespace Forja.Anvil.Scene;

/// <summary>
/// Instância de dispositivo na cena. Id é único, sequencial e nunca
/// reutilizado; a ordem canônica de iteração é id crescente (Artigo I.3).
/// </summary>
public sealed record DeviceInstance
{
    public required uint Id { get; init; }
    public required string TypeId { get; init; }
    public Pose Transform { get; init; } = Pose.Identity;

    /// <summary>Parâmetros validados contra o catálogo (data-model §2).</summary>
    public Dictionary<string, JsonElement> Params { get; init; } = new();
}

/// <summary>
/// Raiz do arquivo .forja (Artigo III: cena é dado, JSON versionado).
/// </summary>
public sealed record SceneDocument
{
    /// <summary>Versão atual do schema. Migrações: Core.Persistence.</summary>
    public const int CurrentSchemaVersion = 1;

    public required int SchemaVersion { get; init; }
    public required string Name { get; init; }

    /// <summary>Seed do IRandomSource da cena (Artigo I.2).</summary>
    public ulong Seed { get; init; }

    /// <summary>Sempre normalizada por id crescente ao salvar.</summary>
    public List<DeviceInstance> Devices { get; init; } = new();

    public List<IoTag> IoMap { get; init; } = new();

    public ConnectionConfig Connection { get; init; } = new();

    /// <summary>Próximo id livre para o editor (ids nunca são reutilizados).</summary>
    public uint NextDeviceId() => Devices.Count == 0 ? 1 : Devices.Max(d => d.Id) + 1;
}
