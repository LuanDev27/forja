# Aceite manual com OpenPLC real (T054, V-E — RF-06 / RF-09)

**Pré-requisito:** OpenPLC Runtime instalado e configurado seguindo
`demo/openplc/README.md` (programa `separador.st` carregado, Slave Device
apontando para `127.0.0.1:5020`).

**Roteiro Forja:** abrir o projeto no Godot (Play), **Abrir…** →
`demo/separador-altura.forja` → **Rodar**.

## Checklist

### Conexão (RF-06)

- [ ] Ao dar Rodar, o painel "Conexão PLC" mostra **Aguardando master…**
- [ ] Após *Start PLC* no OpenPLC, muda para **Conectado** em ~1 s
- [ ] A Tabela de I/O mostra os 6 pontos com a notação dupla (%IX/%QX + DI/Coil)

### Lógica via PLC (RF-09)

- [ ] Pressionar o botão Start no painel HMI → a luz indicadora acende e a esteira liga (lógica veio do PLC, não da Forja)
- [ ] Peça **baixa (S)** passa sob o sensor sem parar e cai na calha do fim da esteira
- [ ] Peça **alta (L)** para sob o sensor, o pistão a desvia para a calha lateral, recolhe e a esteira religa sozinha
- [ ] Reação sensor→pistão perceptivelmente imediata (< 100 ms — sem "peça passa antes do pistão reagir")
- [ ] Pressionar Start de novo → esteira para e luz apaga

### Falha segura (Artigo VII)

- [ ] *Stop PLC* com a planta rodando → em ~1 s a Forja pausa sozinha e reporta "master sem atividade"
- [ ] *Start PLC* de novo + **Rodar** na Forja → tudo volta a operar

## Resultado

- Data: ____
- Veredicto: [ ] aprovado · [ ] reprovado (anotar problemas abaixo)
- Observações:
