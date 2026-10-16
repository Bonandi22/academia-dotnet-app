namespace Academia.Infrastructure.Lab;

// SQL dos cenários de índices. Fica em código para ser executado pela API (POST /api/lab/indices/{cenario}),
// mas é o mesmo SQL que deves saber escrever à mão na entrevista.
public static class LabSql
{
    public const string RemoverIndicesNonClustered = """
        DECLARE @sql NVARCHAR(MAX) = N'';
        SELECT @sql += N'DROP INDEX ' + QUOTENAME(i.name) + N' ON dbo.Transacoes;'
        FROM sys.indexes AS i
        WHERE i.object_id = OBJECT_ID(N'dbo.Transacoes')
          AND i.type IN (2, 6)            -- 2 = nonclustered rowstore, 6 = nonclustered columnstore
          AND i.is_primary_key = 0
          AND i.is_unique_constraint = 0;
        EXEC sys.sp_executesql @sql;
        """;

    // Índice "ingénuo": resolve o filtro por conta, mas obriga a key lookups para Valor/Tipo
    // e não entrega a ordem DESC com desempate por Id.
    private const string ContaData = """
        CREATE NONCLUSTERED INDEX IX_Transacoes_Conta_Data
            ON dbo.Transacoes (ContaId, DataMovimento);
        """;

    // Índices de cobertura: igualdade primeiro (ContaId), depois a ordem da navegação (DataMovimento DESC, Id DESC);
    // INCLUDE com as colunas só lidas. Servem a paginação keyset por conta e global sem key lookups.
    private const string Cobertura = """
        CREATE NONCLUSTERED INDEX IX_Transacoes_Conta_Data_Id
            ON dbo.Transacoes (ContaId, DataMovimento DESC, Id DESC)
            INCLUDE (Valor, Tipo);

        CREATE NONCLUSTERED INDEX IX_Transacoes_Data_Id
            ON dbo.Transacoes (DataMovimento DESC, Id DESC)
            INCLUDE (ContaId, Valor, Tipo);
        """;

    // Columnstore nonclustered para reporting: guarda por coluna, comprime e processa em batch mode.
    // Mantém os índices de cobertura para as leituras linha a linha (paginação).
    private const string Columnstore = Cobertura + """

        CREATE NONCLUSTERED COLUMNSTORE INDEX NCCI_Transacoes_Reporting
            ON dbo.Transacoes (ContaId, DataMovimento, Valor, Tipo);
        """;

    public static readonly IReadOnlyDictionary<string, (string Sql, string Descricao)> Cenarios =
        new Dictionary<string, (string, string)>(StringComparer.OrdinalIgnoreCase)
        {
            ["nenhum"] = ("", "Só o clustered index da PK (Id). Qualquer filtro por conta ou data faz scan."),
            ["conta-data"] = (ContaData, "Índice (ContaId, DataMovimento). Seek por conta, mas com key lookups e sort para a ordem DESC."),
            ["cobertura"] = (Cobertura, "Índices de cobertura para paginação por conta e global. Sem key lookups nem sort."),
            ["columnstore"] = (Columnstore, "Cobertura + columnstore nonclustered. Agregações sobre milhões de linhas em batch mode.")
        };
}
