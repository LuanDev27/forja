using System.Text.Json;
using System.Text.Json.Serialization;

namespace Forja.Anvil;

/// <summary>Vetor 3D do domínio (1 unidade = 1 m). Sem dependência de engine.</summary>
[JsonConverter(typeof(Vec3JsonConverter))]
public readonly record struct Vec3(float X, float Y, float Z)
{
    public static readonly Vec3 Zero = new(0, 0, 0);

    public static Vec3 operator +(Vec3 a, Vec3 b) => new(a.X + b.X, a.Y + b.Y, a.Z + b.Z);
    public static Vec3 operator -(Vec3 a, Vec3 b) => new(a.X - b.X, a.Y - b.Y, a.Z - b.Z);
    public static Vec3 operator *(Vec3 a, float s) => new(a.X * s, a.Y * s, a.Z * s);

    public float Length() => MathF.Sqrt(X * X + Y * Y + Z * Z);
}

/// <summary>Serializa Vec3 como array JSON compacto: [x, y, z].</summary>
public sealed class Vec3JsonConverter : JsonConverter<Vec3>
{
    public override Vec3 Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType != JsonTokenType.StartArray)
            throw new JsonException("Vec3 deve ser um array [x, y, z].");
        reader.Read();
        float x = reader.GetSingle();
        reader.Read();
        float y = reader.GetSingle();
        reader.Read();
        float z = reader.GetSingle();
        reader.Read();
        if (reader.TokenType != JsonTokenType.EndArray)
            throw new JsonException("Vec3 deve ter exatamente 3 componentes.");
        return new Vec3(x, y, z);
    }

    public override void Write(Utf8JsonWriter writer, Vec3 value, JsonSerializerOptions options)
    {
        writer.WriteStartArray();
        writer.WriteNumberValue(value.X);
        writer.WriteNumberValue(value.Y);
        writer.WriteNumberValue(value.Z);
        writer.WriteEndArray();
    }
}

/// <summary>Posição + rotação em torno de Y (graus). Suficiente para o editor v1.</summary>
public readonly record struct Pose(Vec3 Pos, float RotY)
{
    public static readonly Pose Identity = new(Vec3.Zero, 0f);
}
