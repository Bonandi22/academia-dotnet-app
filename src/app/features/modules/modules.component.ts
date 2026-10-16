import { Component, computed, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { RouterLink } from '@angular/router';
import { STUDY_MODULES } from '../../data/study-data';
import { STUDY_LESSONS } from '../../data/study-content';
import { ProgressService } from '../../core/progress.service';

@Component({
  standalone: true,
  selector: 'app-modules',
  imports: [FormsModule, RouterLink],
  template: `
    <section class="page-heading">
      <span class="eyebrow">Biblioteca de conhecimento</span>
      <h1>Módulos de estudo</h1>
      <p>Organiza a tua preparação por tecnologia e aprofunda cada tema ao teu ritmo.</p>
    </section>
    <section class="filters">
      <label class="search-box"><span aria-hidden="true">⌕</span>
        <input type="search" placeholder="Pesquisar módulos ou tópicos..." [ngModel]="search()" (ngModelChange)="search.set($event)">
      </label>
      <label class="select-label"><span class="sr-only">Filtrar por nível</span>
        <select [ngModel]="level()" (ngModelChange)="level.set($event)">
          <option value="Todos">Todos os níveis</option><option>Fundamentos</option><option>Intermédio</option><option>Avançado</option>
        </select>
      </label>
    </section>
    <p class="result-count">{{ filteredModules().length }} módulos disponíveis</p>
    <section class="module-grid module-grid-all">
      @for (module of filteredModules(); track module.id) {
        <article class="module-card">
          <div class="module-top"><span class="module-icon">{{ module.icon }}</span><span class="level">{{ module.level }}</span></div>
          <span class="category">{{ module.category }}</span>
          <h2>{{ module.title }}</h2><p>{{ module.description }}</p>
          <div class="topic-list">@for (topic of module.topics; track topic) { <span>{{ topic }}</span> }</div>
          <div class="progress-panel-label"><span>Progresso do módulo</span><strong>{{ moduleProgress(module.id) }}%</strong></div><div class="progress-track"><div class="progress-fill" [style.width.%]="moduleProgress(module.id)"></div></div>
          <div class="module-actions">
            <a class="button" [routerLink]="['/estudo', module.id]">Estudar módulo →</a>
            <button class="button button-outline" type="button" (click)="toggle(module.id)">
              {{ expandedId() === module.id ? 'Ocultar tópicos' : 'Ver tópicos' }}
            </button>
          </div>
          @if (expandedId() === module.id) {
            <div class="expanded-topics"><h3>Lista de revisão</h3><ol>@for (topic of module.topics; track topic) { <li>{{ topic }}</li> }</ol></div>
          }
        </article>
      } @empty {
        <div class="empty-state"><h2>Nenhum módulo encontrado</h2><p>Experimenta outro termo ou nível.</p></div>
      }
    </section>
  `
})
export class ModulesComponent {
  readonly modules = STUDY_MODULES;
  constructor(private readonly progress: ProgressService) {}
  moduleProgress(moduleId: string): number {
    const lesson = STUDY_LESSONS.find(item => item.moduleId === moduleId);
    return lesson ? this.progress.lessonProgress(moduleId, lesson.sections.length) : 0;
  }
  readonly search = signal('');
  readonly level = signal('Todos');
  readonly expandedId = signal<string | null>(null);
  readonly filteredModules = computed(() => {
    const query = this.search().trim().toLowerCase();
    const level = this.level();
    return this.modules.filter(module => {
      const matchesLevel = level === 'Todos' || module.level === level;
      const haystack = [module.title, module.description, module.category, ...module.topics].join(' ').toLowerCase();
      return matchesLevel && (!query || haystack.includes(query));
    });
  });
  toggle(id: string): void {
    this.expandedId.set(this.expandedId() === id ? null : id);
  }
}