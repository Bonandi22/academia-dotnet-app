import { Routes } from '@angular/router';

export const routes: Routes = [
  {
    path: '',
    loadComponent: () =>
      import('./features/dashboard/dashboard.component')
        .then(m => m.DashboardComponent),
    title: 'Início | Academia .NET'
  },
  {
    path: 'modulos',
    loadComponent: () =>
      import('./features/modules/modules.component')
        .then(m => m.ModulesComponent),
    title: 'Módulos de estudo | Academia .NET'
  },
  {
    path: 'estudo/:moduleId',
    loadComponent: () =>
      import('./features/study/study.component')
        .then(m => m.StudyComponent),
    title: 'Guia de estudo | Academia .NET'
  },
  {
    path: 'quiz',
    loadComponent: () =>
      import('./features/quiz/quiz.component')
        .then(m => m.QuizComponent),
    title: 'Quiz técnico | Academia .NET'
  },
  {
    path: 'flashcards',
    loadComponent: () =>
      import('./features/flashcards/flashcards.component')
        .then(m => m.FlashcardsComponent),
    title: 'Flashcards | Academia .NET'
  },
  {
    path: 'revisao',
    loadComponent: () => import('./features/review/review.component').then(m => m.ReviewComponent),
    title: 'Progresso e revisão | Academia .NET'
  },
  { path: 'conta', loadComponent: () => import('./features/account/account.component').then(m => m.AccountComponent), title: 'Conta e sincronização | Academia .NET' },
  { path: '**', redirectTo: '' }
];