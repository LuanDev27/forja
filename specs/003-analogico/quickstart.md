# Quickstart — validar a Fase 2 (analógico)

Guia de validação ponta a ponta das quatro histórias. Cada bloco é
independentemente demonstrável (Artigo VIII). Pré-requisitos: .NET 8 SDK,
Godot 4 mono (para a UI), OpenPLC v4 (validação real). Testes headless não
precisam de GPU nem de PLC (Artigo V).

## Pré-requisitos

```powershell
dotnet build           # compila as 4 camadas
dotnet test            # cenários headless (sem GPU, sem PLC)
```

## US1 — Ler um valor analógico (P1) · `%IW`

**Headless**: cena mínima com um sensor de nível cuja grandeza física é conhecida.

- Faixa EU 0–100 cm, cartão bruto 0–65535, nível físico = 50 cm.
- Rodar N ticks → assertar `inputWords[offset] ≈ 32767` (meio da escala).
- 0 cm → 0; 100 cm → 65535; sem estouro fora da faixa (satura).
- Dois sensores iguais com cartões diferentes lendo 50 cm → brutos diferentes.

**Ponta a ponta (OpenPLC v4)**:
1. Subir a cena em modo servidor Modbus; apontar o OpenPLC para ela.
2. No programa ST, ler `%IW0` e reescalar: `eu := 0 + IW0 * (100 - 0) / 65535`.
3. Conferir que `eu` reconstrói o nível físico dentro da resolução de 16 bits.

Detalhes de escala: [contracts/scaling-eu-raw.md](contracts/scaling-eu-raw.md).

## US2 — Comandar por setpoint (P2) · `%QW`

**Headless**: cena com uma esteira de velocidade variável (EU 0–2 m/s).

- Forçar setpoint bruto no meio da escala via tabela de I/O manual →
  esteira anda a ~1 m/s.
- Setpoint 0 → parada; fundo de escala → velocidade máxima.
- Escala monotônica com o setpoint bruto.

**Ponta a ponta (OpenPLC v4)**:
1. Programa ST escreve `%QW0` (setpoint bruto de velocidade) a cada scan.
2. Conferir que o tick seguinte da simulação lê o valor escrito (defasagem ≤ 1
   tick — regra W5 de [iosnapshot-words.md](contracts/iosnapshot-words.md)).

## US3 — Controle de nível ponta a ponta (P3)

**Cena**: sensor de nível (`%IW0`) + esteira/atuador (`%QW0`), programa ST que
compara o nível contra um setpoint e decide a saída.

1. Escrever o ST: ler `%IW0`, reescalar, comparar com setpoint, escrever `%QW0`.
2. Validar no OpenPLC v4: abaixo do setpoint o atuador corrige numa direção,
   acima na outra; sem oscilar por ruído de quantização.
3. **Determinismo**: rodar a cena duas vezes com o mesmo seed e as mesmas
   entradas por N ticks → hash de estado final idêntico (Artigo I.4, W6).

## US4 — Carregar cena v1 numa Forja v2 (P2) · migração aditiva

**Headless**: pegar uma cena real digital da biblioteca (`schemaVersion: 1`).

1. `SceneSerializer.Load` → sucesso, sem erro; a migração 1→2 aditiva carimba a
   versão sem reescrever nada.
2. Rodar a cena → comportamento idêntico à Fase 1 (zero regressão digital).
3. Salvar de volta → campos aditivos com defaults explícitos, cena válida.
4. **Negativo**: cena v2 com campo analógico desconhecido → falha com caminho +
   motivo (`UnmappedMemberHandling.Disallow`), nunca corrompe em silêncio.

Ver [data-model.md §7](data-model.md) e [research.md R4](research.md).

## Critérios de aceite (mapeamento)

| Quickstart | Success Criteria | User Story |
|---|---|---|
| US1 headless + OpenPLC | SC-001 | US1 (P1) |
| US2 headless + OpenPLC | SC-002 | US2 (P2) |
| US3 malha + hash | SC-003, SC-004 | US3 (P3) |
| US4 carga + negativo | SC-005 | US4 (P2) |
| Cenários por dispositivo | SC-006 | US1–US3 |
| Build sem tocar 18 digitais | SC-007 | US4 |
