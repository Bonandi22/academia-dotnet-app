/**
 * Ordem das secções quando as chaves de aula eram `${moduleId}::${índice}` (até ao commit 81081d1).
 * Tabela congelada: NÃO derivar de STUDY_LESSONS, que pode ser reordenado. A migration EF
 * MigrateLessonKeysToSectionIds aplica exatamente o mesmo mapeamento no servidor
 * (tests/content-integrity.test.mjs verifica que os dois coincidem).
 */
export const LEGACY_LESSON_IDS: Readonly<Record<string, readonly string[]>> = {
  csharp: ['async-await', 'task-cancellation', 'di-lifetimes'],
  aspnet: ['middleware-pipeline', 'authn-vs-authz', 'contracts-errors'],
  data: ['projections', 'n-plus-one', 'indexes-plans', 'transactions-concurrency'],
  architecture: ['dependency-rule', 'solid', 'avoid-ceremony'],
  microservices: ['sync-vs-events', 'delivery-idempotency', 'outbox-retries'],
  angular: ['standalone-components', 'signals-rxjs', 'forms-ui-state'],
  react: ['props-state', 'use-effect-cleanup', 'derived-state'],
  quality: ['test-pyramid', 'ci-cd', 'evidence-based-diagnosis']
};

const legacyId = (moduleId: string, index: string): string | null =>
  /^\d+$/.test(index) ? LEGACY_LESSON_IDS[moduleId]?.[Number(index)] ?? null : null;

/** `csharp::0` → `csharp::async-await`; chaves já migradas ou desconhecidas ficam iguais. */
export function migrateLessonKey(key: string): string {
  const split = key.lastIndexOf('::');
  if (split < 1) return key;
  const id = legacyId(key.slice(0, split), key.slice(split + 2));
  return id ? `${key.slice(0, split + 2)}${id}` : key;
}

/** `lesson:csharp:0` → `lesson:csharp:async-await`; outros ids de revisão ficam iguais. */
export function migrateReviewId(reviewId: string): string {
  const match = /^lesson:([^:]+):(\d+)$/.exec(reviewId);
  const id = match ? legacyId(match[1], match[2]) : null;
  return match && id ? `lesson:${match[1]}:${id}` : reviewId;
}
