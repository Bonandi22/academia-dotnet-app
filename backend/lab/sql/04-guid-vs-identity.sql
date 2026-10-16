/* =====================================================================================
   LAB 04 — GUID ALEATÓRIO vs BIGINT IDENTITY COMO CHAVE CLUSTERED
   Ligação direta ao código da Academia: as entidades usam Guid.NewGuid() como PK,
   e no SQL Server a PK é clustered por omissão.

   Pergunta de entrevista: "Porque é que um GUID aleatório como clustered key é mau em tabelas grandes?"
   Resposta: cada insert cai num ponto aleatório do índice → page splits, páginas meio vazias,
   fragmentação, mais I/O e mais log. Uma chave crescente acrescenta sempre no fim.
   Correções: BIGINT IDENTITY clustered (+ GUID como coluna única nonclustered, se precisares de ID público),
   NEWSEQUENTIALID(), ou GUID v7 ordenado por tempo (Guid.CreateVersion7() no .NET 9).
   ===================================================================================== */

USE AcademiaLab;
GO
DROP TABLE IF EXISTS dbo.Lab_PkGuid;
DROP TABLE IF EXISTS dbo.Lab_PkIdentity;
DROP TABLE IF EXISTS dbo.Lab_PkSequencial;
GO
CREATE TABLE dbo.Lab_PkGuid       (Id UNIQUEIDENTIFIER NOT NULL DEFAULT NEWID()           PRIMARY KEY CLUSTERED, Dados CHAR(100) NOT NULL);
CREATE TABLE dbo.Lab_PkSequencial (Id UNIQUEIDENTIFIER NOT NULL DEFAULT NEWSEQUENTIALID() PRIMARY KEY CLUSTERED, Dados CHAR(100) NOT NULL);
CREATE TABLE dbo.Lab_PkIdentity   (Id BIGINT IDENTITY(1, 1) NOT NULL                       PRIMARY KEY CLUSTERED, Dados CHAR(100) NOT NULL);
GO

-- 300 000 inserts em lotes de 1 000 (simula escrita contínua de uma aplicação, não uma carga ordenada).
SET NOCOUNT ON;
DECLARE @i INT = 0;
WHILE @i < 300
BEGIN
    INSERT INTO dbo.Lab_PkGuid (Dados)       SELECT TOP (1000) 'x' FROM sys.all_objects a CROSS JOIN sys.all_objects b;
    INSERT INTO dbo.Lab_PkSequencial (Dados) SELECT TOP (1000) 'x' FROM sys.all_objects a CROSS JOIN sys.all_objects b;
    INSERT INTO dbo.Lab_PkIdentity (Dados)   SELECT TOP (1000) 'x' FROM sys.all_objects a CROSS JOIN sys.all_objects b;
    SET @i += 1;
END;
GO

-- Compara: fragmentação, número de páginas e ocupação média das páginas.
SELECT OBJECT_NAME(ps.object_id) AS Tabela,
       ps.page_count AS Paginas,
       CAST(ps.avg_fragmentation_in_percent AS DECIMAL(5, 1)) AS FragmentacaoPct,
       CAST(ps.avg_page_space_used_in_percent AS DECIMAL(5, 1)) AS OcupacaoPaginaPct
FROM sys.dm_db_index_physical_stats(DB_ID(), NULL, NULL, NULL, 'DETAILED') AS ps
WHERE ps.object_id IN (OBJECT_ID(N'dbo.Lab_PkGuid'), OBJECT_ID(N'dbo.Lab_PkSequencial'), OBJECT_ID(N'dbo.Lab_PkIdentity'))
  AND ps.index_level = 0
ORDER BY FragmentacaoPct DESC;
GO

-- Limpeza
-- DROP TABLE dbo.Lab_PkGuid; DROP TABLE dbo.Lab_PkSequencial; DROP TABLE dbo.Lab_PkIdentity;
