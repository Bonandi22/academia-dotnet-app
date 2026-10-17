# Academia .NET para Entrevistas

Aplicação de aprendizagem para preparar entrevistas técnicas Fullstack/.NET. Esta entrega reúne o frontend Angular e uma API ASP.NET Core com persistência SQL Server, organizada como monólito modular em projetos separados.

## Funcionalidades

- Dashboard, catálogo e pesquisa de módulos, guias de estudo e navegação por aulas.
- Conteúdo inicial sobre C#/.NET, ASP.NET Core, EF Core/SQL Server, arquitetura, RabbitMQ, Angular/RxJS, React/TypeScript e qualidade/DevOps.
- Exemplos de código, perguntas de entrevista com respostas e exercícios comentados.
- Quiz técnico com pontuação, explicações e histórico.
- Flashcards com avaliação «Preciso rever», «Sei explicar» e «Domino».
- Progresso local persistente: aulas concluídas, revisão manual, resultados de quiz e avaliações de flashcards.
- Conta de utilizador: registo, login com JWT e sincronização do progresso com a API.
- API com health check e documentação Swagger em ambiente de desenvolvimento.

## Arquitetura

```text
src/                         Angular standalone + TypeScript
backend/
  Academia.Domain/           Entidades e regras centrais
  Academia.Application/      Contratos de aplicação
  Academia.Infrastructure/   EF Core, SQL Server e persistência
  Academia.Api/              ASP.NET Core Minimal API, JWT e Swagger
```

É um **monólito modular**, não um conjunto de microserviços. RabbitMQ é matéria de estudo da academia, não uma dependência de runtime da própria plataforma.

## Laboratório de volume (preparação SQL / grandes volumes)

Módulo `/api/lab` com uma base separada (`AcademiaLab`) e 5 milhões de transações para medir paginação OFFSET vs keyset, cenários de índices (cobertura, columnstore), SARGability, conversões implícitas, parameter sniffing, `SqlBulkCopy`, idempotência com outbox e exportação em streaming. Ativo apenas em Development (`Lab:Enabled`). Roteiro completo e tabela de resultados em [`backend/lab/LAB.md`](backend/lab/LAB.md); pedidos prontos em `backend/Academia.Api/Lab.http`.

## Requisitos

- Node.js 22.15+ recomendado para a versão Angular configurada.
- npm.
- .NET SDK 8.
- Docker Desktop (recomendado para executar SQL Server localmente) ou uma instância SQL Server 2022.

## Executar tudo com Docker Compose

Na raiz do repositório, com Docker Desktop iniciado. Esta opção executa a API dentro da rede Docker; nesse caso a API liga-se ao host `sqlserver:1433`, enquanto a porta publicada no Windows é `3311`. Não executes simultaneamente esta composição e a composição do backend, porque ambas publicam a porta 3311.

Na primeira vez, cria o `.env` (git-ignored) com uma chave JWT própria — a API corre em `Production` e recusa arrancar com a chave versionada nos `appsettings*.json`:

```powershell
Copy-Item .env.example .env
$key = [Convert]::ToBase64String([Security.Cryptography.RandomNumberGenerator]::GetBytes(48))
(Get-Content .env) -replace '^JWT_SECRET=.*', "JWT_SECRET=$key" | Set-Content .env
docker compose up --build
```

Depois abre `http://localhost:4200`. Esta composição simula produção: o frontend é o build de produção servido por nginx, que também encaminha `/api` para a API (mesma origem, sem CORS); a API corre em `Production`, sem Swagger nem Lab, e **não publica porta própria** — usa `http://localhost:4200/api/...` (ex.: `http://localhost:4200/api/auth/login`). Para Swagger e Lab, corre a API fora do Docker (secção seguinte). O primeiro build precisa de acesso à internet para obter imagens e pacotes. Não publiques as passwords e a chave JWT incluídas.

Para parar os serviços, usa `Ctrl+C` e depois `docker compose down`. Para apagar também os dados SQL locais, usa `docker compose down -v` (esta operação elimina os dados da base de dados).

## Executar apenas a base de dados no Docker e a API no Rider

A Academia usa a porta **3311** no Windows para não conflitar com os teus outros contentores SQL Server (portas 3308, 3309 e 3310). O SQL Server continua a escutar na porta 1433 dentro do contentor.

A partir da raiz do repositório, se fores executar a API localmente no Rider, usa apenas o Compose do backend:

```powershell
docker compose -f .\backend\docker-compose.yml up -d sqlserver
```

A connection string em `backend/Academia.Api/appsettings.json` está configurada para `localhost,3311`. O container usa uma password **apenas para desenvolvimento local** definida no Compose. Não reutilizes esta password em ambientes partilhados ou de produção.

## Executar a API

Abre um segundo terminal:

```powershell
cd backend
dotnet restore .\Academia.sln
dotnet run --project .\Academia.Api\Academia.Api.csproj --launch-profile Academia.Api
```

- API: `http://localhost:5080`
- Swagger: `http://localhost:5080/swagger`
- Health check: `http://localhost:5080/health`

No arranque, a API aplica as migrations EF Core pendentes (`backend/Academia.Infrastructure/Persistence/Migrations`) com `MigrateAsync`. Em produção, prefere gerar um script idempotente (`dotnet ef migrations script --idempotent`) e aplicá-lo num passo de deploy controlado. Se tiveres uma base `AcademiaDotNet` criada antes das migrations existirem, apaga-a uma vez para a API a recriar.

### Configuração e segurança

A configuração de desenvolvimento está em `backend/Academia.Api/appsettings.json`. A chave JWT incluída é exclusivamente local e deve ser substituída por uma chave aleatória segura (mínimo 32 bytes) fora do repositório antes de publicar. Podes configurar variáveis de ambiente ASP.NET Core, por exemplo:

```powershell
$env:Jwt__Secret = "COLOCA_AQUI_UMA_CHAVE_ALEATORIA_COM_PELO_MENOS_32_BYTES"
$env:ConnectionStrings__Academia = "Server=...;Database=AcademiaDotNet;..."
```

O que já está aplicado (e coberto por testes de integração onde se aplica):

- JWT validado no servidor; todos os dados de `/api/learning` são filtrados pelo `UserId` da claim, nunca do pedido.
- Passwords com hash PBKDF2 (ASP.NET Core Identity `PasswordHasher`).
- Validação de todos os inputs com limites iguais aos do schema; DTOs explícitos (sem mass assignment); acesso a dados só por LINQ/EF (queries parametrizadas).
- Rate limiting: 10 pedidos/minuto por IP em `/api/auth/login` e `/register`, 120/minuto por conta em `/api/learning` (`RateLimiting:*`). Atrás do nginx usa o `X-Forwarded-For`, só porque a API não está exposta diretamente (`ReverseProxy:TrustForwardedHeaders`).
- Limites de volume: listas do `/sync` (500 itens, 100 tentativas de quiz) e 300 itens de revisão por conta; snapshot com as últimas 50 tentativas.
- Pedidos ilegíveis (JSON malformado) devolvem 400 com ProblemDetails, nunca 500 nem stack trace.
- Fora de Development: HSTS e redirecionamento HTTPS; sem Swagger; o Lab (endpoints anónimos e destrutivos) recusa-se a arrancar.
- nginx com CSP (`script-src 'self'`), `X-Frame-Options`, `nosniff` e `Referrer-Policy`.
- Dependências sem vulnerabilidades conhecidas (`npm audit`, `dotnet list package --vulnerable --include-transitive`).

- Sessão num cookie `HttpOnly` + `SameSite=Strict` (e `Secure` com prefixo `__Host-` fora de Development): o JWT nunca chega ao JavaScript nem ao `localStorage`, por isso um XSS não o consegue roubar. Proteção CSRF: além do SameSite, os pedidos que alteram dados exigem o header `X-Requested-With` (403 sem ele); o CORS só aceita credenciais das origens em `AllowedOrigins`.

## Executar o Angular

Abre outro terminal na raiz do repositório:

```powershell
npm install
npm start
```

Abre `http://localhost:4200`. Em desenvolvimento, a API vem de `src/environments/environment.ts` (`apiBaseUrl`); se usares outra porta, atualiza esse valor e a lista `AllowedOrigins` da API. O build de produção usa `environment.prod.ts` (`/api`, servido pelo nginx na mesma origem).

Para criar uma compilação de produção do frontend:

```powershell
npm run build
```

Os testes de smoke estruturais do frontend correm com `npm test`. Os testes de integração da API (`backend/Academia.Api.Tests`) correm com `dotnet test .\backend\Academia.sln` e precisam do SQL Server local ligado: cada execução cria e apaga a sua própria base `AcademiaTests_<guid>`.

## Como usar a sincronização

1. Inicia SQL Server, API e Angular.
2. No frontend, abre **Conta** e cria uma conta com email válido e password de pelo menos 10 caracteres.
3. O frontend tenta sincronizar o progresso local depois do registo/login.
4. Também podes clicar em **Sincronizar progresso agora**.
5. O conteúdo continua disponível localmente sem conta. A sincronização exige API e SQL Server ativos.

A sincronização envia todo o progresso local num único pedido (`POST /api/learning/sync`), que a API aplica numa transação — tudo ou nada — antes de devolver o estado da conta; o cliente une-o ao progresso local. As tentativas de quiz têm um id gerado no cliente, por isso reenviar nunca as duplica. Entre dispositivos, ganha o último a sincronizar: ainda não há resolução de conflitos por timestamp de item.

## Endpoints principais

| Método | Endpoint | Acesso | Descrição |
|---|---|---|---|
| GET | `/health` | Público | Health check |
| POST | `/api/auth/register` | Público | Registar conta |
| POST | `/api/auth/login` | Público | Iniciar sessão (JWT num cookie HttpOnly) |
| POST | `/api/auth/logout` | Público | Terminar sessão (apaga o cookie) |
| GET | `/api/auth/me` | Sessão | Obter utilizador atual |
| GET | `/api/learning/snapshot` | Sessão | Obter snapshot de aprendizagem |
| POST | `/api/learning/sync` | Sessão | Enviar todo o progresso numa transação e receber o snapshot (usado pela app) |
| PUT | `/api/learning/lessons` | Sessão | Atualizar conclusão de aula |
| PUT | `/api/learning/reviews` | Sessão | Criar/atualizar item de revisão |
| DELETE | `/api/learning/reviews/{itemKey}` | Sessão | Remover item de revisão |
| POST | `/api/learning/quiz-attempts` | Sessão | Guardar resultado de quiz |
| PUT | `/api/learning/flashcards` | Sessão | Guardar avaliação de flashcard |

## Validação e estado

- A instalação de dependências e a compilação precisam de acesso ao registry npm e ao NuGet.
- A primeira versão do conteúdo é uma base de estudo, não uma enciclopédia completa. Acrescenta e revê os exemplos técnicos antes de os usar como única fonte de preparação.
- Ainda ficam fora do MVP: pagamentos/planos, painel administrativo editorial, recuperação de password, email transacional, observabilidade avançada e publicação cloud automatizada.

## Próximos passos recomendados para produção

1. Testes unitários de componentes Angular e deploy automatizado a partir do CI (o CI em `.github/workflows/ci.yml` já corre tipos, testes, build, scans de dependências e build das imagens).
2. Revogação de sessões no servidor (hoje um token roubado vale até expirar) e renovação do cookie antes de expirar, em vez de pedir login de hora a hora.
3. Recuperação e verificação de conta, política de passwords (lista de passwords comprometidas) e registo que não revele se um email já existe (hoje devolve 409).
4. Segredos num cofre (Key Vault, secrets do orquestrador) e arranque que falhe se a chave JWT de desenvolvimento chegar a produção.
5. Utilizador SQL só com as permissões da aplicação em vez de `sa`, ligação encriptada (`Encrypt=True`) e backups automáticos com restauro testado.
6. Resolução de conflitos entre dispositivos por timestamp de item, em vez de "ganha o último a sincronizar".
7. Completar e rever tecnicamente cada módulo de conteúdo.
