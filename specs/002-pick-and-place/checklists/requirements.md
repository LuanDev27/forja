# Specification Quality Checklist: Pick-and-place

**Purpose**: Validate specification completeness and quality before proceeding to planning
**Created**: 2026-07-19
**Feature**: [spec.md](../spec.md)

## Content Quality

- [x] No implementation details (languages, frameworks, APIs)
- [x] Focused on user value and business needs
- [x] Written for non-technical stakeholders
- [x] All mandatory sections completed

## Requirement Completeness

- [x] No [NEEDS CLARIFICATION] markers remain
- [x] Requirements are testable and unambiguous
- [x] Success criteria are measurable
- [x] Success criteria are technology-agnostic (no implementation details)
- [x] All acceptance scenarios are defined
- [x] Edge cases are identified
- [x] Scope is clearly bounded
- [x] Dependencies and assumptions identified

## Feature Readiness

- [x] All functional requirements have clear acceptance criteria
- [x] User scenarios cover primary flows
- [x] Feature meets measurable outcomes defined in Success Criteria
- [x] No implementation details leak into specification

## Notes

Passou na primeira iteração, com duas observações registradas:

**Sobre "no implementation details".** A descrição de entrada citava
`actuator.pickplace`, `SetKind` e `IPhysicsBody` — nomes de implementação. Foram
deliberadamente **mantidos fora** da spec: `SetKind` e a escolha de dispositivo
composto são decisões, e o lugar delas é o
[ADR 0004](../../../adr/0004-pick-and-place-e-o-fim-do-catalogo-congelado.md).
A spec fala de "vincular a peça ao cabeçote" e "fins de curso", que é o que o
usuário observa.

**Sobre fronteira de escopo.** Nenhum marcador de clarificação foi necessário
porque as três ambiguidades reais já tinham resposta assumida e registrada em
Assumptions: número de eixos (dois), natureza da garra (posse, não atrito) e
tipo de peça (a existente). As três são reversíveis sem retrabalho estrutural
caso mudem.

**Ponto de atenção para o `/speckit-plan`.** A US1 depende de uma capacidade que
não existe na abstração de física (trocar o tipo de um corpo em tempo de
execução). O plano deve tratar isso como primeira fatia vertical, provada por
cenário headless, antes de qualquer geometria ou visual — é o único item com
risco técnico real.
