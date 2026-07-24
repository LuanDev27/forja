/**
 * Extrai o compilador STruC++ de dentro do OpenPLC Editor para uma pasta
 * chamável por linha de comando.
 *
 * Por que isto existe: o Editor 4.2.8 é um app Electron e o STruC++ é um
 * pacote npm (`strucpp`) empacotado pelo webpack dentro de `app.asar`. Não há
 * binário `iec2c` para chamar — mas o bundle vem com **source map completo**,
 * e o source map carrega o código-fonte original de cada módulo. Dá para
 * escrevê-lo de volta no disco e ter o compilador de verdade, na versão exata
 * que o Editor instalado usa.
 *
 * O que precisa ser remendado, e por quê: o webpack faz tree-shaking, então os
 * arquivos "barril" (o `index.js` que só re-exporta) somem do bundle — eles
 * viraram referências diretas. Este script os reconstrói a partir dos próprios
 * arquivos extraídos.
 *
 * Rodar com o Node que já vem no Editor:
 *   ELECTRON_RUN_AS_NODE=1 "OpenPLC Editor.exe" extrair.js <destino>
 */
const fs = require('fs');
const path = require('path');

const EDITOR = process.env.OPENPLC_EDITOR_DIR
  || 'C:/Users/JCINFO/AppData/Local/Programs/open-plc-editor';
const MAPA = EDITOR + '/resources/app.asar/dist/main/main.js.map';

const destino = process.argv[2];
if (!destino) {
  console.error('uso: extrair.js <pasta-destino>');
  process.exit(2);
}

const nm = path.join(destino, 'node_modules');

// ---------------------------------------------------------------------------
// 1. Escrever no disco toda fonte de node_modules que o source map carrega.
// ---------------------------------------------------------------------------
const map = JSON.parse(fs.readFileSync(MAPA, 'utf8'));
let escritos = 0;
map.sources.forEach((s, i) => {
  const m = s.match(/^webpack:\/\/open-plc-editor\/\.\/(node_modules\/.+)$/);
  if (!m) return;
  const dest = path.join(destino, m[1]);
  fs.mkdirSync(path.dirname(dest), { recursive: true });
  fs.writeFileSync(dest, map.sourcesContent[i] ?? '');
  escritos++;
});
console.log(`[1/4] ${escritos} arquivos extraídos do source map`);

function jsRecursivo(dir) {
  let out = [];
  for (const e of fs.readdirSync(dir, { withFileTypes: true })) {
    const full = path.join(dir, e.name);
    if (e.isDirectory()) out = out.concat(jsRecursivo(full));
    else if (e.name.endsWith('.js')) out.push(full);
  }
  return out;
}

// Pega as DUAS formas de trazer nomes de outro módulo: `import { a } from "x"`
// e `export { a } from "x"`. A segunda é justamente como o strucpp puxa seus
// barris internos — esquecê-la deixa o pacote extraído sem carregar.
const RE_IMPORT_NOMEADO = /(?:import|export)\s+(?:type\s+)?\{([^}]*)\}\s*from\s*["']([^"']+)["']/g;
const RE_EXPORT_DECL = /export\s+(?:async\s+)?(?:function|const|let|var|class)\s+([A-Za-z_$][\w$]*)/g;
const RE_EXPORT_LISTA = /export\s*\{([^}]*)\}/g;

function nomesImportados(texto, filtro) {
  const out = new Map(); // especificador -> Set(nomes)
  let m;
  RE_IMPORT_NOMEADO.lastIndex = 0;
  while ((m = RE_IMPORT_NOMEADO.exec(texto))) {
    const spec = m[2];
    if (!filtro(spec)) continue;
    const nomes = m[1]
      .split(',')
      .map((s) => s.trim().split(/\s+as\s+/)[0].trim())
      .filter(Boolean);
    if (!out.has(spec)) out.set(spec, new Set());
    nomes.forEach((n) => out.get(spec).add(n));
  }
  return out;
}

/** Onde cada nome exportado mora, dentro de uma árvore. */
function indiceDeExports(dir) {
  const mapa = new Map();
  for (const f of jsRecursivo(dir)) {
    const rel = './' + path.relative(dir, f).split(path.sep).join('/');
    const txt = fs.readFileSync(f, 'utf8');
    let m;
    RE_EXPORT_DECL.lastIndex = 0;
    while ((m = RE_EXPORT_DECL.exec(txt))) if (!mapa.has(m[1])) mapa.set(m[1], rel);
    RE_EXPORT_LISTA.lastIndex = 0;
    while ((m = RE_EXPORT_LISTA.exec(txt))) {
      for (const parte of m[1].split(',')) {
        const nome = parte.trim().split(/\s+as\s+/).pop()?.trim();
        if (nome && !mapa.has(nome)) mapa.set(nome, rel);
      }
    }
  }
  return mapa;
}

const todos = jsRecursivo(nm);

// ---------------------------------------------------------------------------
// 2. Barris de pacote: quem é importado por nome nu ("chevrotain") precisa de
//    um index.js + package.json que o webpack tinha dispensado.
// ---------------------------------------------------------------------------
const precisa = new Map();
for (const f of todos) {
  const encontrados = nomesImportados(fs.readFileSync(f, 'utf8'), (s) => /^[@\w]/.test(s) && !s.includes(' '));
  for (const [pkg, nomes] of encontrados) {
    if (!precisa.has(pkg)) precisa.set(pkg, new Set());
    nomes.forEach((n) => precisa.get(pkg).add(n));
  }
}

let barris = 0;
for (const [pkg, nomes] of precisa) {
  const dir = path.join(nm, pkg);
  if (!fs.existsSync(dir) || pkg === 'strucpp') continue;

  const linhas = [];
  if (pkg === 'lodash-es') {
    // lodash-es é um arquivo por função, cada um com `export default`.
    for (const arq of fs.readdirSync(dir)) {
      if (!arq.endsWith('.js') || arq === 'index.js') continue;
      linhas.push(`export { default as ${arq.replace(/\.js$/, '')} } from "./${arq}"`);
    }
    // `first` é apelido histórico de `head`; o tree-shaking come o arquivo.
    if (fs.existsSync(path.join(dir, 'head.js'))) {
      linhas.push('export { default as first } from "./head.js"');
    }
  } else {
    const idx = indiceDeExports(dir);
    const porArquivo = new Map();
    for (const n of nomes) {
      const origem = idx.get(n);
      if (!origem) continue;
      if (!porArquivo.has(origem)) porArquivo.set(origem, []);
      porArquivo.get(origem).push(n);
    }
    for (const [arq, ns] of porArquivo) linhas.push(`export { ${ns.join(', ')} } from ${JSON.stringify(arq)}`);
  }

  if (linhas.length === 0) continue;
  fs.writeFileSync(path.join(dir, 'index.js'), linhas.join('\n') + '\n');
  fs.writeFileSync(
    path.join(dir, 'package.json'),
    JSON.stringify({ name: pkg, version: '0.0.0-extraido-do-editor', type: 'module', main: 'index.js', exports: { '.': './index.js' } }, null, 2),
  );
  barris++;
}
console.log(`[2/4] ${barris} barris de pacote reconstruídos`);

// ---------------------------------------------------------------------------
// 3. Barris INTERNOS: `import ... from "./algo/index.js"` onde o index sumiu.
// ---------------------------------------------------------------------------
let internos = 0;
for (const f of jsRecursivo(nm)) {
  const encontrados = nomesImportados(fs.readFileSync(f, 'utf8'), (s) => s.startsWith('.') && s.endsWith('/index.js'));
  for (const [spec, nomes] of encontrados) {
    const alvo = path.resolve(path.dirname(f), spec);
    if (fs.existsSync(alvo)) continue;

    const dir = path.dirname(alvo);
    if (!fs.existsSync(dir)) continue;
    const idx = indiceDeExports(dir);
    const porArquivo = new Map();
    for (const n of nomes) {
      const origem = idx.get(n);
      if (!origem) continue;
      if (!porArquivo.has(origem)) porArquivo.set(origem, []);
      porArquivo.get(origem).push(n);
    }
    const linhas = [...porArquivo.entries()].map(([arq, ns]) => `export { ${ns.join(', ')} } from ${JSON.stringify(arq)}`);
    if (linhas.length === 0) continue;
    fs.writeFileSync(alvo, linhas.join('\n') + '\n');
    internos++;
  }
}
console.log(`[3/4] ${internos} barris internos reconstruídos`);

// ---------------------------------------------------------------------------
// 4. package.json do strucpp e da raiz.
// ---------------------------------------------------------------------------
fs.writeFileSync(
  path.join(nm, 'strucpp', 'package.json'),
  JSON.stringify({ name: 'strucpp', version: '0.0.0-extraido-do-editor', type: 'module', main: 'dist/index.js', exports: { '.': './dist/index.js' } }, null, 2),
);
fs.writeFileSync(
  path.join(destino, 'package.json'),
  JSON.stringify({ name: 'strucpp-extraido', private: true, type: 'module' }, null, 2),
);
console.log('[4/4] pronto: ' + destino);
