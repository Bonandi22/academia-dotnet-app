export interface LessonSection {
  title: string;
  explanation: string;
  code?: string;
  takeaway?: string;
}

export interface StudyLesson {
  moduleId: string;
  title: string;
  subtitle: string;
  objectives: string[];
  estimatedMinutes: number;
  sections: LessonSection[];
  interviewQuestions: { question: string; answer: string }[];
  exercise: { scenario: string; task: string; solution: string };
}

/** Conteúdo de estudo versionado junto com o frontend. Pode ser movido para uma API no futuro. */
export const STUDY_LESSONS: StudyLesson[] = [
  {
    moduleId: 'csharp',
    title: 'C# moderno: async/await e injeção de dependências',
    subtitle: 'Do conceito à explicação que esperam numa entrevista técnica.',
    estimatedMinutes: 18,
    objectives: ['Distinguir I/O-bound de CPU-bound', 'Explicar o papel de Task e await', 'Escolher lifetimes de Dependency Injection'],
    sections: [
      { title: '1. O que async/await realmente faz', explanation: 'async e await tornam o fluxo assíncrono mais legível. Em operações de I/O, como chamadas HTTP ou acesso assíncrono à base de dados, a thread não precisa de ficar bloqueada enquanto aguarda a resposta. async não cria automaticamente uma nova thread e não torna, por si só, um algoritmo mais rápido.', code: 'public async Task<Customer?> GetCustomerAsync(int id, CancellationToken ct)\n{\n    return await db.Customers\n        .AsNoTracking()\n        .FirstOrDefaultAsync(x => x.Id == id, ct);\n}', takeaway: 'Propaga async até à camada que inicia a operação e evita .Result ou .Wait() em código assíncrono.' },
      { title: '2. Task, Task<T> e CancellationToken', explanation: 'Task representa uma operação assíncrona sem resultado; Task<T> representa uma operação que produz T. CancellationToken permite que o chamador solicite cancelamento cooperativo. A operação tem de aceitar e respeitar o token para que o cancelamento seja útil.', code: 'public async Task<string> LoadAsync(CancellationToken ct)\n{\n    using var response = await httpClient.GetAsync("/api/status", ct);\n    response.EnsureSuccessStatusCode();\n    return await response.Content.ReadAsStringAsync(ct);\n}' },
      { title: '3. Dependency Injection e lifetimes', explanation: 'Transient cria uma instância sempre que o serviço é resolvido; Scoped cria uma instância por scope (normalmente por pedido HTTP); Singleton mantém uma instância durante a vida do container. Um singleton não deve capturar diretamente um serviço scoped, como um DbContext.', code: 'builder.Services.AddTransient<IFormatter, Formatter>();\nbuilder.Services.AddScoped<IOrderService, OrderService>();\nbuilder.Services.AddSingleton<ISystemClock, SystemClock>();', takeaway: 'O lifetime deve refletir o estado e o ciclo de vida da dependência, não apenas uma preferência de performance.' }
    ],
    interviewQuestions: [
      { question: 'async/await cria sempre uma thread?', answer: 'Não. Em I/O assíncrono, a thread pode ser libertada enquanto a operação aguarda. Trabalho intensivo de CPU é um problema diferente e pode exigir outra estratégia.' },
      { question: 'Quando usarias Scoped em vez de Singleton?', answer: 'Para serviços cujo estado ou dependências devem viver por pedido, como serviços de aplicação que usam um DbContext scoped.' },
      { question: 'Porque evitar .Result numa chamada assíncrona?', answer: 'Bloqueia a thread e pode causar contenção; em determinados contextos pode também contribuir para deadlocks. Prefere await.' }
    ],
    exercise: { scenario: 'Um endpoint chama uma API externa e demora vários segundos. O método usa GetAwaiter().GetResult() e não aceita CancellationToken.', task: 'Indica duas melhorias e justifica-as.', solution: 'Usar HttpClient com GetAsync(..., cancellationToken) e propagar async/await até ao endpoint. Isto evita bloqueio síncrono desnecessário e permite cancelar a operação quando o pedido deixa de ser necessário.' }
  },
  {
    moduleId: 'aspnet',
    title: 'ASP.NET Core: pipeline HTTP, validação e erros',
    subtitle: 'Como estruturar endpoints previsíveis e fáceis de operar.',
    estimatedMinutes: 16,
    objectives: ['Explicar middleware e a ordem do pipeline', 'Separar autenticação de autorização', 'Devolver respostas HTTP consistentes'],
    sections: [
      { title: '1. O pipeline de middleware', explanation: 'Cada middleware pode executar lógica antes e depois do próximo componente, terminar o pedido ou encaminhá-lo. A ordem importa: por exemplo, o tratamento de exceções deve envolver componentes posteriores e autenticação deve ocorrer antes da autorização.', code: 'app.UseExceptionHandler();\napp.UseHttpsRedirection();\napp.UseAuthentication();\napp.UseAuthorization();\napp.MapControllers();', takeaway: 'Quando um pedido falha ou não chega ao endpoint, verifica primeiro o pipeline e a ordem dos middlewares.' },
      { title: '2. Autenticação vs. autorização', explanation: 'Autenticação estabelece quem é o utilizador, por exemplo validando um bearer token. Autorização decide se essa identidade pode executar uma ação, com base em roles, claims ou policies. Estar autenticado não significa ter acesso a todos os recursos.', code: '[Authorize(Policy = "CanManageOrders")]\n[HttpPost("orders")]\npublic async Task<IActionResult> Create(CreateOrderRequest request)\n{\n    // validar e executar o caso de uso\n    return CreatedAtAction(nameof(GetById), new { id = 42 }, null);\n}' },
      { title: '3. Contratos e tratamento de erros', explanation: 'Usa códigos HTTP coerentes: 200 para sucesso com representação, 201 para recurso criado, 204 para sucesso sem conteúdo, 400 para pedido inválido, 401 para falta de autenticação e 403 para acesso proibido. Evita expor stack traces ou detalhes internos ao cliente; regista contexto útil nos logs.', code: 'builder.Services.AddProblemDetails();\napp.UseExceptionHandler();' }
    ],
    interviewQuestions: [
      { question: 'Qual a diferença entre 401 e 403?', answer: '401 indica que falta autenticação válida; 403 indica que o pedido foi compreendido, mas a identidade não tem permissão para a ação.' },
      { question: 'Porque é que a ordem do middleware importa?', answer: 'Porque cada componente pode depender do contexto preparado pelos anteriores, como a identidade criada pela autenticação antes de avaliar policies.' },
      { question: 'Deves devolver a exceção completa ao frontend?', answer: 'Não. Devolve uma resposta controlada e um identificador de correlação, mantendo detalhes técnicos nos logs protegidos.' }
    ],
    exercise: { scenario: 'Um endpoint protegido responde 403 a um utilizador que ainda não enviou token.', task: 'Que verificações farias primeiro?', solution: 'Confirmar a ordem UseAuthentication antes de UseAuthorization, configuração do esquema de autenticação e política aplicada. Um utilizador sem identidade autenticada normalmente recebe 401; 403 é comum quando está autenticado, mas não autorizado.' }
  },
  {
    moduleId: 'data',
    title: 'EF Core e SQL Server: consultas e performance',
    subtitle: 'Aprende a diagnosticar antes de otimizar.',
    estimatedMinutes: 20,
    objectives: ['Usar projeções e AsNoTracking em leituras', 'Reconhecer o problema N+1', 'Investigar índices e planos de execução'],
    sections: [
      { title: '1. Projetar apenas os dados necessários', explanation: 'Em endpoints de leitura, uma projeção para DTO pode reduzir colunas transferidas e evitar materializar entidades completas. AsNoTracking é útil quando não precisas de acompanhar alterações às entidades devolvidas.', code: 'var orders = await db.Orders\n    .AsNoTracking()\n    .Where(o => o.CustomerId == customerId)\n    .Select(o => new OrderSummaryDto(o.Id, o.CreatedAt, o.Total))\n    .ToListAsync(ct);', takeaway: 'Não uses Include por reflexo. Define primeiro quais os dados de que o caso de uso precisa.' },
      { title: '2. Identificar N+1', explanation: 'O problema N+1 ocorre quando uma consulta inicial é seguida por consultas adicionais para cada registo, muitas vezes por carregamento lazy dentro de um ciclo. O resultado pode ser uma quantidade de round-trips que cresce com o número de elementos.', code: '// Evitar consultar Customer individualmente dentro de cada iteração.\nvar rows = await db.Orders\n    .Where(o => o.Status == OrderStatus.Open)\n    .Select(o => new { o.Id, CustomerName = o.Customer.Name })\n    .ToListAsync(ct);', takeaway: 'Confirma a hipótese através dos logs SQL e do número de comandos executados.' },
      { title: '3. Índices e planos de execução', explanation: 'Um índice pode acelerar filtros, joins e ordenações, mas ocupa espaço e aumenta o custo de INSERT, UPDATE e DELETE. Analisa o plano de execução real, seletividade, estatísticas e consultas mais frequentes antes de decidir.', code: 'CREATE INDEX IX_Orders_CustomerId_CreatedAt\nON dbo.Orders (CustomerId, CreatedAt DESC);', takeaway: 'A ordem das colunas de um índice composto deve refletir os predicados e padrões de consulta relevantes.' },
      { title: '4. Transações e concorrência', explanation: 'Uma transação agrupa alterações que devem ser confirmadas ou revertidas em conjunto. Para atualizações concorrentes, considera tokens de concorrência e trata conflitos explicitamente; não assumes que duas operações simultâneas nunca vão alterar o mesmo registo.' }
    ],
    interviewQuestions: [
      { question: 'AsNoTracking impede SQL injection?', answer: 'Não. Serve para desativar o tracking de alterações em consultas. SQL injection deve ser evitada usando APIs parametrizadas e não concatenando entrada do utilizador em SQL.' },
      { question: 'Como investigarias uma API lenta que usa EF Core?', answer: 'Mediria latência, inspecionaria os comandos SQL, plano de execução, índices, volume de dados, round-trips, projeções e eventuais problemas N+1 antes de alterar o código.' },
      { question: 'Um índice torna todas as consultas mais rápidas?', answer: 'Não. A escolha depende dos filtros, ordenações, seletividade e custo de escrita; índices desnecessários também prejudicam.' }
    ],
    exercise: { scenario: 'Uma página lista 500 encomendas e executa uma consulta adicional para obter o nome do cliente de cada encomenda.', task: 'Identifica o problema e propõe uma solução.', solution: 'É um padrão N+1. Projeta o nome do cliente na consulta principal ou usa uma estratégia de carregamento apropriada, confirma o SQL gerado e mede o resultado.' }
  },
  {
    moduleId: 'architecture',
    title: 'Clean Architecture, SOLID e limites de dependência',
    subtitle: 'Justifica as decisões arquiteturais com responsabilidades claras.',
    estimatedMinutes: 17,
    objectives: ['Explicar a regra de dependência', 'Separar domínio de infraestrutura', 'Evitar abstrações sem necessidade'],
    sections: [
      { title: '1. A regra de dependência', explanation: 'Na Clean Architecture, o código de camadas externas pode depender das abstrações internas, mas o domínio não deve depender de detalhes como HTTP, ORM ou broker. O objetivo é manter as regras de negócio testáveis e menos sensíveis a mudanças de tecnologia.', code: 'Domain: regras e entidades\nApplication: casos de uso e contratos\nInfrastructure: EF Core, email, RabbitMQ\nAPI: HTTP, autenticação e composição' },
      { title: '2. SOLID na prática', explanation: 'Single Responsibility procura uma razão principal para mudar; Open/Closed favorece extensão sem alterar continuamente código estável; Liskov exige substituibilidade correta; Interface Segregation evita contratos demasiado amplos; Dependency Inversion faz módulos de alto nível dependerem de abstrações.' },
      { title: '3. Evitar arquitetura cerimonial', explanation: 'Nem todo CRUD precisa de dezenas de interfaces ou padrões. Introduz abstrações quando clarificam uma regra, isolam uma dependência volátil ou tornam um teste importante possível. A arquitetura deve reduzir o custo de mudança, não apenas aumentar o número de ficheiros.', takeaway: 'Numa entrevista, explica o problema que o padrão resolve e o custo que introduz.' }
    ],
    interviewQuestions: [
      { question: 'Porque o domínio não deve referenciar o EF Core?', answer: 'Para evitar acoplamento das regras de negócio a detalhes de persistência e manter o domínio utilizável e testável sem essa infraestrutura.' },
      { question: 'Clean Architecture significa obrigatoriamente microserviços?', answer: 'Não. É uma forma de organizar dependências dentro de uma aplicação; pode ser usada num monólito modular.' },
      { question: 'Quando uma abstração é desnecessária?', answer: 'Quando não isola uma variação real, não clarifica um limite e acrescenta indireção sem benefício concreto.' }
    ],
    exercise: { scenario: 'Uma entidade de domínio importa Microsoft.EntityFrameworkCore apenas para executar uma consulta.', task: 'Como reorganizarias a responsabilidade?', solution: 'Mover a consulta para um repositório ou serviço de aplicação/infraestrutura, mantendo a regra de negócio no domínio e fazendo a infraestrutura implementar os contratos definidos para dentro.' }
  },
  {
    moduleId: 'microservices',
    title: 'Microserviços e RabbitMQ: fiabilidade de mensagens',
    subtitle: 'Preparação para falhas, duplicados e consistência eventual.',
    estimatedMinutes: 19,
    objectives: ['Escolher entre HTTP e mensageria', 'Desenhar consumidores idempotentes', 'Explicar Outbox e retries'],
    sections: [
      { title: '1. Pedido síncrono ou evento?', explanation: 'HTTP é adequado quando o chamador precisa de uma resposta imediata. Mensageria desacopla temporalmente produtor e consumidor e ajuda a absorver picos, mas introduz consistência eventual, observabilidade distribuída e tratamento de falhas.' },
      { title: '2. Entrega e idempotência', explanation: 'Em sistemas de mensagens, um consumidor pode receber novamente uma mensagem, por exemplo após falha antes de confirmar o processamento. A operação deve tolerar duplicados usando uma chave de idempotência, registo de mensagens processadas ou uma atualização condicional segura.', code: 'if (await processedMessages.ExistsAsync(message.Id, ct))\n    return;\n\nawait handler.HandleAsync(message, ct);\nawait processedMessages.MarkAsync(message.Id, ct);', takeaway: 'A deduplicação e a alteração de negócio precisam de uma estratégia consistente; código sequencial isolado não elimina todas as condições de corrida.' },
      { title: '3. Outbox e retries', explanation: 'O padrão Outbox grava a alteração de negócio e o evento numa tabela Outbox na mesma transação local. Um processo publica os eventos pendentes e marca-os como enviados. Retries devem ter limites, backoff e uma política para mensagens que falham repetidamente, como uma dead-letter queue.' }
    ],
    interviewQuestions: [
      { question: 'O que significa consistência eventual?', answer: 'Os serviços podem observar estados diferentes temporariamente, mas convergem quando os eventos e operações pendentes são processados com sucesso.' },
      { question: 'O Outbox garante que uma mensagem nunca é duplicada?', answer: 'Não necessariamente. Ajuda a não perder o evento entre commit e publicação; consumidores e publicação ainda devem tolerar duplicados.' },
      { question: 'Porque limitar retries?', answer: 'Para evitar ciclos infinitos, sobrecarga e amplificação de falhas; mensagens persistentes devem ser isoladas e investigadas.' }
    ],
    exercise: { scenario: 'O serviço grava uma encomenda na base de dados e falha antes de publicar OrderCreated no RabbitMQ.', task: 'Que padrão reduz o risco de inconsistência?', solution: 'Usar Outbox: gravar encomenda e evento na mesma transação local e publicar o evento a partir de um processo que lê a Outbox. O consumidor continua a precisar de idempotência.' }
  },
  {
    moduleId: 'angular',
    title: 'Angular: componentes, reatividade e chamadas HTTP',
    subtitle: 'Estrutura componentes previsíveis e fluxos reativos.',
    estimatedMinutes: 16,
    objectives: ['Explicar componentes standalone', 'Escolher entre Signals e RxJS', 'Evitar respostas obsoletas em pesquisas'],
    sections: [
      { title: '1. Componentes standalone', explanation: 'Componentes standalone declaram diretamente as dependências de template em imports e reduzem a necessidade de NgModules para organizar funcionalidades. Mantém componentes focados na apresentação e extrai lógica reutilizável para serviços quando fizer sentido.' },
      { title: '2. Signals e RxJS', explanation: 'Signals são úteis para estado síncrono reativo e derivação de valores na interface. RxJS é adequado para fluxos assíncronos, eventos e composição de operações, como pesquisas com debounce e cancelamento da subscrição anterior.' , code: 'searchTerms.pipe(\n  debounceTime(300),\n  distinctUntilChanged(),\n  switchMap(term => api.search(term))\n);' },
      { title: '3. Formulários e estados de UI', explanation: 'Reactive Forms tornam explícitos o modelo, validação e estado do formulário. Para chamadas HTTP, representa estados de carregamento, sucesso e erro; evita deixar o utilizador sem feedback quando a rede falha.' }
    ],
    interviewQuestions: [
      { question: 'Quando switchMap é útil?', answer: 'Quando apenas o resultado do fluxo mais recente interessa, como pesquisa por texto. A subscrição interna anterior é cancelada; o cancelamento real da operação depende da fonte.' },
      { question: 'Signals substituem RxJS?', answer: 'Não completamente. Signals são úteis para estado reativo local; RxJS oferece operadores e composição de streams assíncronos.' },
      { question: 'Porque separar a chamada HTTP do componente?', answer: 'Um serviço pode centralizar acesso à API, facilitar reutilização e testes, e manter o componente focado no estado e interação da interface.' }
    ],
    exercise: { scenario: 'Ao pesquisar rapidamente, uma resposta antiga chega depois da resposta da pesquisa mais recente.', task: 'Como podes reduzir esse problema num fluxo RxJS?', solution: 'Usa debounceTime e switchMap quando a semântica for “o valor mais recente vence”. Confirma também o tratamento de erros para que um erro não termine inadvertidamente todo o fluxo de pesquisa.' }
  },
  {
    moduleId: 'react',
    title: 'React e TypeScript: estado e efeitos',
    subtitle: 'Explica o ciclo de renderização e sincronização com sistemas externos.',
    estimatedMinutes: 14,
    objectives: ['Distinguir props e state', 'Usar hooks com dependências corretas', 'Evitar efeitos redundantes'],
    sections: [
      { title: '1. Props e state', explanation: 'Props são dados recebidos pelo componente pai; state é estado local gerido pelo componente ou por hooks. Atualizar state agenda uma nova renderização. Evita mutar diretamente objetos de estado: cria novos valores.' },
      { title: '2. useEffect e limpeza', explanation: 'useEffect sincroniza o componente com sistemas externos, como subscrições, timers ou APIs imperativas. A lista de dependências deve representar os valores usados pelo efeito e a função de limpeza deve libertar recursos quando necessário.', code: 'useEffect(() => {\n  const controller = new AbortController();\n  loadData(controller.signal);\n  return () => controller.abort();\n}, [loadData]);' },
      { title: '3. Estado derivado', explanation: 'Se um valor pode ser calculado a partir de props ou state existente, muitas vezes não precisa de um segundo state sincronizado por efeito. Menos estado duplicado significa menos oportunidades para inconsistência.' }
    ],
    interviewQuestions: [
      { question: 'useEffect garante que o componente renderiza apenas uma vez?', answer: 'Não. A execução depende das dependências e do ciclo de vida; em desenvolvimento, Strict Mode pode executar ciclos adicionais para revelar efeitos problemáticos.' },
      { question: 'Quando deves usar cleanup?', answer: 'Quando o efeito cria recursos ou subscrições que precisam de ser cancelados ou libertados, como listeners, timers ou pedidos canceláveis.' },
      { question: 'Porque evitar estado derivado duplicado?', answer: 'Pode ficar dessincronizado com a fonte original e obriga a lógica extra para o manter consistente.' }
    ],
    exercise: { scenario: 'Um componente guarda total e quantidade em estados separados, mas total é sempre quantidade vezes preço.', task: 'Que simplificação considerarias?', solution: 'Calcular total durante a renderização a partir de quantidade e preço, salvo se existir uma razão real para armazenar esse valor independentemente.' }
  },
  {
    moduleId: 'quality',
    title: 'Testes, CI/CD e diagnóstico em produção',
    subtitle: 'Distingue testes úteis de verificações superficiais.',
    estimatedMinutes: 18,
    objectives: ['Escolher níveis de teste', 'Investigar incidentes com evidências', 'Explicar um pipeline seguro'],
    sections: [
      { title: '1. Pirâmide de testes', explanation: 'Testes unitários verificam comportamento isolado com rapidez; testes de integração verificam colaboração real entre componentes, por exemplo API e base de dados; testes end-to-end validam fluxos completos e tendem a ser mais lentos e sensíveis ao ambiente.' },
      { title: '2. CI/CD', explanation: 'Um pipeline típico restaura dependências, compila, executa testes, analisa qualidade e produz um artefacto versionado. A entrega deve promover o mesmo artefacto entre ambientes, proteger segredos e permitir rollback ou mitigação.' },
      { title: '3. Diagnóstico baseado em evidências', explanation: 'Começa por delimitar impacto e janela temporal. Correlaciona métricas, logs estruturados, traces, alterações recentes e dependências externas. Evita mudar várias coisas ao mesmo tempo sem hipótese verificável.', code: 'logger.LogInformation(\n    "Order {OrderId} processed for customer {CustomerId}",\n    order.Id, customer.Id);', takeaway: 'Usa identificadores de correlação e não registes passwords, tokens ou dados pessoais desnecessários.' }
    ],
    interviewQuestions: [
      { question: 'Um teste unitário deve testar a base de dados real?', answer: 'Em geral, não; isso aproxima o teste de integração. Testes unitários isolam a unidade, enquanto testes de integração validam a colaboração com dependências reais.' },
      { question: 'O que investigas numa API que ficou lenta após uma publicação?', answer: 'Compara métricas antes/depois, logs, traces, consultas SQL, dependências externas e alterações no artefacto; identifica primeiro onde o tempo é gasto.' },
      { question: 'Porque não colocar segredos no repositório?', answer: 'O histórico do Git pode preservá-los mesmo após remoção. Usa secret stores/variáveis protegidas e roda credenciais expostas.' }
    ],
    exercise: { scenario: 'Depois de um deploy, a latência P95 aumentou e a taxa de erros subiu.', task: 'Define uma sequência inicial de investigação.', solution: 'Confirmar impacto e janela temporal, comparar métricas e versões, analisar traces e logs correlacionados, verificar dependências/SQL e alterações recentes, depois mitigar com rollback ou feature flag se necessário.' }
  }
];
