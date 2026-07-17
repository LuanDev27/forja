# Contrato — arquivo `.forja` (schema v1)

JSON UTF-8 (sem BOM), indentado com 2 espaços, chaves camelCase — legível e
diffável (RF-08). Extensão `.forja`. Corresponde 1:1 ao
[data-model.md](../data-model.md).

## Exemplo mínimo válido

```json
{
  "schemaVersion": 1,
  "name": "Separador por altura",
  "seed": 42,
  "devices": [
    {
      "id": 1,
      "typeId": "conveyor.belt.io",
      "transform": { "pos": [0.0, 0.0, 0.0], "rotY": 0.0 },
      "params": { "speed": 0.5, "length": 4.0 }
    },
    {
      "id": 2,
      "typeId": "sensor.height",
      "transform": { "pos": [1.5, 0.3, 0.0], "rotY": 0.0 },
      "params": { "threshold": 0.15 }
    },
    {
      "id": 3,
      "typeId": "actuator.piston",
      "transform": { "pos": [2.5, 0.0, -0.4], "rotY": 90.0 },
      "params": { "stroke": 0.6 }
    }
  ],
  "ioMap": [
    { "deviceId": 1, "portName": "run",    "address": { "area": "coil",          "offset": 0 } },
    { "deviceId": 2, "portName": "detect", "address": { "area": "discreteInput", "offset": 0 } },
    { "deviceId": 3, "portName": "extend", "address": { "area": "coil",          "offset": 1 } }
  ],
  "connection": {
    "driver": "modbus-tcp",
    "bindAddress": "0.0.0.0",
    "port": 502,
    "timeoutMs": 1000
  }
}
```

## Regras do schema

| # | Regra | Origem |
|---|---|---|
| S1 | `schemaVersion` obrigatório, inteiro ≥ 1. Versão > atual ⇒ erro "arquivo de versão futura"; versão < atual ⇒ pipeline de migração; gap na cadeia ⇒ erro explícito | Artigo III.3 |
| S2 | `devices[].id` únicos, ordenados crescente no arquivo (normalização ao salvar) | Artigo I.3 |
| S3 | `typeId` desconhecido no catálogo ⇒ erro de carga citando o typeId e o índice do device — nunca ignorar silenciosamente | Artigo VII.3 |
| S4 | `params` desconhecidos para o tipo ⇒ erro; params ausentes ⇒ default do catálogo | III.2 |
| S5 | `ioMap`: `(area, offset)` único; direção compatível com a porta; `deviceId` existente | Artigo VI |
| S6 | Unidades: metros e graus (`rotY`). 1 unidade = 1 m (decisão Q1) | spec §7 |
| S7 | Round-trip: `load(save(doc)) == doc` — verificado por teste de igualdade estrutural | RF-08 |
| S8 | Todo erro de carga reporta **caminho do arquivo + JSON path + motivo** | Artigo VII.3 |

## Migração

`ISceneMigration { int From; int To; SceneDocument Apply(SceneDocument) }` —
cadeia registrada em ordem; aplicada sequencialmente até a versão atual.
Migração nunca altera o arquivo original em disco sem o usuário salvar.
