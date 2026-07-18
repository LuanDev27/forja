# Demo OpenPLC — Separador por altura (RF-09)

A Forja simula a planta; o **OpenPLC Runtime** executa a lógica de controle
(`separador.st`) e conversa com a Forja por **Modbus TCP** — a Forja é o
*slave* (servidor, porta **5020**) e o OpenPLC é o *master*.

```
OpenPLC Runtime ──FC02 lê sensores / FC05-FC15 escreve atuadores──▶ Forja:5020
```

## 1. Instalar o OpenPLC Runtime (Windows)

1. Baixe o instalador em <https://autonomylogic.com/download/> (seção
   "OpenPLC Runtime", opção Windows) e instale.
2. Ao final, o Runtime abre um serviço web local. Acesse
   <http://localhost:8080> (usuário/senha padrão: `openplc` / `openplc`).

## 2. Carregar o programa

1. Na interface web: **Programs → Browse** → selecione
   `demo/openplc/separador.st` → **Upload program**.
2. Dê um nome (ex.: "Separador") e confirme a compilação — deve terminar em
   *Compilation finished successfully*.

## 3. Apontar o OpenPLC para a Forja (Slave Devices)

1. **Slave Devices → Add new device**:
   - **Device Name**: `Forja`
   - **Device Protocol**: `Generic Modbus TCP Device`
   - **IP Address**: `127.0.0.1` (Forja na mesma máquina)
   - **IP Port**: `5020`
   - **Slave ID**: `1`
   - **Discrete Inputs (%IX100.0)**: Start Address `0`, Size `3`
   - **Coils (%QX100.0)**: Start Address `0`, Size `3`
   - Demais áreas (Input Registers etc.): Size `0` (a v1 é digital-only).
2. Salve. O OpenPLC mapeia o I/O remoto a partir de `%IX100.0`/`%QX100.0` —
   exatamente os endereços usados no `separador.st`.

## 4. Rodar a demo

1. **Forja**: abra o projeto no Godot e rode; no editor, **Abrir…** →
   `demo/separador-altura.forja` → botão **Rodar**. O painel "Conexão PLC"
   deve mostrar **Aguardando master…**.
2. **OpenPLC**: na interface web, clique **Start PLC**. Na Forja o estado
   muda para **Conectado**.
3. No painel **HMI** da Forja, pressione o **Botão de comando** (Start):
   - a luz indicadora acende e a esteira liga;
   - peças baixas (S) seguem até o fim da esteira e caem na calha da direita;
   - peças altas (L) param sob o sensor, o pistão as desvia para a calha
     lateral e a esteira religa sozinha.
4. Pressione Start de novo para desligar a planta.

## Falha segura (Artigo VII)

Com a planta rodando, clique **Stop PLC** no OpenPLC (ou derrube a rede):
em ~1 s (timeout da cena) a Forja **pausa a simulação sozinha** e reporta
"master sem atividade". Religando o PLC, basta **Rodar** de novo.

## Solução de problemas

| Sintoma | Causa provável |
|---|---|
| Estado fica em "Aguardando master…" | OpenPLC sem *Start PLC*, ou Slave Device com IP/porta errados |
| "Erro" ao dar Rodar na Forja | Porta 5020 ocupada por outra instância — feche-a ou troque a porta no painel Conexão PLC (e no Slave Device) |
| PLC conecta mas nada se move | Botão Start não pressionado (a esteira só roda com a planta ligada) |
| Firewall pergunta na primeira vez | Permita acesso em rede privada para o Godot/Forja |
