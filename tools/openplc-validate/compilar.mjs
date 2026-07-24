/**
 * Compila arquivos .st com o STruC++ extraído do OpenPLC Editor, sem GUI.
 *
 * Carrega as mesmas bibliotecas .stlib que o Editor injeta — sem elas, todo
 * programa que usa TON, R_TRIG, CTU, CTUD ou TP falha com "Undefined type",
 * que é erro do harness, não do programa.
 *
 * Uso (via validar.ps1, ou direto):
 *   ELECTRON_RUN_AS_NODE=1 "OpenPLC Editor.exe" compilar.mjs <arquivo.st> [...]
 */
import { readFileSync, readdirSync } from 'fs';
import { basename, join } from 'path';
import { compile, getVersion, loadStlibFromString } from 'strucpp';

const EDITOR = process.env.OPENPLC_EDITOR_DIR
  || 'C:/Users/JCINFO/AppData/Local/Programs/open-plc-editor';
const LIBS = EDITOR + '/resources/strucpp/libs';

const arquivos = process.argv.slice(2);
if (arquivos.length === 0) {
  console.error('uso: compilar.mjs <arquivo.st> [outro.st ...]');
  process.exit(2);
}

const libraries = [];
const carregadas = [];
for (const nome of readdirSync(LIBS).filter((f) => f.endsWith('.stlib'))) {
  try {
    libraries.push(loadStlibFromString(readFileSync(join(LIBS, nome), 'utf8'), nome));
    carregadas.push(nome);
  } catch (e) {
    console.error(`  (biblioteca ${nome} não carregou: ${e.message})`);
  }
}

console.log('STruC++ ' + getVersion());
console.log('libs: ' + carregadas.join(', '));
console.log('');

let falhas = 0;

for (const caminho of arquivos) {
  const nome = basename(caminho);
  let r;
  try {
    r = compile(readFileSync(caminho, 'utf8'), {
      fileName: nome,
      headerFileName: 'generated.hpp',
      debug: true,
      lineMapping: true,
      libraries,
    });
  } catch (e) {
    console.log(`X  ${nome}: EXCEÇÃO ${e.message}`);
    falhas++;
    continue;
  }

  if (r.success) {
    const tus = r.cppFiles ?? [];
    const linhas =
      (r.cppCode ? r.cppCode.split('\n').length : 0) +
      tus.reduce((s, f) => s + String(f.code ?? f.content ?? '').split('\n').length, 0);
    const nomes = tus.map((f) => f.fileName ?? f.name).filter(Boolean);
    console.log(
      `OK ${nome} -> ${linhas} linhas de C++` +
        (nomes.length ? ` em ${nomes.join(', ')}` : '') +
        (r.headerCode ? ' + generated.hpp' : ''),
    );
  } else {
    console.log(`X  ${nome}: FALHOU`);
    falhas++;
  }

  for (const e of r.errors ?? []) console.log(`   ERRO  linha ${e.line}:${e.column}  ${e.message}`);
  for (const w of r.warnings ?? []) console.log(`   aviso linha ${w.line}:${w.column}  ${w.message}`);
}

console.log('');
console.log(falhas === 0 ? `TODOS PASSARAM (${arquivos.length})` : `${falhas} de ${arquivos.length} falharam`);
process.exit(falhas > 0 ? 1 : 0);
