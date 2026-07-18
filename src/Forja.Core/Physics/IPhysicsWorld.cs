using Forja.Anvil;

namespace Forja.Core.Physics;

public enum BodyKind
{
    /// <summary>Imóvel (piso, calha, guia). Pode ter velocidade de superfície (esteira).</summary>
    Static,

    /// <summary>Movido pelo core (pistão, stopper); empurra corpos rígidos.</summary>
    Kinematic,

    /// <summary>Peça sob gravidade (caixa).</summary>
    Rigid,
}

public readonly record struct BodySpec(
    BodyKind Kind,
    Vec3 HalfExtents,
    Pose Pose,
    float TiltXDeg = 0f,
    float Mass = 1f,
    float Friction = 0.8f);

public readonly record struct RayHit(uint EntityId, Vec3 Point);

/// <summary>Corpo físico abstrato — o core nunca toca a engine diretamente.</summary>
public interface IPhysicsBody
{
    Pose Pose { get; set; }
    Vec3 LinearVelocity { get; }

    /// <summary>Velocidade de superfície (esteiras): move o que está em contato.</summary>
    void SetSurfaceVelocity(Vec3 velocity);

    bool Asleep { get; }

    /// <summary>Acorda o corpo. Necessário quando uma superfície em contato
    /// muda de velocidade (esteira liga) — a engine não acorda sozinha
    /// corpos dormindo sobre um estático que mudou.</summary>
    void Wake();
}

/// <summary>
/// Abstração da física (Jolt via Godot na produção; fake nos testes de
/// lógica). Implementação Godot em Forja.Core.Physics.GodotPhysicsWorld;
/// roda 100% headless (Artigo II.3).
/// </summary>
public interface IPhysicsWorld
{
    IPhysicsBody CreateBox(uint entityId, BodySpec spec);

    void Remove(IPhysicsBody body);

    /// <summary>Primeiro corpo atingido pelo segmento, ou null.</summary>
    RayHit? Raycast(Vec3 from, Vec3 to);

    /// <summary>Ids de entidade cujos corpos intersectam a caixa dada.</summary>
    IReadOnlyList<uint> QueryBox(Vec3 center, Vec3 halfExtents);

    /// <summary>Liga/desliga a física (Run ↔ Pause/Edit) sem perder estado.</summary>
    void SetActive(bool active);
}
