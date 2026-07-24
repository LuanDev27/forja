# Validar os `.st` sem abrir o OpenPLC Editor

```powershell
.\tools\openplc-validate\validar.ps1
```

Compila todos os programas de `plc/` e `demo/` com o **STruC++ de verdade** — o
mesmo compilador, na mesma versão, que o OpenPLC Editor instalado usa — e
falha com linha, coluna e motivo. Sem GUI, sem clicar em Build, sem preencher
tabela de variáveis à mão.

## Por que isto existe

Até aqui, validar um cenário custava uma sessão de GUI: `File → New Project`,
preencher a tabela de variáveis à mão (porque o `Open Project` do Editor 4.2.8
zera a tabela ao abrir do disco), colar o corpo, clicar Build. Na prática isso
significava que **os programas ficavam sem validação por semanas**, e o
histórico do repositório mostra exatamente isso: 3 dos 7 cenários validados,
os outros 4 "pendentes, dependem de GUI".

Com este script a validação virou um comando, e os 4 pendentes passaram.

## Como funciona

O OpenPLC Editor 4.2.8 é um app Electron, e o STruC++ é um pacote npm
(`strucpp`) empacotado pelo webpack dentro de `app.asar`. Não há binário
`iec2c` para chamar. Mas três coisas se encaixam:

1. **O binário do Editor é um Node.** `ELECTRON_RUN_AS_NODE=1` faz
   `OpenPLC Editor.exe` rodar como Node 22 puro, sem abrir janela.
2. **O bundle vem com source map completo.** `main.js.map` carrega o
   código-fonte original de cada módulo — inclusive os 43 arquivos do
   `strucpp`. `extrair.js` escreve tudo de volta no disco.
3. **A API é pura.** `strucpp.compile(fonteST, opções)` recebe texto e devolve
   `{ success, errors, warnings, cppFiles, headerCode }`. Sem I/O, sem
   subprocesso.

O que precisa de remendo: o webpack faz *tree-shaking*, então os arquivos
"barril" (o `index.js` que só re-exporta) somem do bundle. `extrair.js` os
reconstrói a partir dos próprios arquivos extraídos — inclusive os internos do
`strucpp`, que ele puxa com `export … from`, e o `lodash-es`, que é um arquivo
por função com `export default`.

**A versão é sempre a do Editor instalado.** Nada é baixado da internet: se o
Editor for atualizado, o próximo `validar.ps1` extrai a versão nova.

## Os controles negativos

`controles-negativos/` tem dois `.st` quebrados de propósito — um com erro de
sintaxe, outro com sintaxe perfeita e erro semântico. Eles rodam **antes** da
biblioteca, e **têm de falhar**. Se passarem, o script aborta antes de compilar
qualquer coisa de verdade.

Isso não é zelo decorativo. Um validador montado por extração pode facilmente
ficar carregando um compilador mutilado que devolve `success: true` para tudo —
e aí um relatório de "8/8 passaram" seria pior que nenhum relatório. Os dois
controles são o que separa "validado" de "achei que validei".

O segundo controle (erro semântico) é o que importa mais: ele prova que a
análise de tipo e escopo rodou, não só o parser. É a classe de erro que o
analógico levanta — `UINT` vs `INT`, conversão por `DINT`, `%IW` localizado.

## Armadilha: as bibliotecas `.stlib`

Sem passar `libraries` para o `compile()`, todo programa que usa `TON`,
`R_TRIG`, `CTU`, `CTUD` ou `TP` falha com `Undefined type` — e o erro parece
ser do programa, quando é do harness. O Editor injeta os `.stlib` de
`resources/strucpp/libs/` automaticamente; `compilar.mjs` faz o mesmo.

Foi exatamente esse o susto na primeira rodada: 5 de 8 cenários "falharam" por
um defeito do validador, não do código.

## O que isto NÃO valida

Compilar não é rodar. Este script prova que o programa é IEC 61131-3 válido,
que os tipos fecham e que os endereços localizados (`%IX`, `%QX`, `%IW`, `%QW`)
foram aceitos e ligados. Ele **não** prova que a lógica está correta, que o
Runtime aceita o board, nem que a planta se comporta como esperado — isso é o
teste headless da Forja (`tests/`) e a bancada.

## Arquivos

| | |
|---|---|
| `validar.ps1` | o comando |
| `extrair.js` | extrai o STruC++ do Editor e remonta os barris |
| `compilar.mjs` | chama `strucpp.compile` com os `.stlib` do Editor |
| `controles-negativos/` | os dois `.st` que têm de falhar |
| `.strucpp/` | o compilador extraído (gerado; fora do git) |

`OPENPLC_EDITOR_DIR` aponta para outra instalação, se precisar.
