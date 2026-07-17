namespace Forja.Anvil.Contracts;

/// <summary>
/// Resultado explícito de operações que podem falhar. Artigo VII.3: erros
/// sempre carregam motivo — nunca exceção engolida.
/// </summary>
public readonly record struct Result<T>
{
    public bool Ok { get; }
    public T? Value { get; }
    public string? Error { get; }

    private Result(bool ok, T? value, string? error)
    {
        Ok = ok;
        Value = value;
        Error = error;
    }

    public static Result<T> Success(T value) => new(true, value, null);
    public static Result<T> Fail(string error) => new(false, default, error);

    /// <summary>Valor garantido; lança se chamado num resultado de falha.</summary>
    public T Require() =>
        Ok ? Value! : throw new InvalidOperationException($"Result de falha consumido como sucesso: {Error}");
}
