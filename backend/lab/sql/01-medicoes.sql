/* =====================================================================================
   LAB 01 — MEDIÇÕES GUIADAS
   Base: AcademiaLab (5 milhões de transações depois do seed).

   Como usar:
     1. Liga-te a localhost,3311 (sa) e seleciona a base AcademiaLab.
     2. Ativa o plano de execução REAL:
          SSMS / Azure Data Studio: Ctrl+M ("Include Actual Execution Plan")
          Rider (DataGrip): botão "Explain Plan" → "Explain Analyse" na consola.
     3. Corre cada bloco isoladamente e anota no LAB.md:
          - logical reads (separador Messages)  ← a métrica principal
          - CPU time / elapsed time
          - operadores do plano: Seek, Scan, Key Lookup, Sort, Hash Match
     4. Corre cada query 2 vezes e anota a segunda (a primeira paga cache frio e compilação).

   Os cenários de índices aplicam-se com a API: POST /api/lab/indices/{nenhum|conta-data|cobertura|columnstore}
   ===================================================================================== */

USE AcademiaLab;
GO
SET STATISTICS IO, TIME ON;
GO

-- Que índices existem neste momento?
SELECT i.name, i.type_desc,
       STUFF((SELECT ', ' + c.name + CASE WHEN ic.is_descending_key = 1 THEN ' DESC' ELSE '' END
              FROM sys.index_columns ic JOIN sys.columns c ON c.object_id = ic.object_id AND c.column_id = ic.column_id
              WHERE ic.object_id = i.object_id AND ic.index_id = i.index_id AND ic.is_included_column = 0
              ORDER BY ic.key_ordinal FOR XML PATH('')), 1, 2, '') AS chave,
       STUFF((SELECT ', ' + c.name
              FROM sys.index_columns ic JOIN sys.columns c ON c.object_id = ic.object_id AND c.column_id = ic.column_id
              WHERE ic.object_id = i.object_id AND ic.index_id = i.index_id AND ic.is_included_column = 1
              FOR XML PATH('')), 1, 2, '') AS include
FROM sys.indexes i
WHERE i.object_id = OBJECT_ID(N'dbo.Transacoes');
GO


/* -------------------------------------------------------------------------------------
   1. SEEK vs SCAN vs KEY LOOKUP
   Corre este bloco em 3 cenários: nenhum → conta-data → cobertura.
   Esperado:
     nenhum      → Clustered Index Scan das 5M linhas (logical reads altíssimos)
     conta-data  → Index Seek + Key Lookup por linha + Sort (ordem DESC com Id não está no índice)
     cobertura   → Index Seek sem lookup e sem sort
   ------------------------------------------------------------------------------------- */
DECLARE @conta INT = 42;

SELECT TOP (50) Id, ContaId, DataMovimento, Valor, Tipo
FROM dbo.Transacoes
WHERE ContaId = @conta
ORDER BY DataMovimento DESC, Id DESC;

-- Mesma query a pedir Descricao (fora de qualquer INCLUDE): no cenário cobertura volta o Key Lookup.
SELECT TOP (50) Id, ContaId, DataMovimento, Valor, Tipo, Descricao
FROM dbo.Transacoes
WHERE ContaId = @conta
ORDER BY DataMovimento DESC, Id DESC;
GO


/* -------------------------------------------------------------------------------------
   2. SARGABILITY — função na coluna mata o seek
   Cenário: cobertura (existe índice que começa por DataMovimento).
   ------------------------------------------------------------------------------------- */
-- Não SARGable: o SQL Server tem de calcular YEAR/MONTH para cada linha → scan.
SELECT COUNT_BIG(*), SUM(Valor)
FROM dbo.Transacoes
WHERE YEAR(DataMovimento) = 2026 AND MONTH(DataMovimento) = 3;

-- SARGable: intervalo semiaberto sobre a coluna limpa → seek no intervalo.
SELECT COUNT_BIG(*), SUM(Valor)
FROM dbo.Transacoes
WHERE DataMovimento >= '2026-03-01' AND DataMovimento < '2026-04-01';
GO


/* -------------------------------------------------------------------------------------
   3. CONVERSÃO IMPLÍCITA — VARCHAR na tabela, NVARCHAR no parâmetro
   É exatamente o que acontece quando o .NET envia uma string sem tipo explícito (SqlParameter
   sem SqlDbType → NVARCHAR). Procura o aviso amarelo (!) no SELECT do plano.
   ------------------------------------------------------------------------------------- */
CREATE NONCLUSTERED INDEX IX_Lab_Tipo ON dbo.Transacoes (Tipo) INCLUDE (Valor);
GO
DECLARE @tipoN NVARCHAR(20) = N'TRANSFERENCIA';
DECLARE @tipoV VARCHAR(20)  = 'TRANSFERENCIA';

SELECT COUNT_BIG(*), SUM(Valor) FROM dbo.Transacoes WHERE Tipo = @tipoN;  -- CONVERT_IMPLICIT na coluna → scan
SELECT COUNT_BIG(*), SUM(Valor) FROM dbo.Transacoes WHERE Tipo = @tipoV;  -- seek
GO
DROP INDEX IX_Lab_Tipo ON dbo.Transacoes;
GO


/* -------------------------------------------------------------------------------------
   4. PAGINAÇÃO — OFFSET vs KEYSET
   Cenário: cobertura. Compara logical reads da página 1, da página 50 000 e do keyset equivalente.
   ------------------------------------------------------------------------------------- */
-- Página 1
SELECT Id, ContaId, DataMovimento, Valor, Tipo
FROM dbo.Transacoes
ORDER BY DataMovimento DESC, Id DESC
OFFSET 0 ROWS FETCH NEXT 50 ROWS ONLY;

-- Página 50 000: lê e descarta 2 499 950 linhas antes de devolver 50.
SELECT Id, ContaId, DataMovimento, Valor, Tipo
FROM dbo.Transacoes
ORDER BY DataMovimento DESC, Id DESC
OFFSET 2499950 ROWS FETCH NEXT 50 ROWS ONLY;

-- Keyset numa posição igualmente profunda (meio do histórico): custo de uma página só.
DECLARE @depoisData DATETIME2(3) = '2025-05-01T00:00:00', @depoisId BIGINT = 9223372036854775807;
SELECT TOP (50) Id, ContaId, DataMovimento, Valor, Tipo
FROM dbo.Transacoes
WHERE DataMovimento < @depoisData
   OR (DataMovimento = @depoisData AND Id < @depoisId)
ORDER BY DataMovimento DESC, Id DESC;
GO


/* -------------------------------------------------------------------------------------
   5. AGREGAÇÃO — ROWSTORE vs COLUMNSTORE
   Cenário: columnstore. A dica IGNORE_NONCLUSTERED_COLUMNSTORE_INDEX força o rowstore
   na mesma base, para comparar sem trocar de cenário.
   Repara no plano: com columnstore aparece "Execution Mode: Batch" e segment elimination.
   ------------------------------------------------------------------------------------- */
SELECT YEAR(DataMovimento) AS Ano, MONTH(DataMovimento) AS Mes, Tipo,
       COUNT_BIG(*) AS Quantidade, SUM(Valor) AS Total
FROM dbo.Transacoes
GROUP BY YEAR(DataMovimento), MONTH(DataMovimento), Tipo
ORDER BY Ano, Mes, Tipo
OPTION (IGNORE_NONCLUSTERED_COLUMNSTORE_INDEX);

SELECT YEAR(DataMovimento) AS Ano, MONTH(DataMovimento) AS Mes, Tipo,
       COUNT_BIG(*) AS Quantidade, SUM(Valor) AS Total
FROM dbo.Transacoes
GROUP BY YEAR(DataMovimento), MONTH(DataMovimento), Tipo
ORDER BY Ano, Mes, Tipo;
GO


/* -------------------------------------------------------------------------------------
   6. PARAMETER SNIFFING
   Cenário: cobertura. O plano é compilado para o PRIMEIRO valor recebido e reutilizado.
   Um intervalo de 1 dia (~5 mil linhas) gera um plano Seek + Key Lookup; reutilizado para
   2 anos (~3,6 milhões de linhas) faz milhões de lookups.
   ------------------------------------------------------------------------------------- */
CREATE OR ALTER PROCEDURE dbo.ResumoPeriodo @Inicio DATETIME2(3), @Fim DATETIME2(3)
AS
    SELECT COUNT_BIG(*) AS Quantidade, SUM(Valor) AS Total, MAX(Descricao) AS UmaDescricao
    FROM dbo.Transacoes
    WHERE DataMovimento >= @Inicio AND DataMovimento < @Fim;
GO

-- 6a. Primeira chamada com intervalo pequeno → plano de seek fica em cache.
EXEC sys.sp_recompile N'dbo.ResumoPeriodo';
EXEC dbo.ResumoPeriodo @Inicio = '2026-03-10', @Fim = '2026-03-11';
EXEC dbo.ResumoPeriodo @Inicio = '2024-06-01', @Fim = '2026-06-01';   -- reutiliza o plano: lento
GO

-- 6b. Ordem inversa → plano de scan em cache; a chamada pequena passa a ler a tabela toda.
EXEC sys.sp_recompile N'dbo.ResumoPeriodo';
EXEC dbo.ResumoPeriodo @Inicio = '2024-06-01', @Fim = '2026-06-01';
EXEC dbo.ResumoPeriodo @Inicio = '2026-03-10', @Fim = '2026-03-11';   -- reutiliza o scan: desperdício
GO

-- 6c. Uma correção possível: recompilar a cada execução (custa CPU de compilação, plano sempre certo).
CREATE OR ALTER PROCEDURE dbo.ResumoPeriodo @Inicio DATETIME2(3), @Fim DATETIME2(3)
AS
    SELECT COUNT_BIG(*) AS Quantidade, SUM(Valor) AS Total, MAX(Descricao) AS UmaDescricao
    FROM dbo.Transacoes
    WHERE DataMovimento >= @Inicio AND DataMovimento < @Fim
    OPTION (RECOMPILE);
GO
EXEC dbo.ResumoPeriodo @Inicio = '2026-03-10', @Fim = '2026-03-11';
EXEC dbo.ResumoPeriodo @Inicio = '2024-06-01', @Fim = '2026-06-01';
GO
-- Alternativas a discutir: OPTIMIZE FOR, procedimentos separados para intervalos curtos/longos,
-- Query Store para forçar um plano, ou rever os índices (INCLUDE Descricao elimina o dilema aqui).


/* -------------------------------------------------------------------------------------
   7. ESTATÍSTICAS — o otimizador decide com base nelas
   ------------------------------------------------------------------------------------- */
SELECT s.name, sp.last_updated, sp.rows, sp.rows_sampled, sp.modification_counter
FROM sys.stats s
CROSS APPLY sys.dm_db_stats_properties(s.object_id, s.stats_id) sp
WHERE s.object_id = OBJECT_ID(N'dbo.Transacoes');
GO

SET STATISTICS IO, TIME OFF;
GO
