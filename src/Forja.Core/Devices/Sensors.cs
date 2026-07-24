using Forja.Anvil;
using Forja.Core.State;

namespace Forja.Core.Devices;

/// <summary>
/// Sensor fotoelétrico de barreira (RF-03): feixe por raycast ao longo do
/// eixo local +X. Semântica decidida no T026: é um feixe físico — o raycast
/// para no PRIMEIRO corpo, e só marca detecção se esse corpo for uma peça
/// (estrutura no caminho corta o feixe, como no mundo real). Por isso o
/// sensor é posicionado de modo que só peças cruzem o feixe.
/// Também usado como sensor de altura/difuso (posicionado na altura desejada).
/// </summary>
public sealed class PhotoSensor : DeviceBehavior
{
    private bool _detected;

    /// <summary>Estado atual da detecção — a camada visual acende a lente
    /// com isso (só leitura, Artigo II.2).</summary>
    public bool Detected => _detected;

    public override void Tick(SimContext ctx)
    {
        var from = Instance.Transform.Pos;
        var to = from + LocalXAxis() * GetFloat("range", 1f);

        var hit = ctx.Physics.Raycast(from, to);
        _detected = hit is { } h && ctx.Parts.IsPart(h.EntityId);

        ctx.Io.SetInput(Id, "detect", _detected);
    }

    public override void WriteState(ref StateHasher hasher) => hasher.Add(_detected);
}

/// <summary>
/// Sensor de altura difuso (RF-03): montado ACIMA da esteira olhando para
/// baixo (−Y), mede a distância até o primeiro corpo. Detecta quando o eco
/// vem de uma peça a até "threshold" metros — peça alta chega perto do
/// sensor, peça baixa fica além do threshold. É o sensor do separador por
/// altura da demo (contracts/modbus-mapping.md).
/// </summary>
public sealed class HeightSensor : DeviceBehavior
{
    private bool _detected;

    /// <summary>Estado atual da detecção — a camada visual acende a lente
    /// com isso (só leitura, Artigo II.2).</summary>
    public bool Detected => _detected;

    public override void Tick(SimContext ctx)
    {
        var from = Instance.Transform.Pos;
        var to = from + new Vec3(0f, -GetFloat("range", 2f), 0f);

        var hit = ctx.Physics.Raycast(from, to);
        _detected = hit is { } h && ctx.Parts.IsPart(h.EntityId)
            && from.Y - h.Point.Y <= GetFloat("threshold", 0.5f);

        ctx.Io.SetInput(Id, "detect", _detected);
    }

    public override void WriteState(ref StateHasher hasher) => hasher.Add(_detected);
}

/// <summary>
/// Sensor de nível analógico (Fase 2, US1): montado no topo de um silo/tanque
/// olhando para baixo (−Y), mede a distância até a superfície do material
/// (primeira peça abaixo) e reporta o NÍVEL preenchido como grandeza contínua
/// — o equivalente a um sensor ultrassônico de nível. É o primeiro sensor que
/// escreve uma palavra (`%IW`) em vez de um bit.
///
/// Nível (% cheio) = (alcance − distância à superfície) / alcance, saturado em
/// [0, 100]: superfície mais alta (mais material) → nível maior. Sem peça no
/// feixe → tanque vazio (0%). A porta `level` é PortType.Word; o cartão da cena
/// escala o percentual→bruto e o programa de CLP reescala em ST (ADR 0005).
/// </summary>
public sealed class LevelSensor : DeviceBehavior
{
    private float _level;
    private float[] _janela = Array.Empty<float>();
    private float[] _ordenar = Array.Empty<float>();
    private int _preenchidos;
    private int _proximo;

    /// <summary>Nível publicado em % (0–100), já amortecido. Só leitura.</summary>
    public float Level => _level;

    /// <summary>Leitura crua do último tick, antes do amortecimento — a camada
    /// visual e os testes usam para mostrar o que o feixe realmente viu.</summary>
    public float LevelCru { get; private set; }

    public override void Build(SimContext ctx)
    {
        // damping = quantos ticks entram na mediana. 1 = sem amortecimento
        // (comportamento da Fase 2 original, e o default para não mudar cena
        // nenhuma que já existe).
        int n = Math.Clamp(GetInt("damping", 1), 1, 600);
        _janela = new float[n];
        _ordenar = new float[n];
        _preenchidos = 0;
        _proximo = 0;
        _level = 0f;
        LevelCru = 0f;
    }

    public override void Tick(SimContext ctx)
    {
        float range = GetFloat("range", 1f);
        var from = Instance.Transform.Pos;
        var to = from + new Vec3(0f, -range, 0f);

        var hit = ctx.Physics.Raycast(from, to);
        float fill = hit is { } h && ctx.Parts.IsPart(h.EntityId) && range > 0f
            ? Math.Clamp((range - (from.Y - h.Point.Y)) / range, 0f, 1f)
            : 0f;
        LevelCru = fill * 100f;

        if (_janela.Length == 0)
            _janela = _ordenar = new float[1];   // Tick sem Build (testes de unidade)

        _janela[_proximo] = LevelCru;
        _proximo = (_proximo + 1) % _janela.Length;
        if (_preenchidos < _janela.Length)
            _preenchidos++;

        _level = Mediana();
        ctx.Io.SetInputWord(Id, "level", _level);
    }

    /// <summary>
    /// MEDIANA, não média. O ruído deste sensor é de IMPULSO: o feixe cai numa
    /// fresta entre duas peças, atravessa até o fundo e a leitura despenca a 0
    /// por alguns ticks. Média espalharia esse zero por toda a janela; mediana
    /// simplesmente o descarta enquanto ele for minoria. É o mesmo motivo pelo
    /// qual transmissor de nível de verdade tem amortecimento, e por que
    /// rejeição de spike não se faz com filtro de primeira ordem.
    /// </summary>
    private float Mediana()
    {
        if (_preenchidos == 1)
            return _janela[(_proximo - 1 + _janela.Length) % _janela.Length];

        Array.Copy(_janela, _ordenar, _preenchidos);
        Array.Sort(_ordenar, 0, _preenchidos);
        int meio = _preenchidos / 2;
        return (_preenchidos & 1) == 1
            ? _ordenar[meio]
            : (_ordenar[meio - 1] + _ordenar[meio]) * 0.5f;
    }

    // Grandeza contínua entra no hash quantizada (mm), como as poses (research
    // R5); o bruto do registrador também entra pelo hash da IoTable. A JANELA
    // também entra: ela decide as saídas dos próximos ticks, então duas
    // execuções com janelas diferentes não são o mesmo estado (Artigo I.4).
    public override void WriteState(ref StateHasher hasher)
    {
        hasher.AddQuantized(_level);
        hasher.Add(_preenchidos);
        hasher.Add(_proximo);
        for (int i = 0; i < _janela.Length; i++)
            hasher.AddQuantized(_janela[i]);
    }
}

/// <summary>
/// Sensor de proximidade (RF-03): capacitivo detecta qualquer peça;
/// indutivo detecta só metal (usa PartKind.Material).
/// </summary>
public sealed class ProximitySensor : DeviceBehavior
{
    private bool _detected;

    /// <summary>Estado atual da detecção — a camada visual acende a lente
    /// com isso (só leitura, Artigo II.2).</summary>
    public bool Detected => _detected;

    public override void Tick(SimContext ctx)
    {
        float range = GetFloat("range", 0.3f);
        bool inductiveOnly = GetString("mode", "capacitive") == "inductive";

        var center = Instance.Transform.Pos + LocalXAxis() * (range / 2f);
        var ids = ctx.Physics.QueryBox(center, new Vec3(range / 2f, 0.15f, 0.15f));

        _detected = false;
        foreach (uint id in ids)
        {
            if (!ctx.Parts.TryGet(id, out var part))
                continue;
            if (!inductiveOnly || part.Kind.Material == "metal")
            {
                _detected = true;
                break;
            }
        }

        ctx.Io.SetInput(Id, "detect", _detected);
    }

    public override void WriteState(ref StateHasher hasher) => hasher.Add(_detected);
}

/// <summary>
/// Balança (spec 003 / T038): soma a massa das peças apoiadas na plataforma e
/// publica o total numa palavra de entrada. É o terceiro dispositivo analógico
/// da Fase 2 e existe para provar que o canal de palavras é GENÉRICO — não foi
/// feito sob medida para o sensor de nível (research R8).
///
/// O nome evita `Scale` de propósito: `AnalogScale` já é a faixa do cartão, e
/// dois "Scale" no mesmo domínio confundem quem lê depois.
/// </summary>
public sealed class WeighScale : DeviceBehavior
{
    private float _weight;

    /// <summary>Peso medido em kg (só leitura).</summary>
    public float Weight => _weight;

    public override void Tick(SimContext ctx)
    {
        float sizeX = GetFloat("sizeX", 0.6f);
        float sizeZ = GetFloat("sizeZ", 0.6f);
        float height = GetFloat("height", 0.3f);

        var center = Instance.Transform.Pos + new Vec3(0f, height / 2f, 0f);
        var ids = ctx.Physics.QueryBox(center, new Vec3(sizeX / 2f, height / 2f, sizeZ / 2f));

        // Ordem canônica ANTES de somar (Artigo I.3). Soma de float não é
        // associativa: (a+b)+c pode diferir de a+(b+c) no último bit. Somar na
        // ordem que a física devolveu faria o hash depender da ordem de
        // varredura do motor — determinismo quebrado sem nenhum sintoma
        // visível. É por isso que a QueryBox dos testes devolve invertido.
        float total = 0f;
        foreach (uint id in ids.OrderBy(i => i))
        {
            if (ctx.Parts.TryGet(id, out var part))
                total += part.Kind.Mass;
        }

        _weight = total;
        ctx.Io.SetInputWord(Id, "weight", _weight);
    }

    public override void WriteState(ref StateHasher hasher) => hasher.AddQuantized(_weight);
}
