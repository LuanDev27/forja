# Biblioteca de lógica de CLP

Cenários clássicos de automação, cada um com a planta, o programa e a
explicação do raciocínio. A Forja é a bancada: em vez de descrever a lógica no
papel, ela roda contra uma planta que obedece à física, comandada por um CLP de
verdade via Modbus TCP.

Cada pasta é um trio:

| | |
|---|---|
| `*.forja` | a planta — abra na Forja com **Abrir…** |
| `*.st` | o programa em Texto Estruturado (IEC 61131-3), para OpenPLC v4 |
| `README.md` | o porquê: o conceito, a decisão de projeto e o que dá errado sem ela |

## Cenários

| # | Cenário | Conceitos |
|---|---|---|
| [01](01-partida-parada-selo/) | Partida/parada com selo | selo (*seal-in*), parada dominante, botoeira NA e NF, nível vs borda |
| [02](02-intertravamento-emergencia/) | Intertravamento e emergência | intertravamento por realimentação física, emergência travada, rearme obrigatório, partida a frio |

Os cenários são cumulativos: o 02 reusa o selo do 01.

## Como ligar o CLP

O passo a passo com o OpenPLC v4 — projeto ST, cadastro da Forja como
dispositivo remoto Modbus, transferência e teste de queda — está em
[`demo/openplc/README.md`](../demo/openplc/README.md). Para cada cenário muda só
o arquivo do programa e o mapa de endereços, que está no cabeçalho de cada `.st`.

Todos os cenários usam a Forja como **servidor** Modbus TCP na porta 5020, com o
CLP no papel de master.

## Sobre segurança

Vários cenários tratam de parada segura, intertravamento e emergência. Uma
ressalva que vale para todos, e que está detalhada no
[cenário 02](02-intertravamento-emergencia/):

> Parada de emergência de verdade é **circuito cabeado**, com relé de segurança
> cortando a energia dos atuadores. O CLP não fica no caminho dessa proteção.
> O que se programa é o intertravamento, o rearme e a sinalização.

Estes cenários são material de estudo rodando em simulador. Nenhum deles é
projeto de segurança de máquina.
