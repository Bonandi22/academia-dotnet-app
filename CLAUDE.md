# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Project context

Academia de estudo para preparar entrevistas técnicas Fullstack/.NET: guias, quiz, flashcards e progresso pessoal. É um projeto de aprendizagem e de portefólio — não tem utilizadores reais nem dados sensíveis, mas é código mostrado em entrevista, por isso vale mais ser correto e explicável do que extenso.

Todo o conteúdo visível ao utilizador (templates, rotas, mensagens de erro da API, comentários, documentação) está em **português de Portugal**. Manter esse idioma ao acrescentar código ou conteúdo.

## Guardrails

**Qualidade** — antes de dar uma tarefa como terminada, apresentar o diff (ou resumo, se grande) e os riscos: o que pode partir, o que não foi testado. `npm test` e `dotnet build` não chegam para mudanças com comportamento em runtime — mudanças de UI confirmam-se a correr a app no browser, mudanças de endpoint confirmam-se com `dotnet test` (e pelo Swagger ou pelos ficheiros `.http` quando o teste não cobre o caso).

**Segurança** — a password de SQL Server e a chave JWT em `appsettings.json`, `appsettings.Development.json` e nos dois `docker-compose.yml` são **deliberadamente locais e já versionadas**; nunca as reutilizar fora deste ambiente e nunca acrescentar segredos reais ao repositório. Configuração sensível vai por variáveis de ambiente (`$env:Jwt__Secret`, `$env:ConnectionStrings__Academia`) ou `appsettings.Local.json`/`.env`, ambos git-ignored.

**Custo** — no final de qualquer tarefa que use loops, subagents/forks ou várias iterações de exploração, estimar o esforço gasto (nº de agentes, builds/testes repetidos) e sugerir onde reduzir da próxima vez.

## Comandos

### Frontend (raiz do repositório)

```powershell
npm install
npm start          # ng serve -> http://localhost:4200
npm run build      # ng build (configuração production por omissão)
npm test           # node --test sobre tests/*.test.mjs
```

Correr um único ficheiro ou teste:

```powershell
node --test tests/project-smoke.test.mjs
node --test --test-name-pattern "rotas"
```

### Backend (`backend/`)

```powershell
cd backend
dotnet restore .\Academia.sln
dotnet build .\Academia.sln
dotnet run --project .\Academia.Api\Academia.Api.csproj --launch-profile Academia.Api
```

API em `http://localhost:5080`, Swagger em `/swagger`, health check em `/health`. `backend/Academia.Api/Academia.Api.http` e `Lab.http` servem para exercitar os endpoints à mão.

### Testes de integração (`backend/Academia.Api.Tests`)

```powershell
dotnet test .\backend\Academia.sln                                      # precisa do SQL Server na 3311
dotnet test .\backend\Academia.sln --filter "FullyQualifiedName~LearningEndpointsTests"
```

xUnit + `WebApplicationFactory<Program>` em ambiente `Testing` (sem Lab nem Swagger). `ApiFactory` cria uma base `AcademiaTests_<guid>` por execução através das migrations e apaga-a no fim; outra instância SQL vai por `ACADEMIA_TEST_SQL`. Sem SQL Server, falham logo com "Base de testes indisponível". Endpoint novo ou regra de validação nova em `/api/learning` leva teste aqui — em especial o isolamento entre utilizadores.

### SQL Server e Docker

```powershell
docker compose -f .\backend\docker-compose.yml up -d sqlserver   # só a BD, API no Rider
docker compose up --build                                        # stack completa (raiz)
docker compose down -v                                           # apaga os dados SQL locais
```

As duas composições publicam a porta **3311** e não podem correr em simultâneo. O SQL Server escuta em `1433` dentro do contentor e é publicado em `3311` no Windows (3308–3310 pertencem a outros projetos do utilizador).

### Lint

Não há lint configurado (sem ESLint, sem analyzers extra nem `.editorconfig`). O mais próximo:

```powershell
npx tsc --noEmit -p tsconfig.app.json    # tipos estritos + strictTemplates
dotnet build .\backend\Academia.sln      # warnings de nullable reference (CS86xx)
```

### Git

Repositório privado em `github.com/Bonandi22/academia-dotnet-app`, branch principal `main`, sem CI configurado. O `gh` está em `C:\tools\gh\bin\gh.exe` (fora do PATH); em Git Bash invocar como `"/c/tools/gh/bin/gh.exe"`. Não commitar nem fazer push sem pedido explícito do utilizador.

Atenção: existe um repositório git acidental com raiz em `C:\Users\elker` (toda a pasta pessoal, 1 commit, sem remote). Este projeto tem `.git` próprio e não é afetado, mas comandos git corridos a partir da home ou de outra pasta sem repo próprio vão parar lá.

## Arquitetura

Monólito modular num só repositório: frontend Angular na raiz (`src/`), API ASP.NET Core 8 em `backend/` dividida em `Academia.Domain` → `Academia.Application` → `Academia.Infrastructure` → `Academia.Api`. RabbitMQ e microserviços são **matéria de estudo**, não dependências de runtime.

### O conteúdo de estudo não está na base de dados

`src/app/data/study-data.ts` (catálogo de módulos, perguntas de quiz, flashcards) e `src/app/data/study-content.ts` (guias por módulo) são TypeScript versionado com o frontend. A base de dados guarda **apenas o progresso por utilizador**. Acrescentar um módulo significa editar estes ficheiros, não migrar schema.

`tests/project-smoke.test.mjs` valida esta ligação por análise textual dos ficheiros: cada `id` em `STUDY_MODULES` tem de ter um `moduleId` correspondente em `STUDY_LESSONS`, e `app.routes.ts` tem de conter literais exatos (`path: 'modulos'`, etc.). Renomear uma rota ou um id de módulo parte estes testes mesmo que a aplicação compile.

### Progresso local-first, sincronização opcional

`ProgressService` (`src/app/core/progress.service.ts`) é a fonte de verdade no cliente: estado em signals, persistido em `localStorage` sob `academia-dotnet-progress-v1`. A aplicação funciona inteiramente sem conta nem API.

`LearningSyncService` só corre quando o utilizador sincroniza: faz **push** (deleções de revisão, aulas desmarcadas e concluídas, revisões, tentativas de quiz ainda não marcadas, flashcards), depois **pull** de `/api/learning/snapshot`, e termina em `ProgressService.mergeSnapshot()`, que une os dois lados. A união depende destes mecanismos, que têm de ser preservados ao mexer aqui:

- `deletedReviewIds` e `uncompletedLessons` — tombstones locais; sem eles o pull ressuscita revisões apagadas e aulas desmarcadas. Depois do push limpam-se só as chaves enviadas (`clearDeletedReviewIds(sent)`, `clearUncompletedLessons(sent)`), não as criadas durante a sincronização.
- Dono do progresso local (`academia-dotnet-progress-owner-v1`) — `ProgressService.claimFor(userId)` no início do sync. Progresso anónimo é absorvido pela conta; progresso de **outra** conta é descartado para nunca ser enviado para a conta errada.
- Id de tentativa de quiz (UUID gerado no cliente em `recordQuiz`) — `POST /quiz-attempts` é idempotente por `(UserId, ClientAttemptId)`: reenviar devolve 200 com a tentativa já gravada, por isso o sync envia sempre todas as tentativas locais e `mergeSnapshot` deduplica por `id`. A data enviada é a do quiz, não a da sincronização (datas no futuro passam a "agora"). Substitui a antiga assinatura `data|score|…` e a janela de 120 s.

### Formato das chaves

- Aula concluída: `` `${moduleId}::${section.id}` `` (ex.: `csharp::async-await`). Cada `LessonSection` tem um `id` estável, único no módulo — reordenar ou inserir secções não mexe no progresso; **mudar um `id` existente sim**. O cliente divide com `lastIndexOf('::')` antes de enviar `moduleId`/`lessonId` separados. Qualquer mudança neste formato tem de ser feita em `ProgressService.lessonKey` e em `LearningSyncService.sync()` ao mesmo tempo.
- Item de revisão: prefixado por origem — `lesson:${moduleId}:${section.id}`, `quiz:${id}`, `flashcard:${id}`. Avaliar um flashcard como `again` cria a revisão; `good`/`easy` resolve-a.
- Chaves antigas por índice (`csharp::0`, `lesson:csharp:0`) são convertidas uma vez: no cliente por `legacy-lesson-keys.ts` (em `ProgressService.load()`), no servidor pela migration `MigrateLessonKeysToSectionIds`. As duas tabelas estão congeladas e um teste garante que coincidem — não as derivar do conteúdo atual.

### API

`backend/Academia.Api/Program.cs` contém toda a superfície da API principal como Minimal API num único ficheiro (auth + learning). É uma escolha deliberada de escala: as camadas `Domain`/`Application`/`Infrastructure` existem e devem continuar a ser respeitadas (entidades e DbContext não sobem para a API, contratos não descem para o Domain), mas não há serviços de aplicação a mais só por simetria.

Todos os endpoints de `/api/learning` exigem JWT e filtram por `UserId` retirado da claim `NameIdentifier` — nunca aceitam um id de utilizador vindo do corpo do pedido. O arranque falha com exceção se `Jwt:Secret` tiver menos de 32 bytes.

### Migrations EF Core

O schema de `AcademiaDbContext` é versionado em `backend/Academia.Infrastructure/Persistence/Migrations` e aplicado com `MigrateAsync()` no arranque da API. Alterar entidades ou `OnModelCreating` exige uma migration nova (o `dotnet-ef` está fixado em `.config/dotnet-tools.json`; correr `dotnet tool restore` uma vez):

```powershell
dotnet ef migrations add NomeDaMudanca --context AcademiaDbContext --project backend/Academia.Infrastructure --startup-project backend/Academia.Api --output-dir Persistence/Migrations
```

Rever o `.cs` gerado antes de commitar. Uma base criada antes das migrations (pelo antigo `EnsureCreated`) não tem `__EFMigrationsHistory` e faz falhar o arranque: largar só a base `AcademiaDotNet` e deixar a API recriá-la. O `LabDbContext` continua com `EnsureCreated` de propósito — é um laboratório descartável, fora das migrations.

### Módulo Lab

`/api/lab/*` é um laboratório de volume (5M de transações) com `LabDbContext` próprio sobre a base **separada** `AcademiaLab`, mais um `OutboxPublisher` como `BackgroundService`. É registado apenas quando `Lab:Enabled = true` (definido em `appsettings.Development.json`); fora disso nada é injetado nem mapeado. Roteiro em `backend/lab/LAB.md`, pedidos prontos em `backend/Academia.Api/Lab.http`, scripts em `backend/lab/sql/`. Não misturar entidades do Lab com `AcademiaDbContext`.

## Convenções de código

### Backend

- **`DateTimeOffset.UtcNow`, nunca `DateTime.Now`** — é o tipo usado em todas as entidades.
- **Async de ponta a ponta** — nunca bloquear com `.Result`/`.Wait()`.
- **Consultas só-leitura usam `AsNoTracking()`**; preferir `Select` para projeções quando não é precisa a entidade completa.
- **Nunca engolir `Exception` silenciosamente** — registar via `ILogger` antes de tratar ou relançar (ver o `try/catch` do `MigrateAsync` como referência).
- Validação de entrada acontece no endpoint e devolve `ValidationProblem`/`BadRequest` com mensagem em português; comprimentos máximos têm de coincidir com os `HasMaxLength` do `AcademiaDbContext`.

### Frontend

- Angular 22 standalone, sem NgModules. Rotas sempre com `loadComponent` e `title`.
- Componentes com **template inline** e sem `styles`/`styleUrls`: todo o CSS vive em `src/styles.css` e é partilhado por classes (`.button`, `.page-heading`, `.quiz-card`, …). Reutilizar classes existentes antes de criar novas.
- Estado com signals/`computed`; sintaxe de controlo `@if`/`@for`.
- Subscrições novas usam `takeUntilDestroyed(this.destroyRef)` (`inject(DestroyRef)`, operador de `@angular/core/rxjs-interop`) em vez de `ngOnDestroy` manual. Os dois `.subscribe()` existentes (`account.component.ts`, `study.component.ts`) ainda não seguem isto; esta regra fixa o padrão daqui para a frente, sem obrigação de migrar já.
- O token JWT é injetado pelo `authInterceptor` funcional registado em `main.ts`; o URL da API vem de `src/environments/environment.ts` (`apiBaseUrl`), que tem de ficar coerente com `AllowedOrigins` da API.

## Regras de ouro — nunca fazer

- **Nunca adicionar um endpoint em `/api/learning` sem `RequireAuthorization` e sem filtrar por `GetUserId(principal)`** — o grupo já exige autorização, mas o filtro por utilizador é por query e esquecê-lo expõe dados de outras contas.
- **Nunca aceitar um `userId` vindo do corpo ou da query**; a identidade vem sempre da claim do JWT.
- **Nunca alterar o formato das chaves de aula/revisão** sem atualizar cliente e servidor na mesma mudança — dados já guardados em `localStorage` e em SQL ficam órfãos.
- **Nunca alterar entidades EF sem gerar a migration correspondente** — nem editar uma migration já commitada; corrige-se com uma migration nova.
- **Nunca acrescentar um módulo a `STUDY_MODULES` sem a lição correspondente em `STUDY_LESSONS`** — `npm test` falha e o catálogo fica com uma entrada sem guia.
- **Nunca pôr segredos reais no repositório**, nem reutilizar as credenciais de desenvolvimento fora deste ambiente local.
- **Nunca declarar uma tarefa concluída** sem o diff revisto pelo utilizador e sem a verificação apropriada ao tipo de mudança (ver Guardrails).
