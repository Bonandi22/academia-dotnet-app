/* =====================================================================================
   LAB 02 — EXERCÍCIOS (os mesmos 10 do guia de estudo, agora sobre 5 milhões de linhas reais)

   Modelo:
     dbo.Clientes   (Id, Nome, Pais)                         ~10 000
     dbo.Contas     (Id, Numero, ClienteId, DataAbertura)     ~50 000
     dbo.Transacoes (Id, ContaId, DataMovimento, Valor, Tipo, Descricao)   ~5 000 000
       - Valor: crédito positivo; débito, pagamento e transferência negativos
       - Tipo:  'CREDITO' | 'DEBITO' | 'PAGAMENTO' | 'TRANSFERENCIA'   (VARCHAR!)
       - Dados de 2024-01-01 a 2026-10-15
       - As contas 1 a 500 concentram ~30% das transações (contas "quentes")
       - Existem ~0,1% de transações duplicadas de propósito (exercício 7)

   Regras:
     - Escreve primeiro À MÃO (papel ou editor sem autocomplete). Só depois corre.
     - Para cada query: anota logical reads e o operador principal do plano.
     - Só abre 03-solucoes.sql depois de tentares.
   ===================================================================================== */

USE AcademiaLab;
GO
SET STATISTICS IO, TIME ON;
GO

-- 1. Saldo total (soma de Valor) por cliente em 2026, só clientes com saldo acima de 50 000.
--    Mostra Id, Nome e Saldo, do maior para o menor. Filtro de datas SARGable.



-- 2. As 3 transações de maior valor absoluto de cada conta (ROW_NUMBER).
--    Limita às contas 1 a 100 para o resultado ser legível.



-- 3. Saldo acumulado da conta 42 ao longo do tempo (SUM() OVER com ROWS).
--    Colunas: DataMovimento, Valor, SaldoAcumulado.



-- 4. Para a conta 42: variação de Valor entre cada transação e a anterior (LAG).
--    Colunas: DataMovimento, Valor, ValorAnterior, Variacao.



-- 5. Contas sem nenhuma transação nos últimos 90 dias (referência: 2026-10-16). Usa NOT EXISTS.
--    Bónus: porque é que NOT IN pode devolver zero linhas?



-- 6. Total mensal por tipo de transação em 2026 (meses em linhas). GROUP BY + intervalo SARGable.



-- 7. Encontrar as transações duplicadas (mesma conta, data, valor e tipo) e apagar todas menos uma.
--    CTE + ROW_NUMBER + DELETE. Corre dentro de BEGIN TRAN ... ROLLBACK para não estragar o lab.



-- 8. Que índice criarias para o exercício 3? Escreve o CREATE INDEX e justifica a ordem das colunas.
--    Cria-o, volta a correr o exercício 3 e compara logical reads. Depois apaga-o.



-- 9. Reescreve esta query OFFSET como keyset pagination (próxima página depois da última linha vista):
--      SELECT Id, DataMovimento, Valor FROM dbo.Transacoes WHERE ContaId = 7
--      ORDER BY DataMovimento DESC, Id DESC OFFSET 100 ROWS FETCH NEXT 50 ROWS ONLY;



-- 10. (Sem código) Como transformarias o exercício 6 num dashboard que responde em milissegundos
--     com 500 milhões de linhas? Escreve 4–5 linhas de resposta como se fosse a entrevista.
/*

*/
