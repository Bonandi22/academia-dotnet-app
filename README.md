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

```powershell
docker compose up --build
```

Depois abre `http://localhost:4200`. A API fica em `http://localhost:5080` e o Swagger em `http://localhost:5080/swagger`. O primeiro build precisa de acesso à internet para obter imagens e pacotes. Esta composição é para desenvolvimento local; não publiques as passwords e a chave JWT incluídas.

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

A API valida tokens JWT, usa hashing de passwords via ASP.NET Core Identity PasswordHasher, aplica limites básicos de validação e separa os dados por `UserId`. O token do cliente é mantido em `localStorage` nesta versão MVP; para produção, considera autenticação por cookie HttpOnly/Secure/SameSite, proteção contra CSRF conforme o desenho escolhido, limitação de tentativas de login, recuperação de password, verificação de email, rate limiting, logs sem dados sensíveis e gestão segura de segredos.

## Executar o Angular

Abre outro terminal na raiz do repositório:

```powershell
npm install
npm start
```

Abre `http://localhost:4200`. A configuração da API está em `src/environments/environment.ts` (`apiBaseUrl`). Se usares outra porta, atualiza esse valor e a lista `AllowedOrigins` da API.

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

A sincronização faz upsert de aulas concluídas, itens de revisão e avaliações de flashcards e envia resultados de quiz ainda não marcados como sincronizados. Os dados locais e remotos são combinados; ao testar em vários dispositivos, confirma os resultados. A resolução de conflitos é simples e não substitui ainda uma estratégia de sincronização distribuída mais sofisticada.

## Endpoints principais

| Método | Endpoint | Acesso | Descrição |
|---|---|---|---|
| GET | `/health` | Público | Health check |
| POST | `/api/auth/register` | Público | Registar conta |
| POST | `/api/auth/login` | Público | Iniciar sessão e receber JWT |
| GET | `/api/auth/me` | JWT | Obter utilizador atual |
| GET | `/api/learning/snapshot` | JWT | Obter snapshot de aprendizagem |
| PUT | `/api/learning/lessons` | JWT | Atualizar conclusão de aula |
| PUT | `/api/learning/reviews` | JWT | Criar/atualizar item de revisão |
| DELETE | `/api/learning/reviews/{itemKey}` | JWT | Remover item de revisão |
| POST | `/api/learning/quiz-attempts` | JWT | Guardar resultado de quiz |
| PUT | `/api/learning/flashcards` | JWT | Guardar avaliação de flashcard |

## Validação e estado

- A instalação de dependências e a compilação devem ser executadas no teu ambiente com acesso ao registry npm e ao NuGet.
- Este ambiente de preparação não tem .NET SDK disponível e a instalação npm excedeu o tempo limite; por isso, a compilação da API e do Angular não foi certificada aqui.
- A primeira versão do conteúdo é uma base de estudo, não uma enciclopédia completa. Acrescenta e revê os exemplos técnicos antes de os usar como única fonte de preparação.
- Ainda ficam fora do MVP: pagamentos/planos, painel administrativo editorial, recuperação de password, email transacional, observabilidade avançada e publicação cloud automatizada.

## Próximos passos recomendados para produção

1. Pipeline CI a correr `npm test` e `dotnet test` (com SQL Server em serviço) e testes unitários de componentes Angular.
2. Migrar autenticação para cookie HttpOnly ou rever cuidadosamente o armazenamento do token.
3. Adicionar recuperação/verificação de conta, rate limiting e política de password.
4. Completar e rever tecnicamente cada módulo de conteúdo.
5. Adicionar pipeline CI, análise de dependências e deploy separado para frontend/API/SQL Server gerido.
