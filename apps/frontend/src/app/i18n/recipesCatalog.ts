import type { ProductionLocale } from "./productionCatalog";

type Catalog = Record<string, string>;

/** Every key required for the production recipes experience across locales. */
export const REQUIRED_RECIPES_I18N_KEYS = [
  "nav.recipes",
  "recipes.title",
  "recipes.subtitle",
  "recipes.loading",
  "recipes.empty",
  "recipes.empty.guidance",
  "recipes.actions.generate",
  "recipes.actions.retry",
  "recipes.actions.back",
  "recipes.actions.cancel",
  "recipes.actions.confirmGenerate",
  "recipes.actions.selectCandidate",
  "recipes.actions.openInventory",
  "recipes.list.error",
  "recipes.detail.title",
  "recipes.detail.loading",
  "recipes.detail.error",
  "recipes.detail.notFound",
  "recipes.detail.servings",
  "recipes.detail.revision",
  "recipes.detail.mealTypes",
  "recipes.detail.ingredients",
  "recipes.detail.stages",
  "recipes.detail.assumptions",
  "recipes.detail.yield",
  "recipes.generate.title",
  "recipes.generate.subtitle",
  "recipes.generate.confirmDetail",
  "recipes.generate.requesting",
  "recipes.generate.selecting",
  "recipes.generate.waitingSession",
  "recipes.generate.candidatesTitle",
  "recipes.generate.candidatesHint",
  "recipes.generate.minutes",
  "recipes.generate.servings",
  "recipes.generate.difficulty",
  "recipes.generate.emptyInventoryTitle",
  "recipes.generate.emptyInventoryDetail",
  "recipes.generate.cancelled",
  "recipes.error.providerUnavailable",
  "recipes.error.budgetUnavailable",
  "recipes.error.invalidOutput",
  "recipes.error.session",
  "recipes.error.unexpected",
  "recipes.error.loadSession",
] as const;

const en: Record<(typeof REQUIRED_RECIPES_I18N_KEYS)[number], string> = {
  "nav.recipes": "Recipes",
  "recipes.title": "Recipes",
  "recipes.subtitle":
    "Saved cook-now recipes from your inventory and preferences.",
  "recipes.loading": "Loading recipes…",
  "recipes.empty": "No saved recipes yet.",
  "recipes.empty.guidance":
    "Generate cook-now candidates from your pantry to save your first recipe.",
  "recipes.actions.generate": "Generate cook-now",
  "recipes.actions.retry": "Try again",
  "recipes.actions.back": "Back",
  "recipes.actions.cancel": "Cancel",
  "recipes.actions.confirmGenerate": "Request three candidates",
  "recipes.actions.selectCandidate": "Select and save",
  "recipes.actions.openInventory": "Open pantry",
  "recipes.list.error": "Unable to load recipes. Try again.",
  "recipes.detail.title": "Recipe",
  "recipes.detail.loading": "Loading recipe…",
  "recipes.detail.error": "Unable to load this recipe.",
  "recipes.detail.notFound": "Recipe not found.",
  "recipes.detail.servings": "Servings: {{count}}",
  "recipes.detail.revision": "Revision {{number}}",
  "recipes.detail.mealTypes": "Meal types",
  "recipes.detail.ingredients": "Ingredients",
  "recipes.detail.stages": "Steps",
  "recipes.detail.assumptions": "Assumptions",
  "recipes.detail.yield": "Yield: {{value}}",
  "recipes.generate.title": "Cook-now generation",
  "recipes.generate.subtitle":
    "Use current inventory and profile constraints to suggest three recipes.",
  "recipes.generate.confirmDetail":
    "KitchenFlow will read your on-hand pantry and preferences, then ask the AI gateway for exactly three validated candidates. Nothing is saved until you select one.",
  "recipes.generate.requesting": "Requesting candidates…",
  "recipes.generate.selecting": "Expanding and saving the selected recipe…",
  "recipes.generate.waitingSession": "Waiting for generation to finish…",
  "recipes.generate.candidatesTitle": "Choose one candidate",
  "recipes.generate.candidatesHint":
    "Three validated options. Selecting one expands and saves an owned recipe revision.",
  "recipes.generate.minutes": "{{count}} min",
  "recipes.generate.servings": "{{count}} servings",
  "recipes.generate.difficulty": "Difficulty: {{value}}",
  "recipes.generate.emptyInventoryTitle": "Pantry is empty",
  "recipes.generate.emptyInventoryDetail":
    "Add at least one pantry item before requesting cook-now candidates.",
  "recipes.generate.cancelled": "Request cancelled.",
  "recipes.error.providerUnavailable":
    "Recipe AI is unavailable right now. You can retry later.",
  "recipes.error.budgetUnavailable":
    "Recipe AI budget is unavailable. Try again later.",
  "recipes.error.invalidOutput":
    "The AI response could not be validated. Retry to request new candidates.",
  "recipes.error.session": "A valid session and CSRF token are required.",
  "recipes.error.unexpected": "Something went wrong. Try again.",
  "recipes.error.loadSession": "Unable to reload the generation session.",
};

const ptBR: Record<(typeof REQUIRED_RECIPES_I18N_KEYS)[number], string> = {
  "nav.recipes": "Receitas",
  "recipes.title": "Receitas",
  "recipes.subtitle":
    "Receitas cook-now salvas a partir da sua despensa e preferências.",
  "recipes.loading": "Carregando receitas…",
  "recipes.empty": "Nenhuma receita salva ainda.",
  "recipes.empty.guidance":
    "Gere candidatos cook-now a partir da despensa para salvar a primeira receita.",
  "recipes.actions.generate": "Gerar cook-now",
  "recipes.actions.retry": "Tentar de novo",
  "recipes.actions.back": "Voltar",
  "recipes.actions.cancel": "Cancelar",
  "recipes.actions.confirmGenerate": "Pedir três candidatos",
  "recipes.actions.selectCandidate": "Selecionar e salvar",
  "recipes.actions.openInventory": "Abrir despensa",
  "recipes.list.error": "Não foi possível carregar as receitas. Tente de novo.",
  "recipes.detail.title": "Receita",
  "recipes.detail.loading": "Carregando receita…",
  "recipes.detail.error": "Não foi possível carregar esta receita.",
  "recipes.detail.notFound": "Receita não encontrada.",
  "recipes.detail.servings": "Porções: {{count}}",
  "recipes.detail.revision": "Revisão {{number}}",
  "recipes.detail.mealTypes": "Tipos de refeição",
  "recipes.detail.ingredients": "Ingredientes",
  "recipes.detail.stages": "Passos",
  "recipes.detail.assumptions": "Assunções",
  "recipes.detail.yield": "Rendimento: {{value}}",
  "recipes.generate.title": "Geração cook-now",
  "recipes.generate.subtitle":
    "Use a despensa e as preferências atuais para sugerir três receitas.",
  "recipes.generate.confirmDetail":
    "O KitchenFlow lerá a despensa e as preferências e pedirá ao gateway de IA exatamente três candidatos validados. Nada é salvo até você selecionar um.",
  "recipes.generate.requesting": "Solicitando candidatos…",
  "recipes.generate.selecting": "Expandindo e salvando a receita selecionada…",
  "recipes.generate.waitingSession": "Aguardando a geração terminar…",
  "recipes.generate.candidatesTitle": "Escolha um candidato",
  "recipes.generate.candidatesHint":
    "Três opções validadas. Selecionar uma expande e salva uma revisão de receita sua.",
  "recipes.generate.minutes": "{{count}} min",
  "recipes.generate.servings": "{{count}} porções",
  "recipes.generate.difficulty": "Dificuldade: {{value}}",
  "recipes.generate.emptyInventoryTitle": "Despensa vazia",
  "recipes.generate.emptyInventoryDetail":
    "Adicione pelo menos um item na despensa antes de pedir candidatos cook-now.",
  "recipes.generate.cancelled": "Solicitação cancelada.",
  "recipes.error.providerUnavailable":
    "A IA de receitas está indisponível no momento. Você pode tentar depois.",
  "recipes.error.budgetUnavailable":
    "O orçamento da IA de receitas está indisponível. Tente mais tarde.",
  "recipes.error.invalidOutput":
    "A resposta da IA não pôde ser validada. Tente de novo para pedir novos candidatos.",
  "recipes.error.session": "É necessária uma sessão válida e um token CSRF.",
  "recipes.error.unexpected": "Algo deu errado. Tente de novo.",
  "recipes.error.loadSession":
    "Não foi possível recarregar a sessão de geração.",
};

const es: Record<(typeof REQUIRED_RECIPES_I18N_KEYS)[number], string> = {
  "nav.recipes": "Recetas",
  "recipes.title": "Recetas",
  "recipes.subtitle":
    "Recetas cook-now guardadas a partir de tu despensa y preferencias.",
  "recipes.loading": "Cargando recetas…",
  "recipes.empty": "Aún no hay recetas guardadas.",
  "recipes.empty.guidance":
    "Genera candidatos cook-now desde la despensa para guardar tu primera receta.",
  "recipes.actions.generate": "Generar cook-now",
  "recipes.actions.retry": "Reintentar",
  "recipes.actions.back": "Volver",
  "recipes.actions.cancel": "Cancelar",
  "recipes.actions.confirmGenerate": "Pedir tres candidatos",
  "recipes.actions.selectCandidate": "Seleccionar y guardar",
  "recipes.actions.openInventory": "Abrir despensa",
  "recipes.list.error":
    "No se pudieron cargar las recetas. Inténtalo de nuevo.",
  "recipes.detail.title": "Receta",
  "recipes.detail.loading": "Cargando receta…",
  "recipes.detail.error": "No se pudo cargar esta receta.",
  "recipes.detail.notFound": "Receta no encontrada.",
  "recipes.detail.servings": "Porciones: {{count}}",
  "recipes.detail.revision": "Revisión {{number}}",
  "recipes.detail.mealTypes": "Tipos de comida",
  "recipes.detail.ingredients": "Ingredientes",
  "recipes.detail.stages": "Pasos",
  "recipes.detail.assumptions": "Supuestos",
  "recipes.detail.yield": "Rendimiento: {{value}}",
  "recipes.generate.title": "Generación cook-now",
  "recipes.generate.subtitle":
    "Usa la despensa y las preferencias actuales para sugerir tres recetas.",
  "recipes.generate.confirmDetail":
    "KitchenFlow leerá tu despensa y preferencias y pedirá al gateway de IA exactamente tres candidatos validados. Nada se guarda hasta que selecciones uno.",
  "recipes.generate.requesting": "Solicitando candidatos…",
  "recipes.generate.selecting":
    "Expandiendo y guardando la receta seleccionada…",
  "recipes.generate.waitingSession": "Esperando a que termine la generación…",
  "recipes.generate.candidatesTitle": "Elige un candidato",
  "recipes.generate.candidatesHint":
    "Tres opciones validadas. Seleccionar una expande y guarda una revisión de receta tuya.",
  "recipes.generate.minutes": "{{count}} min",
  "recipes.generate.servings": "{{count}} porciones",
  "recipes.generate.difficulty": "Dificultad: {{value}}",
  "recipes.generate.emptyInventoryTitle": "Despensa vacía",
  "recipes.generate.emptyInventoryDetail":
    "Añade al menos un artículo a la despensa antes de pedir candidatos cook-now.",
  "recipes.generate.cancelled": "Solicitud cancelada.",
  "recipes.error.providerUnavailable":
    "La IA de recetas no está disponible ahora. Puedes reintentar más tarde.",
  "recipes.error.budgetUnavailable":
    "El presupuesto de IA de recetas no está disponible. Inténtalo más tarde.",
  "recipes.error.invalidOutput":
    "La respuesta de la IA no se pudo validar. Reintenta para pedir nuevos candidatos.",
  "recipes.error.session": "Se requiere una sesión válida y un token CSRF.",
  "recipes.error.unexpected": "Algo salió mal. Inténtalo de nuevo.",
  "recipes.error.loadSession":
    "No se pudo volver a cargar la sesión de generación.",
};

export const recipesCatalogs: Record<ProductionLocale, Catalog> = {
  en,
  "pt-BR": ptBR,
  es,
};
