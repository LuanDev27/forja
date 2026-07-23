using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using Forja.Anvil.Contracts;
using Forja.Anvil.Scene;

namespace Forja.Core.Persistence;

/// <summary>Migração de schema aplicada sobre o JSON bruto (Artigo III.3).</summary>
public interface ISceneMigration
{
    int FromVersion { get; }
    JsonNode Apply(JsonNode scene);
}

/// <summary>
/// v1 → v2 (Fase 2, ADR 0005): analógico é puramente aditivo — PortType e
/// AnalogScale entram por default do modelo na desserialização. A migração é
/// não-destrutiva: não remove nem renomeia campo, só existe para o Load poder
/// carimbar a versão (sem ela registrada, toda cena v1 falharia ao carregar).
/// </summary>
internal sealed class MigrationV1ToV2 : ISceneMigration
{
    public int FromVersion => 1;
    public JsonNode Apply(JsonNode scene) => scene;
}

/// <summary>
/// Persistência do .forja: JSON versionado, legível e diffável (RF-08).
/// Regras S1–S8 em contracts/forja-schema.md. Todo erro carrega caminho do
/// arquivo e motivo (Artigo VII.3).
/// </summary>
public static class SceneSerializer
{
    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = false,
        WriteIndented = true,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
    };

    /// <summary>Cadeia de migrações registradas, uma por salto de versão.</summary>
    public static readonly List<ISceneMigration> Migrations = new()
    {
        new MigrationV1ToV2(),
    };

    /// <summary>Serializa normalizado: devices em id crescente (S2).</summary>
    public static string Save(SceneDocument doc)
    {
        var normalized = doc with
        {
            Devices = doc.Devices.OrderBy(d => d.Id).ToList(),
            IoMap = doc.IoMap.OrderBy(t => t.Address.Area).ThenBy(t => t.Address.Offset).ToList(),
        };
        return JsonSerializer.Serialize(normalized, Options);
    }

    public static Result<SceneDocument> Load(string json, string sourcePath)
    {
        JsonNode? root;
        try
        {
            root = JsonNode.Parse(json);
        }
        catch (JsonException ex)
        {
            return Result<SceneDocument>.Fail(
                $"'{sourcePath}': JSON inválido — {ex.Message}");
        }

        if (root is null)
            return Result<SceneDocument>.Fail($"'{sourcePath}': arquivo vazio.");

        if (root["schemaVersion"] is not JsonNode versionNode
            || !versionNode.AsValue().TryGetValue(out int version))
        {
            return Result<SceneDocument>.Fail(
                $"'{sourcePath}': campo obrigatório 'schemaVersion' ausente ou inválido (S1).");
        }

        if (version > SceneDocument.CurrentSchemaVersion)
        {
            return Result<SceneDocument>.Fail(
                $"'{sourcePath}': schemaVersion {version} é de uma versão futura da Forja " +
                $"(máximo suportado: {SceneDocument.CurrentSchemaVersion}).");
        }

        // Migração encadeada; gap na cadeia = erro explícito (S1).
        while (version < SceneDocument.CurrentSchemaVersion)
        {
            var migration = Migrations.FirstOrDefault(m => m.FromVersion == version);
            if (migration is null)
            {
                return Result<SceneDocument>.Fail(
                    $"'{sourcePath}': sem migração registrada de schemaVersion {version} → {version + 1}.");
            }
            root = migration.Apply(root);
            version++;
            root["schemaVersion"] = version;
        }

        SceneDocument? doc;
        try
        {
            doc = root.Deserialize<SceneDocument>(Options);
        }
        catch (JsonException ex)
        {
            return Result<SceneDocument>.Fail(
                $"'{sourcePath}': {ex.Message} (JSON path: {ex.Path ?? "?"})");
        }

        if (doc is null)
            return Result<SceneDocument>.Fail($"'{sourcePath}': documento nulo após parse.");

        var duplicated = doc.Devices.GroupBy(d => d.Id).FirstOrDefault(g => g.Count() > 1);
        if (duplicated is not null)
        {
            return Result<SceneDocument>.Fail(
                $"'{sourcePath}': id de dispositivo duplicado: {duplicated.Key} (S2).");
        }

        // Ordem canônica em memória (Artigo I.3).
        doc.Devices.Sort((a, b) => a.Id.CompareTo(b.Id));
        return Result<SceneDocument>.Success(doc);
    }

    public static Result<SceneDocument> LoadFile(string path)
    {
        try
        {
            return Load(File.ReadAllText(path), path);
        }
        catch (IOException ex)
        {
            return Result<SceneDocument>.Fail($"'{path}': falha de leitura — {ex.Message}");
        }
        catch (UnauthorizedAccessException ex)
        {
            return Result<SceneDocument>.Fail($"'{path}': acesso negado — {ex.Message}");
        }
    }

    public static Result<bool> SaveFile(SceneDocument doc, string path)
    {
        try
        {
            File.WriteAllText(path, Save(doc));
            return Result<bool>.Success(true);
        }
        catch (IOException ex)
        {
            return Result<bool>.Fail($"'{path}': falha de escrita — {ex.Message}");
        }
        catch (UnauthorizedAccessException ex)
        {
            return Result<bool>.Fail($"'{path}': acesso negado — {ex.Message}");
        }
    }
}
