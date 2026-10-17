using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Academia.Infrastructure.Persistence.Migrations
{
    /// <summary>
    /// Migração só de dados: as aulas deixam de ser identificadas pela posição da secção ("0", "1"…)
    /// e passam ao id estável da secção. Mesmo mapeamento que src/app/core/legacy-lesson-keys.ts
    /// (tests/content-integrity.test.mjs verifica que coincidem). Congelado: não editar.
    /// </summary>
    public partial class MigrateLessonKeysToSectionIds : Migration
    {
        // (ModuleId, índice antigo, id da secção)
        private const string Mapping = """
            (VALUES
                ('csharp', '0', 'async-await'), ('csharp', '1', 'task-cancellation'), ('csharp', '2', 'di-lifetimes'),
                ('aspnet', '0', 'middleware-pipeline'), ('aspnet', '1', 'authn-vs-authz'), ('aspnet', '2', 'contracts-errors'),
                ('data', '0', 'projections'), ('data', '1', 'n-plus-one'), ('data', '2', 'indexes-plans'), ('data', '3', 'transactions-concurrency'),
                ('architecture', '0', 'dependency-rule'), ('architecture', '1', 'solid'), ('architecture', '2', 'avoid-ceremony'),
                ('microservices', '0', 'sync-vs-events'), ('microservices', '1', 'delivery-idempotency'), ('microservices', '2', 'outbox-retries'),
                ('angular', '0', 'standalone-components'), ('angular', '1', 'signals-rxjs'), ('angular', '2', 'forms-ui-state'),
                ('react', '0', 'props-state'), ('react', '1', 'use-effect-cleanup'), ('react', '2', 'derived-state'),
                ('quality', '0', 'test-pyramid'), ('quality', '1', 'ci-cd'), ('quality', '2', 'evidence-based-diagnosis')
            ) AS m (ModuleId, OldId, NewId)
            """;

        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql($"""
                UPDATE lp SET LessonId = m.NewId
                FROM LessonProgress lp JOIN {Mapping} ON lp.ModuleId = m.ModuleId AND lp.LessonId = m.OldId;

                UPDATE r SET ItemKey = CONCAT('lesson:', m.ModuleId, ':', m.NewId)
                FROM Reviews r JOIN {Mapping} ON r.ItemKey = CONCAT('lesson:', m.ModuleId, ':', m.OldId);
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql($"""
                UPDATE lp SET LessonId = m.OldId
                FROM LessonProgress lp JOIN {Mapping} ON lp.ModuleId = m.ModuleId AND lp.LessonId = m.NewId;

                UPDATE r SET ItemKey = CONCAT('lesson:', m.ModuleId, ':', m.OldId)
                FROM Reviews r JOIN {Mapping} ON r.ItemKey = CONCAT('lesson:', m.ModuleId, ':', m.NewId);
                """);
        }
    }
}
