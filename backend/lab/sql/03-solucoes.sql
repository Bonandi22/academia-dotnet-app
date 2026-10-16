/* =====================================================================================
   LAB 03 — SOLUÇÕES COMENTADAS
   Há várias soluções corretas; estas privilegiam a forma que deves saber explicar na entrevista.
   ===================================================================================== */

USE AcademiaLab;
GO
SET STATISTICS IO, TIME ON;
GO

-- 1. Saldo por cliente em 2026 acima de 50 000
--    WHERE filtra linhas ANTES de agrupar; HAVING filtra grupos DEPOIS.
--    Intervalo semiaberto [2026-01-01, 2027-01-01): SARGable e sem problemas de horas/milissegundos.
SELECT cl.Id, cl.Nome, SUM(t.Valor) AS Saldo
FROM dbo.Transacoes AS t
JOIN dbo.Contas     AS c  ON c.Id  = t.ContaId
JOIN dbo.Clientes   AS cl ON cl.Id = c.ClienteId
WHERE t.DataMovimento >= '2026-01-01' AND t.DataMovimento < '2027-01-01'
GROUP BY cl.Id, cl.Nome
HAVING SUM(t.Valor) > 50000
ORDER BY Saldo DESC;
GO

-- 2. Top 3 por conta
--    ROW_NUMBER numera dentro de cada partição; o filtro rn <= 3 tem de ficar fora (CTE),
--    porque window functions não podem aparecer no WHERE.
--    ROW_NUMBER desempata arbitrariamente; RANK/DENSE_RANK manteriam empates.
WITH Ordenadas AS (
    SELECT t.ContaId, t.Id, t.DataMovimento, t.Valor, t.Tipo,
           ROW_NUMBER() OVER (PARTITION BY t.ContaId ORDER BY ABS(t.Valor) DESC, t.Id) AS rn
    FROM dbo.Transacoes AS t
    WHERE t.ContaId BETWEEN 1 AND 100
)
SELECT ContaId, Id, DataMovimento, Valor, Tipo, rn
FROM Ordenadas
WHERE rn <= 3
ORDER BY ContaId, rn;
GO

-- 3. Saldo acumulado
--    ROWS BETWEEN UNBOUNDED PRECEDING AND CURRENT ROW explícito: o padrão sem ROWS é RANGE,
--    que trata empates na ordenação como uma só "linha" e é mais lento (spool em disco).
--    O Id no ORDER BY torna a ordem determinística quando há duas transações no mesmo milissegundo.
SELECT t.DataMovimento, t.Valor,
       SUM(t.Valor) OVER (ORDER BY t.DataMovimento, t.Id
                          ROWS BETWEEN UNBOUNDED PRECEDING AND CURRENT ROW) AS SaldoAcumulado
FROM dbo.Transacoes AS t
WHERE t.ContaId = 42
ORDER BY t.DataMovimento, t.Id;
GO

-- 4. Variação face à transação anterior
--    LAG devolve NULL na primeira linha; NULL em aritmética dá NULL (correto: não há variação).
SELECT t.DataMovimento, t.Valor,
       LAG(t.Valor) OVER (ORDER BY t.DataMovimento, t.Id) AS ValorAnterior,
       t.Valor - LAG(t.Valor) OVER (ORDER BY t.DataMovimento, t.Id) AS Variacao
FROM dbo.Transacoes AS t
WHERE t.ContaId = 42
ORDER BY t.DataMovimento, t.Id;
GO

-- 5. Contas inativas há 90 dias
--    NOT EXISTS pára na primeira transação encontrada por conta (anti semi join).
--    NOT IN: se a subquery devolver um único NULL, "x NOT IN (..., NULL)" é UNKNOWN para todas as linhas → zero resultados.
SELECT c.Id, c.Numero
FROM dbo.Contas AS c
WHERE NOT EXISTS (
    SELECT 1
    FROM dbo.Transacoes AS t
    WHERE t.ContaId = c.Id
      AND t.DataMovimento >= DATEADD(DAY, -90, CAST('2026-10-16' AS DATETIME2(3)))
);
GO

-- 6. Total mensal por tipo em 2026
--    YEAR()/MONTH() no SELECT e GROUP BY não fazem mal: o que tem de ser SARGable é o WHERE.
SELECT YEAR(t.DataMovimento) AS Ano, MONTH(t.DataMovimento) AS Mes, t.Tipo,
       COUNT_BIG(*) AS Quantidade, SUM(t.Valor) AS Total
FROM dbo.Transacoes AS t
WHERE t.DataMovimento >= '2026-01-01' AND t.DataMovimento < '2027-01-01'
GROUP BY YEAR(t.DataMovimento), MONTH(t.DataMovimento), t.Tipo
ORDER BY Ano, Mes, t.Tipo;
GO

-- 7. Duplicados: identificar e apagar mantendo o de menor Id
--    No SQL Server é possível fazer DELETE diretamente sobre a CTE: apaga na tabela base.
BEGIN TRAN;

WITH Duplicados AS (
    SELECT t.Id,
           ROW_NUMBER() OVER (PARTITION BY t.ContaId, t.DataMovimento, t.Valor, t.Tipo ORDER BY t.Id) AS rn
    FROM dbo.Transacoes AS t
)
DELETE FROM Duplicados
WHERE rn > 1;

SELECT @@ROWCOUNT AS LinhasApagadas;   -- ~5 000 com o seed por omissão

ROLLBACK;   -- troca por COMMIT só se quiseres mesmo limpar
GO

-- 8. Índice para o exercício 3
--    Igualdade primeiro (ContaId), depois a ordem pedida (DataMovimento, Id): o índice entrega as linhas
--    já ordenadas → sem Sort. INCLUDE (Valor): a única coluna só lida → sem Key Lookup.
CREATE NONCLUSTERED INDEX IX_Lab_Ex8 ON dbo.Transacoes (ContaId, DataMovimento, Id) INCLUDE (Valor);
GO
-- (volta a correr o exercício 3 aqui e compara logical reads)
DROP INDEX IX_Lab_Ex8 ON dbo.Transacoes;
GO

-- 9. OFFSET → keyset
--    Em vez de "salta 100 linhas", diz "dá-me as que vêm depois da última que vi".
--    Os valores do cursor vêm da última linha da página anterior (o endpoint devolve-os em proximoCursor).
--    Índice ideal: (ContaId, DataMovimento DESC, Id DESC) — existe no cenário "cobertura".
DECLARE @ultimaData DATETIME2(3) = (SELECT TOP (1) DataMovimento FROM dbo.Transacoes WHERE ContaId = 7 ORDER BY DataMovimento DESC, Id DESC);
DECLARE @ultimoId   BIGINT       = (SELECT TOP (1) Id            FROM dbo.Transacoes WHERE ContaId = 7 ORDER BY DataMovimento DESC, Id DESC);

SELECT TOP (50) Id, DataMovimento, Valor
FROM dbo.Transacoes
WHERE ContaId = 7
  AND (DataMovimento < @ultimaData OR (DataMovimento = @ultimaData AND Id < @ultimoId))
ORDER BY DataMovimento DESC, Id DESC;
GO

-- 10. Dashboard sobre 500 milhões de linhas (resposta modelo)
/*
   "Não agregaria 500 milhões de linhas a cada pedido. Mantinha uma tabela de resumo
   ResumoMensal(Ano, Mes, Tipo, Quantidade, Total) atualizada incrementalmente — por um job
   noturno ou por consumidores de eventos — e o dashboard lê essas poucas centenas de linhas.
   Para análises ad hoc, columnstore na tabela de factos, particionada por mês, para que
   cada query só leia as partições relevantes em batch mode. Por cima, cache com TTL curto.
   O preço é consistência eventual: o dashboard pode estar alguns minutos atrás, e isso tem de
   ser aceite pelo negócio."
*/

SET STATISTICS IO, TIME OFF;
GO
