#!/usr/bin/env node
// Сверяет assembler.js (режим «Своя программа» машины с регистрами) с таблицей фикстур, которую читает
// и тестовый дубликат на C# (RegisterAssemblerTests): одинаковый вход → одинаковые слова или сообщения.
// Вызов: node scripts/check_assembler.js   (код выхода 1 — расхождение)
"use strict";
const fs = require("fs");
const path = require("path");

const root = path.join(__dirname, "..");
const assembler = require(path.join(root, "src/TimeSlices.Web/Content/slices/eniac-1946/examples/squares/implementations/register-machine-1960/assembler.js"));
const fixture = JSON.parse(fs.readFileSync(path.join(root, "tests/TimeSlices.Tests/Fixtures/register-assembler.json"), "utf8"));

let failed = 0;
for (const c of fixture.cases) {
    const r = assembler.assemble(c.source);
    const actual = JSON.stringify(c.errors ? r.errors : r.cells.map(x => [x.address, x.word]));
    const expected = JSON.stringify(c.errors || c.words);
    const ok = actual === expected && (c.errors ? r.cells.length === 0 : r.errors.length === 0);
    if (!ok) {
        failed++;
        console.log(`РАСХОЖДЕНИЕ «${c.name}»:\n  ожидалось ${expected}\n  получено  ${JSON.stringify(r)}`);
    }
}
console.log(`assembler.js: случаев ${fixture.cases.length}, расхождений ${failed}`);
process.exit(failed ? 1 : 0);
