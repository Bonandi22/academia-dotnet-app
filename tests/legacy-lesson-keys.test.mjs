import test from 'node:test';
import assert from 'node:assert/strict';
// O Node (>= 22.18) remove os tipos de um .ts sem decorators; o ProgressService não pode ser importado assim.
import { migrateLessonKey, migrateReviewId } from '../src/app/core/legacy-lesson-keys.ts';

test('chaves de aula por índice passam ao id da secção', () => {
  assert.equal(migrateLessonKey('csharp::0'), 'csharp::async-await');
  assert.equal(migrateLessonKey('data::3'), 'data::transactions-concurrency');
  assert.equal(migrateReviewId('lesson:aspnet:1'), 'lesson:aspnet:authn-vs-authz');
});

test('chaves já migradas, desconhecidas ou de outra origem ficam iguais', () => {
  for (const key of ['csharp::async-await', 'csharp::99', 'desconhecido::0', 'semseparador']) assert.equal(migrateLessonKey(key), key);
  for (const id of ['lesson:aspnet:authn-vs-authz', 'lesson:react:7', 'quiz:q1', 'flashcard:auth-vs-authz']) assert.equal(migrateReviewId(id), id);
});

test('a migração é idempotente (corre em cada arranque)', () => {
  for (const key of ['csharp::0', 'quality::2']) assert.equal(migrateLessonKey(migrateLessonKey(key)), migrateLessonKey(key));
  assert.equal(migrateReviewId(migrateReviewId('lesson:csharp:2')), 'lesson:csharp:di-lifetimes');
});
