# Laboratório de Volume — SQL Server, EF Core e grandes volumes

Objetivo: **medir** com 5 milhões de transações aquilo que o guia de estudo explica em teoria, para levar à entrevista números reais e uma história concreta.

Tempo previsto: **3 a 4 horas**. Se passar de um dia, pára: o SQL escrito à mão e as respostas ensaiadas valem mais.

> Enquadramento honesto para a entrevista: isto é um **laboratório pessoal**, não experiência de produção. Diz exatamente isso. O valor está em teres medido e saberes explicar porquê.

---

## O que foi acrescentado ao projeto

| Ficheiro | O que faz |
| --- | --- |
| `Academia.Domain/Lab/LabEntities.cs` | Clientes, Contas, Transacoes (BIGINT IDENTITY), PedidosIdempotentes, Outbox |
| `Academia.Infrastructure/Lab/LabDbContext.cs` | Contexto EF numa base separada (`AcademiaLab`), tipos de coluna explícitos |
| `Academia.Infrastructure/Lab/LabSeeder.cs` | Carga com `SqlBulkCopy` em lotes de 100 000, dados reprodutíveis (semente 42) |
| `Academia.Infrastructure/Lab/LabSql.cs` | Os 4 cenários de índices em SQL |
| `Academia.Api/Lab/LabEndpoints.cs` | Endpoints `/api/lab/*` (paginação, relatórios, idempotência, export, benchmark) |
| `Academia.Api/Lab/LabSeedJob.cs` | Seed em background: `202 Accepted` + endpoint de estado |
| `Academia.Api/Lab/OutboxPublisher.cs` | `BackgroundService` que publica o outbox (simulado com log) |
| `Academia.Api/Lab.http` | Roteiro de pedidos pela ordem deste documento |
| `lab/sql/01-medicoes.sql` | Medições guiadas com planos de execução |
| `lab/sql/02-exercicios.sql` / `03-solucoes.sql` | Os 10 exercícios do guia sobre os dados reais |
| `lab/sql/04-guid-vs-identity.sql` | Fragmentação: GUID aleatório vs sequencial vs IDENTITY |

`Program.cs` ganhou 4 linhas. O laboratório só é ativado com `Lab:Enabled = true`, que está em `appsettings.Development.json`.

---

## Arranque

1. SQL Server no Docker (o mesmo contentor da Academia, porta 3311):
   ```powershell
   docker compose -f .\backend\docker-compose.yml up -d sqlserver
   ```
2. **Compilar primeiro.** Este código foi validado com o compilador, mas sem acesso ao NuGet no ambiente onde foi escrito. Se o build no Rider falhar, corrige ou envia-me o erro.
   ```powershell
   cd backend
   dotnet build .\Academia.sln
   ```
3. Correr a API no Rider (perfil `Academia.Api`, ambiente Development). Na consola deve aparecer:
   `[LAB] Base de dados do laboratório pronta. Próximo passo: POST /api/lab/seed`
4. Abrir `Academia.Api/Lab.http` no Rider e seguir os passos.
5. Para os scripts SQL: ligar a `localhost,3311` (utilizador `sa`) na janela Database do Rider, base `AcademiaLab`.

Swagger com todos os endpoints: `http://localhost:5080/swagger` (grupo "Laboratório de volume").

---

## Roteiro e tabela de resultados

Preenche a coluna **Medido**. É esta tabela que vais citar na entrevista.

### Passo 1 — Carga em volume

`POST /api/lab/seed` → `GET /api/lab/seed/estado` até `concluido`.

| Medida | Medido |
| --- | --- |
| Tempo total do seed (5M transações) | |
| Linhas por segundo (SqlBulkCopy) | |

O que observar: o pedido responde logo com 202 e a carga corre em background. A carga remove os índices nonclustered antes, carrega em lotes com `TableLock`, e no fim volta a validar as FKs (`WITH CHECK CHECK CONSTRAINT`) e atualiza as estatísticas.

### Passo 2 — Paginação

Para cada cenário (`POST /api/lab/indices/{cenario}`), corre 2b a 2e do `Lab.http`. Usa o `tempoMs` da resposta (segunda execução). Para os logical reads, corre a secção 4 do `01-medicoes.sql`.

| Pedido | nenhum | conta-data | cobertura |
| --- | --- | --- | --- |
| Tempo de criação dos índices (s) | — | | |
| OFFSET página 1 (ms) | | | |
| OFFSET página 50 000 (ms) | | | |
| Keyset primeira página (ms) | | | |
| Keyset posição profunda (ms) | | | |
| Keyset conta 1, sem / com Descricao (ms) | | | |

Esperado: sem índice tudo é lento (scan + sort de 5M linhas). O cenário conta-data só ajuda os pedidos por conta (2f, 2g); as listagens globais continuam sem índice útil. Com cobertura, o keyset fica constante e o OFFSET profundo continua caro, porque lê e descarta 2,5 milhões de entradas do índice. Cada resposta traz o campo `sqlGerado`: lê o SQL que o EF Core produziu para `Skip/Take` e para o keyset.

### Passo 3 — Relatórios

| Pedido | cobertura | columnstore |
| --- | --- | --- |
| Mensal SARGable (ms) | | |
| Mensal não SARGable (ms) | | |
| Top contas (ms) | | |

Esperado: no cenário cobertura, o SARGable faz seek no índice por data e o não SARGable faz scan. Com columnstore, ambos ficam muito mais rápidos (batch mode, compressão), e a diferença entre eles quase desaparece porque o columnstore lê tudo depressa. Confirma com a secção 5 do `01-medicoes.sql` (`IGNORE_NONCLUSTERED_COLUMNSTORE_INDEX`).

### Passo 4 — Escrita

`POST /api/lab/benchmark/insercao?linhas=20000&linhasLinhaALinha=1000`

| Método | Linhas/s medidas |
| --- | --- |
| SaveChanges por linha | |
| AddRange + SaveChanges | |
| SqlBulkCopy | |

### Passo 5 — Idempotência e outbox

Corre 5a a 5e. Confirma:
- [ ] 5a devolve 201 e um Id.
- [ ] 5b devolve 200, o **mesmo** Id e o header `Idempotent-Replayed: true`.
- [ ] 5c devolve 422.
- [ ] Na consola aparece `[OUTBOX] Publicado TransacaoCriada #...` só **uma** vez.

Lê `CriarTransacao` em `LabEndpoints.cs`: transação curta com dado + chave + evento; corrida resolvida pela PK de `PedidosIdempotentes` (erro 2627 → rollback → devolve o vencedor).

### Passo 6 — Exportação em streaming

`GET /api/lab/exportar?ano=2025` (~1,8 milhões de linhas). Na consola: tempo e memória gerida. A memória deve ficar estável, porque nada é carregado inteiro com `ToList()`. Se o Rider tiver dificuldade com uma resposta tão grande, usa:
```powershell
curl.exe -o transacoes-2025.csv "http://localhost:5080/api/lab/exportar?ano=2025"
```

| Medida | Medido |
| --- | --- |
| Linhas exportadas / segundos | |
| Memória gerida no fim (MB) | |

### Passo 7 — SQL à mão

1. `01-medicoes.sql` secções 1, 2, 3, 6 (key lookup, SARGability, conversão implícita, parameter sniffing).
2. `04-guid-vs-identity.sql`: fragmentação das três tabelas.
3. `02-exercicios.sql`: os 10 exercícios, escritos à mão primeiro. Depois compara com `03-solucoes.sql`.

| Script | Resultado a anotar | Medido |
| --- | --- | --- |
| 04 GUID aleatório | Fragmentação % / páginas | |
| 04 IDENTITY | Fragmentação % / páginas | |
| 01 §6 sniffing | Logical reads da chamada "errada" vs com RECOMPILE | |

---

## Três problemas no código da Academia que agora sabes explicar

1. **`Guid.NewGuid()` como chave primária clustered** (todas as entidades da Academia). Mede-o com o script 04. Correção: `BIGINT IDENTITY` clustered, ou GUID sequencial (`NEWSEQUENTIALID()`, `Guid.CreateVersion7()` no .NET 9).
2. **`/api/learning/snapshot` faz 4 queries sequenciais**, e não se resolve com `Task.WhenAll` no mesmo `DbContext`: o contexto não é thread-safe e o EF lança `InvalidOperationException`. As opções são uma única query, contextos separados via `IDbContextFactory`, ou aceitar o sequencial (4 queries pequenas com índice custam poucos ms).
3. **`EnsureCreated` em vez de migrations.** Não gere alterações de schema. Em produção usam-se migrations versionadas e aplicadas num passo de deploy controlado.

---

## Como contar isto na entrevista (preenche com os teus números)

> "Para preparar esta vaga montei um laboratório pessoal em .NET 8 e SQL Server com 5 milhões de transações. Carreguei os dados com SqlBulkCopy a cerca de ___ linhas por segundo, contra ___ com SaveChanges linha a linha. Comparei paginação: com OFFSET, a página 50 000 demorava ___ ms; com keyset e um índice de cobertura na ordem da navegação, qualquer página ficava em ___ ms. Num relatório mensal, o columnstore baixou de ___ para ___ ms. Também medi o efeito de uma função sobre a coluna no WHERE, de uma conversão implícita VARCHAR/NVARCHAR e de parameter sniffing numa stored procedure. Não é produção, mas agora sei ler o plano de execução e explicar cada decisão de índice com números."

Se perguntarem algo que o lab não cobre (Kafka, CDC), não estiques o lab. Usa as respostas da Camada 4 do guia.
