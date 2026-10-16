import test from 'node:test';
import assert from 'node:assert/strict';
import { readFileSync, existsSync } from 'node:fs';

const read = (path) => readFileSync(new URL(path, import.meta.url), 'utf8');

test('cada módulo de estudo tem um guia de conteúdo', () => {
  const modules = read('../src/app/data/study-data.ts');
  const lessons = read('../src/app/data/study-content.ts');
  const catalog = modules.split('export const STUDY_MODULES')[1].split('export interface QuizQuestion')[0];
  const ids = [...catalog.matchAll(/id: '([a-z-]+)'/g)].map(match => match[1]);
  for (const id of ids) assert.match(lessons, new RegExp(`moduleId: '${id}'`), `Guia em falta para ${id}`);
  assert.ok(ids.length >= 8, 'A academia deve incluir pelo menos os oito módulos base.');
});

test('as páginas principais estão ligadas às rotas Angular', () => {
  const routes = read('../src/app/app.routes.ts');
  for (const path of ["path: 'modulos'", "path: 'estudo/:moduleId'", "path: 'quiz'", "path: 'flashcards'", "path: 'revisao'", "path: 'conta'"]) {
    assert.ok(routes.includes(path), `Rota em falta: ${path}`);
  }
});

test('a aplicação inclui os serviços de progresso, autenticação e sincronização', () => {
  for (const path of [
    '../src/app/core/progress.service.ts',
    '../src/app/core/auth.service.ts',
    '../src/app/core/auth.interceptor.ts',
    '../src/app/core/learning-sync.service.ts',
    '../backend/Academia.Api/Program.cs',
    '../backend/Academia.Infrastructure/Persistence/AcademiaDbContext.cs'
  ]) assert.ok(existsSync(new URL(path, import.meta.url)), `Ficheiro em falta: ${path}`);
});
