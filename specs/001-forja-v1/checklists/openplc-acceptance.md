# Aceite manual com OpenPLC real (T054, V-E — RF-06 / RF-09)

**Pré-requisito:** OpenPLC Runtime instalado e configurado seguindo
`demo/openplc/README.md` (programa `separador.st` carregado, Slave Device
apontando para `127.0.0.1:5020`).

**Roteiro Forja:** abrir o projeto no Godot (Play), **Abrir…** →
`demo/separador-altura.forja` → **Rodar**.

## Checklist

### Conexão (RF-06)

- [x] Ao dar Rodar, o painel "Conexão PLC" mostra **Aguardando master…**
- [x] Após *Start PLC* no OpenPLC, muda para **Conectado** em ~1 s
- [x] A Tabela de I/O mostra os 6 pontos com a notação dupla (%IX/%QX + DI/Coil)

### Lógica via PLC (RF-09)

- [x] Pressionar o botão Start no painel HMI → a luz indicadora acende e a esteira liga (lógica veio do PLC, não da Forja)
- [x] Peça **baixa (S)** passa sob o sensor sem parar e cai na calha do fim da esteira
- [x] Peça **alta (L)** para sob o sensor, o pistão a desvia para a calha lateral, recolhe e a esteira religa sozinha
- [x] Reação sensor→pistão perceptivelmente imediata (< 100 ms — sem "peça passa antes do pistão reagir")
- [x] Pressionar Start de novo → esteira para e luz apaga

### Falha segura (Artigo VII)

- [x] *Stop PLC* com a planta rodando → em ~1 s a Forja pausa sozinha e reporta "master sem atividade"
- [x] *Start PLC* de novo + **Rodar** na Forja → tudo volta a operar

## Resultado

- Data: 2026-07-18
- Veredicto: [x] aprovado · [ ] reprovado (anotar problemas abaixo)
- Observações:
  - Executado com **OpenPLC v4** (Editor + Runtime v4.1.8, Windows) — fluxo
    difere do v3: sem interface web, programa criado no Editor, dispositivo
    remoto com IO Groups, transferência pelo botão ▶ Play. O I/O remoto no
    v4 mapeia a partir de **%IX0.0/%QX0.0** (não %IX100.x do v3) —
    README e separador.st atualizados.
  - O aceite revelou dois bugs de UI reais (painéis de canto renderizados
    fora da janela e cliques da barra de modos engolidos por contêiner
    invisível) — corrigidos no commit 9c70322 e re-testados ao vivo com o
    PLC conectado.
  - Fail-safe verificado ao vivo: Stop PLC → pausa automática com "master
    sem atividade" (transição Run→Pause registrada no console sem clique
    do usuário); religado com Start PLC + Rodar sem resíduos.
