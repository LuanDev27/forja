# Aceite manual do editor (T041, RF-02)

**Objetivo:** montar a cena demo ("Esteira mínima") do zero **só no editor** —
sem tocar em JSON — salvar, recarregar e rodar. Referência de resultado:
`demo/esteira-minima.forja`.

**Como rodar:** abrir o projeto no Godot 4.4.1 .NET (janela, não headless) e
apertar Play. ⚠️ Abrir a janela do Godot pode reescrever `project.godot` —
conferir `git diff project.godot` ao terminar e restaurar se preciso.

## Controles do editor (modo Edição)

| Ação | Como |
| --- | --- |
| Orbitar / pan / zoom | Botão do meio / botão direito / roda |
| Colocar dispositivo | Clicar no tipo no Catálogo, depois no chão (Shift mantém; Esc cancela) |
| Selecionar | Clique esquerdo (Ctrl acumula/alterna) |
| Mover | Arrastar o dispositivo ativo (snap 0,1 m) |
| Girar | Arrastar o anel verde (snap 15°) ou Q/E |
| Subir/descer | PageUp / PageDown (0,1 m) |
| Remover / duplicar | Delete / Ctrl+D |
| Desfazer / refazer | Ctrl+Z / Ctrl+Y (ou botões na barra de arquivo) |
| Parâmetros e endereços | Painel Propriedades (canto inferior direito) |
| Nova / abrir / salvar | Barra de arquivo (abaixo da toolbar de modos) |

## Checklist

### Montagem (RF-02)

- [ ] Nova cena → catálogo lista os tipos de `catalog/devices/` (sem recompilar)
- [ ] Colocar: emissor, esteira, calha (chute) e sumidouro (sink) por clique, com ghost de preview
- [ ] Mover cada um para perto das poses da demo (snap de 0,1 m observável)
- [ ] Girar a calha para 90° pelo anel ou Q/E (snap de 15° observável)
- [ ] PageUp/PageDown ajusta a altura (emissor ~0,8 m; calha abaixo do fim da esteira)
- [ ] Editar parâmetros no painel Propriedades (ex.: `interval` do emissor, `speed` da esteira, `tilt` da calha) — controles gerados do catálogo, sem UI por tipo
- [ ] Selecionar vários (Ctrl), duplicar (Ctrl+D) e remover (Delete)
- [ ] Ctrl+Z desfaz cada passo acima na ordem; Ctrl+Y refaz

### Persistência (RF-08)

- [ ] Salvar como `minha-esteira.forja`; conferir que o arquivo é JSON legível
- [ ] Nova cena (esvazia) → Abrir `minha-esteira.forja` → cena volta idêntica
- [ ] Abrir arquivo inválido/corrompido → diálogo de erro com caminho + motivo

### Rodar (integração com US1/US2)

- [ ] Rodar → peças caem na esteira, descem pela calha e somem no sumidouro
- [ ] Editar → painéis do editor voltam; Rodar de novo funciona
- [ ] (Opcional, RF-05) Endereçar portas de um sensor/pistão no painel Propriedades e ver na Tabela de I/O

## Resultado

- Data: 2026-07-17
- Veredicto: [x] aprovado · [ ] reprovado (anotar problemas abaixo)
- Observações: aprovado pelo usuário na janela do Godot ("tá tudo funcionando").
  Único problema encontrado durante o aceite — piso colocado depois engolia as
  esteiras — corrigido no commit cd30da5 (`flushToGround` no catálogo: piso
  assenta com o topo no nível do chão) e revalidado na mesma sessão.
