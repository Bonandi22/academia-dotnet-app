export interface StudyModule {
  id: string;
  title: string;
  category: string;
  level: 'Fundamentos' | 'Intermédio' | 'Avançado';
  description: string;
  topics: string[];
  icon: string;
}

export const STUDY_MODULES: StudyModule[] = [
  {
    id: 'csharp',
    title: 'C# e plataforma .NET',
    category: 'Backend',
    level: 'Fundamentos',
    description: 'Revê a linguagem, orientação a objetos, LINQ, async/await e gestão de dependências.',
    topics: ['OOP e SOLID', 'Generics e collections', 'LINQ', 'async/await', 'Dependency Injection', 'Memória e IDisposable'],
    icon: 'C#'
  },
  {
    id: 'aspnet',
    title: 'ASP.NET Core e APIs',
    category: 'Backend',
    level: 'Intermédio',
    description: 'Constrói APIs seguras, consistentes, observáveis e fáceis de manter.',
    topics: ['REST e HTTP', 'Middleware', 'Autenticação e autorização', 'Validação', 'Tratamento de erros', 'Resiliência'],
    icon: 'API'
  },
  {
    id: 'data',
    title: 'EF Core e SQL Server',
    category: 'Dados',
    level: 'Intermédio',
    description: 'Investiga consultas, modela dados e melhora performance com base em evidências.',
    topics: ['Tracking e projeções', 'Migrations', 'Transações', 'Índices', 'Execution plans', 'Problema N+1'],
    icon: 'DB'
  },
  {
    id: 'architecture',
    title: 'Arquitetura e padrões',
    category: 'Arquitetura',
    level: 'Avançado',
    description: 'Compara alternativas arquiteturais e aprende a justificar decisões técnicas.',
    topics: ['Clean Architecture', 'DDD', 'CQRS', 'SOLID', 'Design patterns', 'System design'],
    icon: 'ARC'
  },
  {
    id: 'microservices',
    title: 'Microserviços e RabbitMQ',
    category: 'Arquitetura',
    level: 'Avançado',
    description: 'Compreende comunicação distribuída, falhas, idempotência e consistência.',
    topics: ['REST vs. mensageria', 'Retries e timeouts', 'Idempotência', 'Outbox', 'Saga', 'Observabilidade'],
    icon: 'MSG'
  },
  {
    id: 'angular',
    title: 'Angular e RxJS',
    category: 'Frontend',
    level: 'Intermédio',
    description: 'Desenvolve interfaces estruturadas com standalone components, formulários e reatividade.',
    topics: ['Standalone components', 'Signals e RxJS', 'Reactive Forms', 'Router', 'HttpClient', 'Performance'],
    icon: 'NG'
  },
  {
    id: 'react',
    title: 'React e microfrontends',
    category: 'Frontend',
    level: 'Intermédio',
    description: 'Revê hooks, gestão de estado, TypeScript e composição de aplicações frontend.',
    topics: ['Props e state', 'Hooks', 'TypeScript', 'Consumo de APIs', 'Performance', 'Microfrontends'],
    icon: 'R'
  },
  {
    id: 'quality',
    title: 'Testes, DevOps e produção',
    category: 'Engenharia',
    level: 'Avançado',
    description: 'Prepara-te para incidentes, pipelines, qualidade e operação em produção.',
    topics: ['Testes unitários', 'Testes de integração', 'CI/CD', 'Docker', 'Logging e tracing', 'Debugging'],
    icon: 'QA'
  }
];

export interface QuizQuestion {
  id: string;
  topic: string;
  prompt: string;
  options: string[];
  correctIndex: number;
  explanation: string;
  level: 'Fundamentos' | 'Intermédio' | 'Avançado';
}

export const QUIZ_QUESTIONS: QuizQuestion[] = [
  {
    id: 'di',
    topic: 'C# e .NET',
    prompt: 'Qual é uma das principais vantagens da Dependency Injection?',
    options: [
      'Eliminar a necessidade de interfaces',
      'Reduzir acoplamento e facilitar testes e substituição de dependências',
      'Fazer com que todos os serviços sejam singletons',
      'Evitar qualquer exceção em runtime'
    ],
    correctIndex: 1,
    explanation: 'A injeção de dependências separa a utilização de um serviço da criação concreta desse serviço. Isso facilita testes e substituição de implementações.'
  , level: 'Fundamentos'
  },
  {
    id: 'async',
    topic: 'C# e .NET',
    prompt: 'Qual afirmação sobre async/await é correta?',
    options: [
      'Cria sempre uma nova thread',
      'Torna qualquer algoritmo automaticamente mais rápido',
      'Permite aguardar operações assíncronas sem bloquear a thread durante uma espera de I/O',
      'Só pode ser usado em aplicações web'
    ],
    correctIndex: 2,
    explanation: 'async/await não cria necessariamente uma thread. Em operações de I/O, permite libertar a thread enquanto a operação está pendente.'
  , level: 'Intermédio'
  },
  {
    id: 'tracking',
    topic: 'EF Core e SQL Server',
    prompt: 'Quando é geralmente apropriado usar AsNoTracking()?',
    options: [
      'Quando se pretende editar a entidade e guardar as alterações com o mesmo contexto',
      'Em consultas de leitura que não precisam de acompanhar alterações às entidades',
      'Para impedir SQL injection',
      'Para criar automaticamente índices'
    ],
    correctIndex: 1,
    explanation: 'AsNoTracking reduz o trabalho de tracking em consultas de leitura. Não é uma funcionalidade de segurança contra SQL injection.'
  , level: 'Intermédio'
  },
  {
    id: 'nplus1',
    topic: 'EF Core e SQL Server',
    prompt: 'O que caracteriza o problema N+1?',
    options: [
      'Uma query que usa exatamente dois índices',
      'Uma transação que nunca termina',
      'Uma consulta inicial seguida de consultas adicionais por cada elemento',
      'A falta de uma chave primária'
    ],
    correctIndex: 2,
    explanation: 'O padrão N+1 pode gerar muitas viagens à base de dados. Investiga logs SQL e considera projeções ou carregamento de dados relacionado adequado.'
  , level: 'Intermédio'
  },
  {
    id: 'cors',
    topic: 'ASP.NET Core',
    prompt: 'O que é CORS?',
    options: [
      'Um mecanismo de autenticação de utilizadores',
      'Uma política do browser relacionada com pedidos cross-origin',
      'Um algoritmo de hashing de passwords',
      'Uma ferramenta de migrations'
    ],
    correctIndex: 1,
    explanation: 'CORS controla como browsers permitem pedidos cross-origin. Não substitui autenticação nem autorização no backend.'
  , level: 'Fundamentos'
  },
  {
    id: 'outbox',
    topic: 'Microserviços',
    prompt: 'Qual é o objetivo do padrão Outbox?',
    options: [
      'Substituir o banco de dados por uma fila',
      'Garantir que todos os serviços partilham uma única transação global simples',
      'Guardar a alteração de negócio e o evento a publicar na mesma transação local',
      'Eliminar a necessidade de consumidores idempotentes'
    ],
    correctIndex: 2,
    explanation: 'A Outbox ajuda a evitar a inconsistência entre gravar dados e publicar eventos: o evento é persistido na mesma transação local e publicado depois.'
  , level: 'Avançado'
  },
  {
    id: 'angular-switchmap',
    topic: 'Angular e RxJS',
    prompt: 'Num campo de pesquisa reativo, por que switchMap costuma ser útil?',
    options: [
      'Executa sempre todas as chamadas em paralelo',
      'Ignora todos os valores depois do primeiro',
      'Troca para o fluxo interno mais recente e cancela a subscrição interna anterior',
      'Transforma um Observable em Promise automaticamente'
    ],
    correctIndex: 2,
    explanation: 'switchMap é útil para pesquisas em que o resultado mais recente interessa. A capacidade de cancelar o trabalho subjacente depende do Observable/cliente usado.'
  , level: 'Intermédio'
  },
  {
    id: 'react-effect',
    topic: 'React',
    prompt: 'Qual é um uso adequado de useEffect?',
    options: [
      'Substituir todas as variáveis de estado',
      'Sincronizar o componente com sistemas externos, gerindo dependências e limpeza',
      'Garantir que o componente renderiza apenas uma vez em qualquer situação',
      'Executar validação de tipos TypeScript em runtime'
    ],
    correctIndex: 1,
    explanation: 'useEffect é adequado para sincronização com sistemas externos. As dependências e a limpeza devem refletir o efeito implementado.'
  , level: 'Intermédio'
  }
];

export interface Flashcard {
  front: string;
  back: string;
  topic: string;
}

export const FLASHCARDS: Flashcard[] = [
  { topic: 'C# e .NET', front: 'Qual é a diferença entre autenticação e autorização?', back: 'Autenticação verifica a identidade; autorização determina o que essa identidade pode fazer.' },
  { topic: 'EF Core', front: 'O que é o problema N+1?', back: 'Uma consulta inicial seguida de consultas adicionais por cada elemento, aumentando viagens à base de dados.' },
  { topic: 'Microserviços', front: 'O que significa idempotência?', back: 'Repetir a mesma intenção não deve produzir efeitos adicionais além do resultado esperado.' },
  { topic: 'Arquitetura', front: 'Qual é a regra central da Clean Architecture?', back: 'As dependências do código apontam para dentro, mantendo as regras de negócio independentes de detalhes externos.' },
  { topic: 'Angular', front: 'Quando usar switchMap?', back: 'Quando um novo valor deve substituir o fluxo interno anterior, por exemplo numa pesquisa reativa.' },
  { topic: 'React', front: 'Qual é a diferença entre props e state?', back: 'Props são entradas recebidas de um componente pai; state é o estado gerido pelo componente ou pelos seus hooks.' },
  { topic: 'SQL Server', front: 'O que deves analisar antes de adicionar um índice?', back: 'Plano de execução, seletividade, padrão de consultas, custo de escrita, armazenamento e evidências de performance.' }
];