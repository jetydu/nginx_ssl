import ts from 'typescript'
import fs from 'node:fs'
import * as lc from '@vue/language-core'

const fileName = process.argv[2]
const text = fs.readFileSync(fileName, 'utf8')
const opts = { target: ts.ScriptTarget.ESNext, module: ts.ModuleKind.ESNext, allowJs: true }
const vco = lc.getDefaultCompilerOptions()
const plugin = lc.createVueLanguagePlugin(ts, opts, vco, id => id)
const vc = plugin.createVirtualCode(fileName, 'vue', ts.ScriptSnapshot.fromString(text))
for (const c of lc.forEachEmbeddedCode(vc)) {
  if (c.id !== 'template') continue
  const t = c.snapshot.getText(0, c.snapshot.getLength())
  console.log('LEN', t.length)
  console.log(t.slice(0, 4000))
}
