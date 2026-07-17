using System.Text.Json;
using Forja.Anvil.Catalog;
using Forja.Anvil.Contracts;
using Forja.Anvil.Scene;
using Forja.Core.Io;
using Forja.Core.Physics;
using Forja.Core.State;

namespace Forja.Core.Devices;

/// <summary>Contexto passado aos dispositivos a cada tick (60 Hz fixo).</summary>
public sealed class SimContext
{
    public const float Dt = 1f / 60f;

    public ulong Tick { get; internal set; }
    public required IoTable Io { get; init; }
    public required IPhysicsWorld Physics { get; init; }
    public required PartsManager Parts { get; init; }
    public required IRandomSource Random { get; init; }
}

/// <summary>
/// Comportamento de dispositivo (camada 2). Instanciado pela DeviceFactory a
/// partir do catálogo (Artigo III.2). Sem UI, sem input, sem render.
/// </summary>
public abstract class DeviceBehavior
{
    public uint Id { get; private set; }
    public DeviceInstance Instance { get; private set; } = null!;
    public DeviceTypeDef Type { get; private set; } = null!;

    internal void Bind(DeviceInstance instance, DeviceTypeDef type)
    {
        Id = instance.Id;
        Instance = instance;
        Type = type;
    }

    /// <summary>Criação de corpos físicos e estado inicial (Edit→Run).</summary>
    public virtual void Build(SimContext ctx) { }

    /// <summary>Desmontagem (→Edit).</summary>
    public virtual void Teardown(SimContext ctx) { }

    /// <summary>Um passo de simulação. Ordem de chamada: id crescente (Artigo I.3).</summary>
    public abstract void Tick(SimContext ctx);

    /// <summary>Contribui com o estado discreto para o hash (Artigo I.4).</summary>
    public virtual void WriteState(ref StateHasher hasher) { }

    /// <summary>Interação de HMI vinda da UI via comando (botão/chave).</summary>
    public virtual void OnHmi(string portName, bool value) { }

    // ---- Acesso a parâmetros com default do catálogo (data-model §2/§3) ----

    protected float GetFloat(string name, float fallback = 0f) =>
        TryGetParam(name, out var el)
            ? el.ValueKind == JsonValueKind.Number ? el.GetSingle() : fallback
            : fallback;

    protected int GetInt(string name, int fallback = 0) =>
        TryGetParam(name, out var el)
            ? el.ValueKind == JsonValueKind.Number ? el.GetInt32() : fallback
            : fallback;

    protected bool GetBool(string name, bool fallback = false) =>
        TryGetParam(name, out var el)
            ? el.ValueKind is JsonValueKind.True or JsonValueKind.False ? el.GetBoolean() : fallback
            : fallback;

    protected string GetString(string name, string fallback = "") =>
        TryGetParam(name, out var el)
            ? el.ValueKind == JsonValueKind.String ? el.GetString() ?? fallback : fallback
            : fallback;

    private bool TryGetParam(string name, out JsonElement element)
    {
        if (Instance.Params.TryGetValue(name, out element))
            return true;

        var def = Type.ParamDefs.FirstOrDefault(p => p.Name == name);
        if (def?.Default is JsonElement dflt)
        {
            element = dflt;
            return true;
        }
        element = default;
        return false;
    }

    /// <summary>Direção local +X do dispositivo no mundo (rotY em graus).</summary>
    protected Anvil.Vec3 LocalXAxis()
    {
        float rad = Instance.Transform.RotY * MathF.PI / 180f;
        return new Anvil.Vec3(MathF.Cos(rad), 0, -MathF.Sin(rad));
    }
}
