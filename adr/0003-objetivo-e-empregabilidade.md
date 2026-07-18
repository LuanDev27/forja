# ADR 0003 — O objetivo é empregabilidade, não comercialização

**Status:** Aceito
**Data:** 2026-07-18
**Recalibra:** [ADR 0002](0002-ecossistema-de-custo-zero.md), seção
"Armadilhas identificadas" (A1–A4)

## Contexto

O ADR 0002 avaliou risco de licença assumindo que a ponta SaaS do ecossistema
*poderia* virar comercial. Essa premissa estava errada.

O objetivo declarado do ecossistema é: **aprender e evoluir como programador**
ao longo de muitas sessões, e **demonstrar essa evolução para conseguir vagas
de dev júnior em automação industrial**. O software não será vendido.

## Decisão

O critério de decisão técnica deste projeto passa a ser, nesta ordem:

1. **Quanto ensina** — profundidade de aprendizado é o retorno principal.
2. **Quanto aparece em descrição de vaga** de automação industrial.
3. **Quanto demonstra evolução** num repositório público.

Custo zero (ADR 0002, decisões 1–3) continua valendo integralmente. O que muda
é só a avaliação de licença.

### Efeito sobre A1–A4

As quatro armadilhas do ADR 0002 eram bloqueios sob premissa comercial. Sob a
premissa correta, viram **notas de rodapé** — riscos a conhecer caso o projeto
um dia mude de natureza, não restrições de escolha hoje:

| | Antes (premissa comercial) | Agora (premissa real) |
|---|---|---|
| **A1** Grafana AGPL | evitar em SaaS | liberado para uso próprio |
| **A2** Ultralytics YOLO AGPL | trocar por YOLOX/RT-DETR | liberado; permissivo ainda é bom hábito |
| **A3** TimescaleDB TSL | atenção se revender | irrelevante |
| **A4** Vercel Hobby / Ignition Maker non-commercial | evitar | **liberados — e Ignition Maker vira ativo** |

A inversão mais importante é a A4. O ADR 0002 recomendava evitar o **Ignition
Maker Edition** por ser não-comercial. Sob o objetivo real ele é o oposto de
uma armadilha: é um SCADA industrial de mercado, citado por nome em vaga, com
licença pessoal gratuita que cobre exatamente este uso.

### Efeito sobre "construir o próprio vs. usar o padrão"

O ADR 0002 recomendou construir o próprio dashboard em vez de usar Grafana,
justificado em parte pela licença. Com a licença fora da conta, sobra a tensão
real: **implementação própria ensina mais; ferramenta padrão casa com mais
vagas.** Ambas cabem no orçamento zero.

Decisão: **ferramenta padrão da indústria primeiro, implementação própria
depois.** Ordem importa — o padrão custa um dia, dá vocabulário da área e
palavra-chave de currículo; a implementação própria custa semanas e dá
profundidade e diferenciação. Fazer o padrão primeiro também informa o que
construir depois, porque você passa a saber o que a indústria espera.

## Consequências

- Nenhuma decisão técnica já tomada precisa ser revertida. O ADR 0002 fica
  válido; só a seção de armadilhas se lê com esta recalibração ao lado.
- Se o projeto um dia virar comercial, **este ADR volta a ficar inválido** e
  A1–A4 voltam a valer como escritos. Registrar aqui evita redescobrir isso
  tarde demais.
- Entra uma dimensão nova de avaliação que não existia: "isso aparece em vaga?"
  é agora critério legítimo, e às vezes vence a escolha tecnicamente mais
  elegante.

## Referências

- [`ROADMAP.md`](../ROADMAP.md) — o plano que operacionaliza este critério
