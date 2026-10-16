import test from 'node:test';
import assert from 'node:assert/strict';
import { readFileSync } from 'node:fs';

const read = (path) => readFileSync(new URL(path, import.meta.url), 'utf8');

test('cada flashcard tem um id estável e um moduleId válido', () => {
  const content = read('../src/app/data/study-data.ts');
  const moduleIds = [...content.matchAll(/id: '([a-z-]+)',\s*\n\s*title:/g)].map(match => match[1]);
  const flashcardsBlock = content.split('export const FLASHCARDS')[1];
  const entries = [...flashcardsBlock.matchAll(/\{ id: '([a-z0-9-]+)',.*?moduleId: '([a-z-]+)',/g)];
  assert.ok(entries.length >= 7, 'Esperava pelo menos 7 flashcards com id e moduleId.');

  const ids = entries.map(([, id]) => id);
  assert.strictEqual(new Set(ids).size, ids.length, 'Os ids dos flashcards têm de ser únicos — não podem depender da posição no array.');

  for (const [, , moduleId] of entries) {
    assert.ok(moduleIds.includes(moduleId), `moduleId '${moduleId}' de um flashcard não corresponde a nenhum STUDY_MODULES.id`);
  }
});

test('cada pergunta de quiz tem um moduleId válido', () => {
  const content = read('../src/app/data/study-data.ts');
  const moduleIds = [...content.matchAll(/id: '([a-z-]+)',\s*\n\s*title:/g)].map(match => match[1]);
  const quizBlock = content.split('export const QUIZ_QUESTIONS')[1].split('export interface Flashcard')[0];
  const entries = [...quizBlock.matchAll(/moduleId: '([a-z-]+)',/g)].map(match => match[1]);
  assert.ok(entries.length >= 8, 'Esperava pelo menos 8 perguntas de quiz com moduleId.');

  for (const moduleId of entries) {
    assert.ok(moduleIds.includes(moduleId), `moduleId '${moduleId}' de uma pergunta de quiz não corresponde a nenhum STUDY_MODULES.id`);
  }
});

test('os componentes de flashcards e quiz usam o id/moduleId dos dados, não heurísticas de texto', () => {
  const flashcardsComponent = read('../src/app/features/flashcards/flashcards.component.ts');
  const quizComponent = read('../src/app/features/quiz/quiz.component.ts');
  assert.ok(!flashcardsComponent.includes('moduleForTopic'), 'flashcards.component.ts ainda resolve o módulo por texto em vez de usar card.moduleId.');
  assert.ok(!quizComponent.includes('moduleForTopic'), 'quiz.component.ts ainda resolve o módulo por texto em vez de usar q.moduleId.');
  assert.ok(flashcardsComponent.includes('card.id'), 'flashcards.component.ts devia identificar o flashcard pelo seu id estável, não pela posição no array.');
});
