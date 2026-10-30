import type { ProductionLocale } from "./productionCatalog";

/** Required keys for the public entry experience. */
export const REQUIRED_ENTRY_I18N_KEYS = [
  "entry.hero.eyebrow",
  "entry.hero.title",
  "entry.hero.lead",
  "entry.hero.notOnlyRecipes",
  "entry.cta.login",
  "entry.cta.secondary",
  "entry.demo.title",
  "entry.demo.step1",
  "entry.demo.step2",
  "entry.demo.step3",
  "entry.demo.step4",
  "entry.demo.step5",
  "entry.why.title",
  "entry.why.account",
  "entry.outcomes.title",
  "entry.outcomes.inventory",
  "entry.outcomes.decide",
  "entry.outcomes.attention",
  "entry.outcomes.plan",
  "entry.outcomes.adapt",
  "entry.legal.terms",
  "entry.legal.privacy",
  "entry.legal.placeholderNote",
] as const;

type Catalog = Record<string, string>;

export const entryCatalogs: Record<ProductionLocale, Catalog> = {
  "pt-BR": {
    "entry.hero.eyebrow": "Para quem quer cozinhar com o que tem",
    "entry.hero.title": "Cozinhe com o que você já tem",
    "entry.hero.lead":
      "Diga o que tem em casa e o Cocinaris sugere receitas, lembra do que precisa ser usado logo e monta a lista de compras do seu cardápio.",
    "entry.hero.notOnlyRecipes":
      "Não é só um gerador de receitas: tudo começa pela sua cozinha.",
    "entry.cta.login": "Criar conta ou entrar",
    "entry.cta.secondary": "Ver como funciona",
    "entry.demo.title": "Como funciona",
    "entry.demo.step1": "Anote o que tem na despensa, geladeira e freezer",
    "entry.demo.step2": "Veja o que precisa ser usado logo",
    "entry.demo.step3": "Receba três receitas que fazem sentido para hoje",
    "entry.demo.step4": "Cozinhe passo a passo, no seu ritmo",
    "entry.demo.step5":
      "Ao terminar, confirme o que usou e a despensa fica em dia",
    "entry.why.title": "Por que criar uma conta?",
    "entry.why.account":
      "Com uma conta, o Cocinaris lembra da sua despensa, das suas preferências e do seu cardápio, então cada sugestão já parte da sua cozinha.",
    "entry.outcomes.title": "O que muda na sua cozinha",
    "entry.outcomes.inventory":
      "Saber o que você tem, sem abrir todos os armários",
    "entry.outcomes.decide": "Decidir o que cozinhar em segundos",
    "entry.outcomes.attention":
      "Usar o que está perto da data antes de estragar",
    "entry.outcomes.plan": "Planejar a semana e comprar só o que falta",
    "entry.outcomes.adapt":
      "Receitas que respeitam suas restrições, seus equipamentos e seu tempo",
    "entry.legal.terms": "Termos de uso (em breve)",
    "entry.legal.privacy": "Privacidade (em breve)",
    "entry.legal.placeholderNote":
      "Os documentos completos serão publicados antes do lançamento.",
  },
  en: {
    "entry.hero.eyebrow": "For anyone who wants to cook with what they have",
    "entry.hero.title": "Cook with what you already have",
    "entry.hero.lead":
      "Tell Cocinaris what you have at home. It suggests recipes, reminds you what to use first, and builds the shopping list for your menu.",
    "entry.hero.notOnlyRecipes":
      "Not just a recipe generator: everything starts from your kitchen.",
    "entry.cta.login": "Create an account or sign in",
    "entry.cta.secondary": "See how it works",
    "entry.demo.title": "How it works",
    "entry.demo.step1": "Note what you have in the pantry, fridge, and freezer",
    "entry.demo.step2": "See what needs to be used soon",
    "entry.demo.step3": "Get three recipes that make sense for today",
    "entry.demo.step4": "Cook step by step, at your pace",
    "entry.demo.step5":
      "When you finish, confirm what you used and your pantry stays up to date",
    "entry.why.title": "Why create an account?",
    "entry.why.account":
      "With an account, Cocinaris remembers your pantry, preferences, and menu, so every suggestion starts from your kitchen.",
    "entry.outcomes.title": "What changes in your kitchen",
    "entry.outcomes.inventory":
      "Know what you have without opening every cupboard",
    "entry.outcomes.decide": "Decide what to cook in seconds",
    "entry.outcomes.attention":
      "Use what is close to its date before it spoils",
    "entry.outcomes.plan": "Plan the week and buy only what is missing",
    "entry.outcomes.adapt":
      "Recipes that respect your restrictions, equipment, and time",
    "entry.legal.terms": "Terms of use (coming soon)",
    "entry.legal.privacy": "Privacy (coming soon)",
    "entry.legal.placeholderNote":
      "The full documents will be published before launch.",
  },
  es: {
    "entry.hero.eyebrow": "Para quien quiere cocinar con lo que tiene",
    "entry.hero.title": "Cocina con lo que ya tienes",
    "entry.hero.lead":
      "Dile a Cocinaris lo que tienes en casa: sugiere recetas, te recuerda qué usar primero y arma la lista de compras de tu menú.",
    "entry.hero.notOnlyRecipes":
      "No es solo un generador de recetas: todo empieza en tu cocina.",
    "entry.cta.login": "Crear cuenta o iniciar sesión",
    "entry.cta.secondary": "Ver cómo funciona",
    "entry.demo.title": "Cómo funciona",
    "entry.demo.step1":
      "Anota lo que tienes en la despensa, la nevera y el congelador",
    "entry.demo.step2": "Ve qué hay que usar pronto",
    "entry.demo.step3": "Recibe tres recetas que tienen sentido para hoy",
    "entry.demo.step4": "Cocina paso a paso, a tu ritmo",
    "entry.demo.step5":
      "Al terminar, confirma lo que usaste y tu despensa queda al día",
    "entry.why.title": "¿Por qué crear una cuenta?",
    "entry.why.account":
      "Con una cuenta, Cocinaris recuerda tu despensa, tus preferencias y tu menú, así cada sugerencia parte de tu cocina.",
    "entry.outcomes.title": "Qué cambia en tu cocina",
    "entry.outcomes.inventory": "Saber qué tienes sin abrir todos los armarios",
    "entry.outcomes.decide": "Decidir qué cocinar en segundos",
    "entry.outcomes.attention":
      "Usar lo que está por vencer antes de que se estropee",
    "entry.outcomes.plan": "Planear la semana y comprar solo lo que falta",
    "entry.outcomes.adapt":
      "Recetas que respetan tus restricciones, tus equipos y tu tiempo",
    "entry.legal.terms": "Términos de uso (próximamente)",
    "entry.legal.privacy": "Privacidad (próximamente)",
    "entry.legal.placeholderNote":
      "Los documentos completos se publicarán antes del lanzamiento.",
  },
};
