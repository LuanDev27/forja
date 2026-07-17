using System.Text.Json;
using Forja.Anvil.Contracts;

namespace Forja.Anvil.Catalog;

/// <summary>
/// Catálogo de tipos de dispositivo carregado de catalog/devices/*.json em
/// runtime (Artigo III.2). Erros de carga são explícitos com caminho e motivo
/// (Artigo VII.3).
/// </summary>
public sealed class DeviceCatalog
{
    private readonly Dictionary<string, DeviceTypeDef> _types;

    public static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = false,
        ReadCommentHandling = JsonCommentHandling.Skip,
        WriteIndented = true,
    };

    private DeviceCatalog(Dictionary<string, DeviceTypeDef> types) => _types = types;

    public IReadOnlyCollection<DeviceTypeDef> All => _types.Values;

    public bool TryGet(string typeId, out DeviceTypeDef def) =>
        _types.TryGetValue(typeId, out def!);

    public DeviceTypeDef Get(string typeId) =>
        _types.TryGetValue(typeId, out var def)
            ? def
            : throw new KeyNotFoundException($"Tipo de dispositivo desconhecido no catálogo: '{typeId}'.");

    /// <summary>Monta um catálogo em memória (usado por testes headless).</summary>
    public static Result<DeviceCatalog> FromDefs(IEnumerable<DeviceTypeDef> defs)
    {
        var types = new Dictionary<string, DeviceTypeDef>();
        foreach (var def in defs)
        {
            var error = ValidateDef(def, source: "(memória)");
            if (error is not null)
                return Result<DeviceCatalog>.Fail(error);
            if (!types.TryAdd(def.TypeId, def))
                return Result<DeviceCatalog>.Fail($"typeId duplicado no catálogo: '{def.TypeId}'.");
        }
        return Result<DeviceCatalog>.Success(new DeviceCatalog(types));
    }

    public static Result<DeviceCatalog> LoadFromDirectory(string directory)
    {
        if (!Directory.Exists(directory))
            return Result<DeviceCatalog>.Fail($"Diretório de catálogo não existe: '{directory}'.");

        var types = new Dictionary<string, DeviceTypeDef>();
        // Ordena por nome de arquivo: carga determinística (Artigo I.3).
        foreach (var file in Directory.GetFiles(directory, "*.json").OrderBy(f => f, StringComparer.Ordinal))
        {
            DeviceTypeDef? def;
            try
            {
                def = JsonSerializer.Deserialize<DeviceTypeDef>(File.ReadAllText(file), JsonOptions);
            }
            catch (JsonException ex)
            {
                return Result<DeviceCatalog>.Fail($"Catálogo inválido em '{file}': {ex.Message}");
            }

            if (def is null)
                return Result<DeviceCatalog>.Fail($"Catálogo vazio em '{file}'.");

            var error = ValidateDef(def, file);
            if (error is not null)
                return Result<DeviceCatalog>.Fail(error);

            if (!types.TryAdd(def.TypeId, def))
                return Result<DeviceCatalog>.Fail(
                    $"typeId duplicado '{def.TypeId}' em '{file}'.");
        }

        return Result<DeviceCatalog>.Success(new DeviceCatalog(types));
    }

    private static string? ValidateDef(DeviceTypeDef def, string source)
    {
        if (string.IsNullOrWhiteSpace(def.TypeId))
            return $"typeId vazio em '{source}'.";
        if (string.IsNullOrWhiteSpace(def.Behavior))
            return $"behavior vazio para '{def.TypeId}' em '{source}'.";

        var portNames = new HashSet<string>(StringComparer.Ordinal);
        foreach (var port in def.Ports)
        {
            if (!portNames.Add(port.PortName))
                return $"Porta duplicada '{port.PortName}' no tipo '{def.TypeId}' em '{source}'.";
        }
        return null;
    }
}
